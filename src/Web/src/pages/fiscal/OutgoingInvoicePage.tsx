import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import {
  discardOutgoingInvoice, getCheckOutgoingInvoiceIssueQueryKey, getDownloadOutgoingInvoiceDanfeUrl, getDownloadOutgoingInvoiceXmlUrl, getGetOutgoingInvoiceQueryKey,
  getListInvoicesToIssueQueryKey, getListOutgoingInvoicesQueryKey, getPreviewOutgoingInvoiceUrl, issueOutgoingInvoice, reopenOutgoingInvoice, resendOutgoingInvoice,
  updateOutgoingInvoice, useCheckOutgoingInvoiceIssue, useGetOutgoingInvoice, useGetOutgoingInvoiceHistory, useListInstallationsToInvoice,
} from '@/api/generated/bemaqui'
import type { OutgoingInvoiceItemView, OutgoingInvoiceView } from '@/api/generated/model'
import { ApiError, apiBlobUrl, apiDownload } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { describeChanges } from '@/components/history/describe-changes'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { FactsBar } from '@/components/ui/facts-bar'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime, formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { useStartNfse } from './use-start-nfse'
import { INVOICE_DICAS, INVOICE_STATUS, INVOICE_TEXTS, invoiceLabel, isSlow, quantity } from './outgoing-invoices'
import { OPERATION_LABEL, RECIPIENT_LABEL, TAX_LABEL } from './tax-rules'

const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-11 items-center rounded-md border border-border bg-card px-3 text-sm font-semibold disabled:opacity-60'

interface ItemDraft { id: string; quantity: number; unitPrice: number; discount: number; description: string; additionalInfo: string; taxRuleId: string; ipiRate: string }
interface InvoiceDraft { items: ItemDraft[]; freight: number; otherExpenses: number; additionalInformation: string; sendToCustomer: boolean; emailCc: string }

const toDraft = (v: OutgoingInvoiceView): InvoiceDraft => ({
  items: v.items.map((i) => ({ id: i.id, quantity: i.quantity, unitPrice: i.unitPrice, discount: i.discount, description: i.description, additionalInfo: i.additionalInfo ?? '', taxRuleId: i.chosenRuleId ?? '', ipiRate: i.ipiRate == null ? '' : String(i.ipiRate) })),
  freight: v.freight, otherExpenses: v.otherExpenses, additionalInformation: v.additionalInformation ?? '', sendToCustomer: v.sendToCustomer, emailCc: v.emailCc ?? '',
})

/**
 * Nota de saída (/app/fiscal/saida/$id, FIS-03a CA 3 a 10 e 14): o rascunho editável (sem campo de imposto), "Pré-visualizar" (DANFE com a
 * faixa "Prévia, sem valor fiscal" numa nova aba), "Emitir NF-e" com o ConfirmDialog grande (pré-condições com link e o impacto), o estado
 * de demora sem reemissão, a rejeitada com o motivo e "Corrigir e emitir de novo", a denegada com a orientação, os documentos e o histórico.
 */
export function OutgoingInvoicePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const client = useQueryClient()
  const navigate = useNavigate()
  const query = useGetOutgoingInvoice(id, { query: { refetchInterval: (q) => (q.state.data && (q.state.data.status === 'Processing' || q.state.data.status === 'ContingencyPending') ? 5_000 : false) } })
  const [draft, setDraft] = useState<InvoiceDraft | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [now, setNow] = useState(() => Date.now())
  const [synced, setSynced] = useState<string | null>(null)
  const data = query.data
  // FIS-06: depois do aceite técnico, a NF-e autorizada do equipamento oferece a NFS-e da instalação do mesmo projeto.
  const canInvoice = useCan('fiscal.create')
  const installations = useListInstallationsToInvoice({ query: { enabled: canInvoice && data?.status === 'Authorized' } })
  const nfse = useStartNfse()
  // O rascunho da tela segue a versão do servidor (salvar, emitir); a releitura da mesma versão (demora) não apaga o que está sendo digitado.
  const version = data ? `${data.id}:${data.version}` : null
  if (data && version !== synced) {
    setSynced(version)
    setDraft(toDraft(data))
  }
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 5_000)
    return () => window.clearInterval(timer)
  }, [])

  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !data) return <ErrorState title="Não foi possível carregar a nota." message="Ela pode não existir. Volte às notas de saída e tente de novo." onRetry={() => void query.refetch()} />
  const v = data
  const refresh = async (next?: OutgoingInvoiceView) => {
    if (next) client.setQueryData(getGetOutgoingInvoiceQueryKey(id), next)
    await client.invalidateQueries({ queryKey: getGetOutgoingInvoiceQueryKey(id) })
    await client.invalidateQueries({ queryKey: getCheckOutgoingInvoiceIssueQueryKey(id) })
    await client.invalidateQueries({ queryKey: getListOutgoingInvoicesQueryKey() })
    await client.invalidateQueries({ queryKey: getListInvoicesToIssueQueryKey() })
  }
  const body = (d: InvoiceDraft) => ({
    items: d.items.map((i) => ({ id: i.id, quantity: Number(i.quantity), unitPrice: Number(i.unitPrice), discount: Number(i.discount), description: i.description, additionalInfo: i.additionalInfo || null, taxRuleId: i.taxRuleId || null, ipiRate: i.ipiRate === '' ? null : Number(i.ipiRate) })),
    freight: Number(d.freight), otherExpenses: Number(d.otherExpenses), additionalInformation: d.additionalInformation || null, sendToCustomer: d.sendToCustomer, emailCc: d.emailCc || null, version: v.version,
  })
  const save = async () => {
    if (!draft) return
    setSaving(true)
    setError(null)
    setFieldErrors({})
    try {
      const saved = await updateOutgoingInvoice(id, body(draft))
      notifySuccess(INVOICE_TEXTS.saved)
      await refresh(saved)
    } catch (e) {
      if (e instanceof ApiError) {
        setFieldErrors(Object.fromEntries(Object.entries(e.fields).map(([k, msgs]) => [k, msgs[0]])))
        setError(e.message)
      } else setError('Não foi possível salvar. Tente de novo.')
    } finally {
      setSaving(false)
    }
  }
  const preview = async () => {
    setError(null)
    try {
      window.open(await apiBlobUrl(getPreviewOutgoingInvoiceUrl(id)), '_blank', 'noopener')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível montar a prévia.')
    }
  }
  const slow = isSlow(v.status, v.processingSince, now)
  const status = INVOICE_STATUS[v.status]
  const title = v.number ? `${invoiceLabel(v.series, v.number)} — ${v.recipient.name ?? ''}` : `Rascunho de nota — ${v.shipmentNumber ?? ''}`

  return (
    <>
      <PageHeading title={title}>
        <p className="mt-2 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <StatusChip tone={status.tone}>{status.label}</StatusChip>
          <span>{OPERATION_LABEL[v.operation]}</span>
          {v.projectId ? <Link to={'/app/projetos/$id' as never} params={{ id: v.projectId } as never} className="font-semibold text-primary tabular-nums hover:underline">{v.projectNumber}</Link> : null}
          {v.shipmentId ? <Link to={'/app/producao/expedicao/$id' as never} params={{ id: v.shipmentId } as never} className="font-semibold text-primary tabular-nums hover:underline">{v.shipmentNumber}</Link> : null}
          <span className="inline-flex items-center">{v.environment === 'Production' ? 'Produção' : 'Homologação'}<Dica nome="Ambiente" texto={INVOICE_DICAS.ambiente} /></span>
        </p>
      </PageHeading>
      <EnvironmentBanner service="sefaz" test={v.environment === 'Homologation'} />
      {v.discardedAt ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Rascunho descartado em {formatDateTime(v.discardedAt)}. Só leitura.</p> : null}
      {v.status === 'Processing' || v.status === 'ContingencyPending' ? (
        <p role="status" aria-live="polite" className={cn('mb-4 rounded-md border px-3 py-2 text-sm font-semibold', slow ? 'border-warn-soft bg-warn-soft text-warn-ink' : 'border-info-soft bg-info-soft text-info-ink')}>
          {slow ? <>Demorando. {INVOICE_TEXTS.slow}</> : INVOICE_TEXTS.waiting}
          {v.status === 'ContingencyPending' ? <Dica nome="Contingência" texto={INVOICE_DICAS.contingencia} /> : null}
        </p>
      ) : null}
      {v.status === 'Rejected' ? (
        <div role="alert" className="mb-4 rounded-md border border-l-4 border-crit-soft border-l-crit bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <p className="flex items-center font-semibold">Rejeitada pela SEFAZ{v.rejectionCode ? ` (código ${v.rejectionCode})` : ''}<Dica nome="Rejeitada" texto={INVOICE_DICAS.rejeitada} /></p>
          <p>{v.rejectionReason}</p>
          {v.suggestedAction ? <p className="mt-1"><b>O que fazer:</b> {v.suggestedAction}</p> : null}
          {v.actions.canReopen ? (
            <ConfirmDialog verb="Corrigir e emitir de novo" done={INVOICE_TEXTS.reopened} title="Corrigir e emitir de novo?" description="A nota volta a rascunho com os dados. O número gasto vai para Números a inutilizar."
              onConfirm={async () => refresh(await reopenOutgoingInvoice(id, { version: v.version }))} trigger={<button type="button" className={cn(primary, 'mt-2')}>Corrigir e emitir de novo</button>} />
          ) : null}
        </div>
      ) : null}
      {v.status === 'Denied' ? (
        <p role="alert" className="mb-4 flex items-center rounded-md border border-l-4 border-crit-soft border-l-crit bg-crit-soft px-3 py-2 text-sm font-semibold text-crit-ink">{v.guidance}<Dica nome="Denegada" texto={INVOICE_DICAS.denegada} /></p>
      ) : null}
      {v.status === 'Authorized' && v.projectId ? (
        <div className="mb-4"><NextSteps title="Próximos passos" onDismiss={() => undefined} steps={[
          { label: 'Ver projeto', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: v.projectId! }, search: { aba: 'resumo' } } as never) },
          { label: 'Ver a fila A emitir', onSelect: () => void navigate({ to: '/app/fiscal/saida' } as never) },
          ...(installations.data ?? []).filter((i) => i.projectId === v.projectId).slice(0, 1)
            .map((i) => ({ label: 'Emitir NFS-e da instalação', onSelect: () => void nfse.start(i.installationOrderId, i.projectId) })),
        ]} /></div>
      ) : null}
      {v.calculationError && v.actions.canEdit ? <p role="note" className="mb-4 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{v.calculationError}</p> : null}
      {error ? <p role="alert" className="mb-4 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
      <div className="mb-4 flex flex-wrap gap-2">
        {v.actions.canEdit ? <button type="button" disabled={saving} onClick={() => void save()} className={secondary}>{saving ? 'Salvando…' : 'Salvar rascunho'}</button> : null}
        {v.actions.canPreview ? <button type="button" onClick={() => void preview()} className={secondary}>Pré-visualizar</button> : null}
        {v.actions.canIssue ? <IssueDialog invoice={v} onDone={refresh} /> : null}
        {v.actions.canDiscard ? (
          <ConfirmDialog destructive verb="Descartar rascunho" done={INVOICE_TEXTS.discarded} title="Descartar o rascunho?" description="O rascunho sai da fila e fica registrado, sem número. O romaneio volta para A emitir."
            onConfirm={async () => refresh(await discardOutgoingInvoice(id, { version: v.version }))} trigger={<button type="button" className={secondary}>Descartar rascunho</button>} />
        ) : null}
      </div>
      <FactsBar label="Totais da nota" facts={[
        { label: 'Produtos', value: <span className="tabular-nums">{formatMoney(v.totals.products)}</span> },
        { label: 'Impostos', value: <span className="tabular-nums">{formatMoney(v.totals.taxes.filter((t) => t.code !== 'DIFAL').reduce((s, t) => s + t.amount, 0))}</span> },
        { label: 'Total da nota', value: <span className="tabular-nums font-semibold">{formatMoney(v.totalAmount)}</span> },
        { label: 'No custo do projeto', value: <span className="tabular-nums">{formatMoney(v.costTaxAmount)}</span> },
      ]} />
      {draft ? (
        <UrlTabs label="Seções da nota" fallback="dados" tabs={[
          { id: 'dados', label: 'Dados', keepMounted: true, content: <InvoiceData invoice={v} draft={draft} setDraft={setDraft} errors={fieldErrors} /> },
          { id: 'itens', label: 'Itens e tributos', keepMounted: true, content: <InvoiceItems invoice={v} draft={draft} setDraft={setDraft} errors={fieldErrors} /> },
          { id: 'documentos', label: 'Documentos', content: <InvoiceDocuments invoice={v} onDone={refresh} /> },
          { id: 'eventos', label: 'Eventos', content: <InvoiceEvents invoice={v} /> },
          { id: 'historico', label: 'Histórico de alterações', content: <InvoiceHistory id={v.id} /> },
        ]} />
      ) : null}
    </>
  )
}

function Field({ label, error, children, id }: { label: React.ReactNode; error?: string; children: React.ReactNode; id: string }) {
  return (
    <div className="space-y-1 text-sm">
      <label htmlFor={id} className="flex items-center font-medium">{label}</label>
      {children}
      {error ? <p id={id + '-erro'} role="alert" className="text-xs font-medium text-crit-ink">{error}</p> : null}
    </div>
  )
}

function InvoiceData({ invoice: v, draft, setDraft, errors }: { invoice: OutgoingInvoiceView; draft: InvoiceDraft; setDraft: (d: InvoiceDraft) => void; errors: Record<string, string> }) {
  const r = v.recipient
  const edit = v.actions.canEdit
  return (
    <div className="space-y-4">
      <section aria-labelledby="destinatario" className="rounded-md border border-border bg-card p-3 text-sm">
        <h2 id="destinatario" className="mb-1 font-semibold">Destinatário</h2>
        <p className="font-semibold">{r.name ?? '—'}</p>
        <p className="tabular-nums">{r.document ?? 'Sem documento'} · {RECIPIENT_LABEL[r.type]}{r.stateRegistration ? ` · IE ${r.stateRegistration}` : ''}</p>
        <p>{[r.street, r.number, r.complement].filter(Boolean).join(', ')} — {r.district} — {r.cityName}/{r.state} — CEP {r.postalCode} · IBGE {r.cityCode ?? '—'}</p>
        <p className="text-muted-foreground">E-mail de cobrança: {r.email ?? 'não informado'}</p>
        {errors['recipient.name'] ? <p role="alert" className="text-xs font-medium text-crit-ink">{errors['recipient.name']}</p> : null}
        {v.partyId ? <Link to={'/app/terceiros/$id' as never} params={{ id: v.partyId } as never} className="mt-1 inline-block font-semibold text-primary hover:underline">Corrigir no cadastro do cliente</Link> : null}
      </section>
      <section aria-labelledby="transporte" className="grid gap-3 rounded-md border border-border bg-card p-3 sm:grid-cols-2">
        <h2 id="transporte" className="font-semibold sm:col-span-2">Transporte e valores</h2>
        <p className="text-sm">Transportadora: {v.carrierName ?? '—'} · {v.volumes ?? '—'} {v.volumeSpecies?.toLowerCase() ?? 'volumes'} · {v.grossWeight != null ? `${quantity(v.grossWeight)} kg` : '—'}</p>
        <p className="text-sm">Modalidade do frete: {v.freightMode}</p>
        <Field id="frete" label="Frete (R$)" error={errors.freight}>
          <input id="frete" type="number" min={0} step="0.01" disabled={!edit} value={draft.freight} onChange={(e) => setDraft({ ...draft, freight: Number(e.target.value) })} className={inputClass} />
        </Field>
        <Field id="outras" label="Outras despesas (R$)">
          <input id="outras" type="number" min={0} step="0.01" disabled={!edit} value={draft.otherExpenses} onChange={(e) => setDraft({ ...draft, otherExpenses: Number(e.target.value) })} className={inputClass} />
        </Field>
      </section>
      <Field id="infcpl" label="Informações complementares" error={errors.additionalInformation}>
        <textarea id="infcpl" rows={4} maxLength={5000} disabled={!edit} value={draft.additionalInformation} onChange={(e) => setDraft({ ...draft, additionalInformation: e.target.value })} className={inputClass} />
      </Field>
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" disabled={!edit} checked={draft.sendToCustomer} onChange={(e) => setDraft({ ...draft, sendToCustomer: e.target.checked })} className="size-5" />
          Enviar ao cliente (XML e DANFE por e-mail depois da autorização)
        </label>
        <Field id="copia" label="Cópia para (até 3 e-mails)" error={errors.emailCc}>
          <input id="copia" type="text" disabled={!edit} value={draft.emailCc} onChange={(e) => setDraft({ ...draft, emailCc: e.target.value })} className={inputClass} />
        </Field>
      </div>
      <section aria-labelledby="parcelas" className="rounded-md border border-border bg-card p-3 text-sm">
        <h2 id="parcelas" className="mb-1 flex items-center font-semibold">Parcelas do contrato<Dica nome="Parcelas do contrato" texto={INVOICE_DICAS.parcelas} /></h2>
        {v.installments.length === 0 ? <p className="text-muted-foreground">Sem parcelas no contrato.</p> : (
          <ul className="divide-y divide-border">
            {v.installments.map((p) => (
              <li key={p.id} className="flex flex-wrap justify-between gap-2 py-1 tabular-nums">
                <Link to={'/app/financeiro/receber/$id' as never} params={{ id: p.id } as never} className="text-primary hover:underline">{p.number}</Link>
                <span>{p.description}</span>
                <span>{p.dueDate ? formatDate(p.dueDate) : 'Sem data'}</span>
                <span>{formatMoney(p.amount)}</span>
              </li>
            ))}
          </ul>
        )}
        <p className="mt-1 text-xs text-muted-foreground">{INVOICE_TEXTS.installments} A nota não traz duplicatas.</p>
      </section>
    </div>
  )
}

function InvoiceItems({ invoice: v, draft, setDraft, errors }: { invoice: OutgoingInvoiceView; draft: InvoiceDraft; setDraft: (d: InvoiceDraft) => void; errors: Record<string, string> }) {
  if (v.items.length === 0) return <EmptyState title="Nota sem itens." />
  const edit = v.actions.canEdit
  const set = (index: number, patch: Partial<ItemDraft>) => setDraft({ ...draft, items: draft.items.map((x, i) => (i === index ? { ...x, ...patch } : x)) })
  return (
    <ul className="space-y-3" aria-label="Itens da nota">
      {v.items.map((item: OutgoingInvoiceItemView, index) => {
        const d = draft.items[index]
        const err = (f: string) => errors[`items[${index}].${f}`]
        return (
          <li key={item.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span><b className="font-semibold">{item.sequence}. {item.code}</b> · NCM <span className="tabular-nums">{item.ncm ?? '—'}</span> · CFOP <span className="tabular-nums">{item.cfop ?? '—'}</span></span>
              <span className="tabular-nums font-semibold">{formatMoney(item.total)}</span>
            </div>
            <div className="mt-2 grid gap-3 sm:grid-cols-2">
              <Field id={`desc-${item.id}`} label="Descrição (até 120)" error={err('description')}>
                <input id={`desc-${item.id}`} disabled={!edit} value={d.description} onChange={(e) => set(index, { description: e.target.value })} className={inputClass} />
              </Field>
              <Field id={`regra-${item.id}`} label={<>Regra aplicada<Dica nome="Regra aplicada" texto={INVOICE_DICAS.regra} /></>} error={err('taxRuleId')}>
                {edit ? (
                  <select id={`regra-${item.id}`} value={d.taxRuleId} onChange={(e) => set(index, { taxRuleId: e.target.value })} className={inputClass}>
                    <option value="">Automática{item.taxRuleName && !item.chosenRuleId ? ` (${item.taxRuleName})` : ''}</option>
                    {item.ruleOptions.map((o) => <option key={o.ruleId} value={o.ruleId}>{o.name} · v{o.version}{o.confirmed ? '' : ' · a confirmar'}</option>)}
                  </select>
                ) : <p id={`regra-${item.id}`}>{item.taxRuleName ?? '—'}{item.taxRuleVersion ? ` · versão ${item.taxRuleVersion}` : ''}</p>}
              </Field>
              <Field id={`qtd-${item.id}`} label={`Quantidade (expedido ${quantity(item.shippedQuantity)} ${item.unit})`} error={err('quantity')}>
                <input id={`qtd-${item.id}`} type="number" min={0} step="0.0001" disabled={!edit} value={d.quantity} onChange={(e) => set(index, { quantity: Number(e.target.value) })} className={inputClass} />
              </Field>
              <Field id={`preco-${item.id}`} label="Valor unitário (R$)">
                <input id={`preco-${item.id}`} type="number" min={0} step="0.01" disabled={!edit} value={d.unitPrice} onChange={(e) => set(index, { unitPrice: Number(e.target.value) })} className={inputClass} />
              </Field>
              <Field id={`desconto-${item.id}`} label="Desconto (R$)" error={err('discount')}>
                <input id={`desconto-${item.id}`} type="number" min={0} step="0.01" disabled={!edit} value={d.discount} onChange={(e) => set(index, { discount: Number(e.target.value) })} className={inputClass} />
              </Field>
              {item.needsIpiRate || d.ipiRate !== '' ? (
                <Field id={`ipi-${item.id}`} label="Alíquota do IPI na TIPI do NCM (%)" error={err('ipiRate')}>
                  <input id={`ipi-${item.id}`} type="number" min={0} max={100} step="0.01" disabled={!edit} value={d.ipiRate} onChange={(e) => set(index, { ipiRate: e.target.value })} className={inputClass} />
                </Field>
              ) : null}
              <div className="sm:col-span-2">
                <Field id={`info-${item.id}`} label="Informações do item (até 500)" error={err('additionalInfo')}>
                  <textarea id={`info-${item.id}`} rows={2} disabled={!edit} value={d.additionalInfo} onChange={(e) => set(index, { additionalInfo: e.target.value })} className={inputClass} />
                </Field>
              </div>
            </div>
            {item.taxes.length > 0 ? (
              <ul className="mt-2 grid gap-1 rounded-md bg-muted p-2 text-xs tabular-nums sm:grid-cols-2" aria-label={`Tributos do item ${item.sequence}`}>
                {item.taxes.map((t) => (
                  <li key={t.code} className={cn(t.code === 'DIFAL' && 'font-semibold text-crit-ink')}>
                    {TAX_LABEL[t.code] ?? t.code}{t.cst ? ` (CST ${t.cst})` : ''}: base {formatMoney(t.base)} × {t.rate}% = {formatMoney(t.amount)}{t.inProjectCost ? ' · custo do projeto' : ''}
                  </li>
                ))}
              </ul>
            ) : <p className="mt-2 text-xs text-muted-foreground">Sem tributos calculados ainda.</p>}
          </li>
        )
      })}
    </ul>
  )
}

/** "Emitir NF-e" (CA 6): ConfirmDialog grande com o impacto, as pré-condições com link e o ambiente. */
function IssueDialog({ invoice: v, onDone }: { invoice: OutgoingInvoiceView; onDone: (next?: OutgoingInvoiceView) => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()
  const check = useCheckOutgoingInvoiceIssue(v.id, { query: { enabled: open } })
  const c = check.data
  const preconditions = c ? c.preconditions.filter((p) => !p.ok).map((p) => ({ ok: false, text: p.text, fix: p.link ? { label: 'Resolver', onSelect: () => void navigate({ to: p.link! } as never) } : undefined })) : [{ ok: false, text: 'Conferindo as pré-condições…' }]
  return (
    <ConfirmDialog size="lg" verb="Emitir NF-e" done={INVOICE_TEXTS.sent} title="Emitir a NF-e?" open={open} onOpenChange={setOpen}
      description="A nota vai para a SEFAZ com os valores abaixo. Depois de autorizada, só o cancelamento muda a nota."
      trigger={<button type="button" className={primary}>Emitir NF-e</button>}
      preconditions={preconditions}
      impactList={c ? {
        label: 'Itens da nota',
        items: v.items.map((i) => ({ key: i.id, primary: `${i.sequence}. ${i.description}`, secondary: `${quantity(i.quantity)} ${i.unit} × ${formatMoney(i.unitPrice)} · CFOP ${i.cfop ?? '—'}` })),
        total: formatMoney(c.totalAmount),
      } : undefined}
      impact={c ? (
        <div className="space-y-1">
          <EnvironmentBanner service="sefaz" test={c.environment === 'Homologation'} className="mb-2" />
          <p><b>Destinatário:</b> {c.recipientName}</p>
          <p><b>Impostos:</b> {c.totals.taxes.filter((t) => t.code !== 'DIFAL').map((t) => `${TAX_LABEL[t.code] ?? t.code} ${formatMoney(t.amount)}`).join(' · ')}</p>
          <p><b>No custo do projeto {c.projectNumber ?? ''}:</b> {formatMoney(c.costTaxAmount)}{c.projectCostApplies ? '' : ' (homologação: não entra no custo nem na receita)'}</p>
          {c.warnings.map((w) => <p key={w} className="text-warn-ink">{w}</p>)}
        </div>
      ) : undefined}
      onConfirm={async () => {
        const result = await issueOutgoingInvoice(v.id, { version: c?.version ?? v.version })
        await onDone(result.invoice)
      }} />
  )
}

function InvoiceDocuments({ invoice: v, onDone }: { invoice: OutgoingInvoiceView; onDone: (next?: OutgoingInvoiceView) => Promise<void> }) {
  const [error, setError] = useState<string | null>(null)
  if (!v.actions.canDownload) return <EmptyState title="Sem documentos ainda." description="O XML e o DANFE ficam aqui depois da autorização da SEFAZ." />
  const name = v.accessKey ?? v.id
  const download = async (xml: boolean) => {
    setError(null)
    try {
      await apiDownload(xml ? getDownloadOutgoingInvoiceXmlUrl(v.id) : getDownloadOutgoingInvoiceDanfeUrl(v.id), `${name}-${xml ? 'nfe.xml' : 'danfe.pdf'}`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível baixar.')
    }
  }
  return (
    <div className="space-y-3">
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
      <div className="flex flex-wrap gap-2">
        {v.xmlAttachmentId ? <button type="button" onClick={() => void download(true)} className={secondary}>Baixar XML</button> : null}
        {v.danfeAttachmentId ? <button type="button" onClick={() => void download(false)} className={secondary}>Baixar DANFE</button> : null}
        {v.actions.canResend ? (
          <ConfirmDialog verb="Reenviar XML e DANFE" done={INVOICE_TEXTS.resent} title="Reenviar o XML e o DANFE ao cliente?" description={`Vai para ${v.recipient.email ?? 'o e-mail de cobrança'}${v.emailCc ? `, com cópia para ${v.emailCc}` : ''}.`}
            onConfirm={async () => onDone(await resendOutgoingInvoice(v.id))} trigger={<button type="button" className={secondary}>Reenviar XML e DANFE</button>} />
        ) : null}
      </div>
      <p className="text-sm text-muted-foreground">{v.emailedAt ? `Enviados ao cliente em ${formatDateTime(v.emailedAt)}.` : v.sendToCustomer ? 'Ainda não enviados ao cliente.' : 'Envio ao cliente desmarcado.'}</p>
    </div>
  )
}

function InvoiceEvents({ invoice: v }: { invoice: OutgoingInvoiceView }) {
  return (
    <dl className="grid gap-2 rounded-md border border-border bg-card p-3 text-sm sm:grid-cols-2">
      <div><dt className="font-medium">Tentativa</dt><dd className="tabular-nums">{v.attempt}</dd></div>
      <div><dt className="flex items-center font-medium">Chave de acesso<Dica nome="Chave de acesso" texto={INVOICE_DICAS.chave} /></dt><dd className="break-all tabular-nums">{v.accessKey ?? '—'}</dd></div>
      <div><dt className="font-medium">Protocolo</dt><dd className="tabular-nums">{v.protocol ?? '—'}</dd></div>
      <div><dt className="font-medium">Autorizada em</dt><dd className="tabular-nums">{v.authorizedAt ? formatDateTime(v.authorizedAt) : '—'}</dd></div>
      <div><dt className="font-medium">Data da nota</dt><dd className="tabular-nums">{v.issuedOn ? formatDate(v.issuedOn) : '—'}</dd></div>
      <div><dt className="font-medium">Rejeição</dt><dd>{v.rejectionCode ? `${v.rejectionCode} — ${v.rejectionReason ?? ''}` : '—'}</dd></div>
      <p className="text-xs text-muted-foreground sm:col-span-2">Cancelamento e carta de correção entram aqui numa próxima versão.</p>
    </dl>
  )
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Excluiu' }
const ENTITY: Record<string, string> = { OutgoingInvoice: 'a nota', OutgoingInvoiceItem: 'um item' }

function InvoiceHistory({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const query = useGetOutgoingInvoiceHistory(id, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => void query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="As mudanças na nota aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>{ENTITY[entry.entity] ?? entry.entity}</span>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'sistema'} · </span>
              <time className="tabular-nums text-muted-foreground" dateTime={entry.at}>{formatDateTime(entry.at)}</time>
            </p>
            <ul className="mt-1 text-muted-foreground">{describeChanges(entry.changes).map((line) => <li key={line}>{line}</li>)}</ul>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="mt-3 flex items-center gap-2 text-sm">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="min-h-11 rounded-md border border-border px-3 font-semibold disabled:opacity-50">Anterior</button>
          <span className="tabular-nums">{page} de {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="min-h-11 rounded-md border border-border px-3 font-semibold disabled:opacity-50">Próxima</button>
        </div>
      ) : null}
    </div>
  )
}
