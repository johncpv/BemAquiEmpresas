import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import {
  discardNfse, getCheckNfseIssueQueryKey, getDownloadOutgoingInvoiceDanfeUrl, getDownloadOutgoingInvoiceXmlUrl, getGetNfseQueryKey, getListInstallationsToInvoiceQueryKey,
  getListOutgoingInvoicesQueryKey, issueNfse, registerNfse, registerNfseCancellation, reopenNfse, resendNfse, updateNfse, useCheckNfseIssue, useGetNfse,
  useGetOutgoingInvoiceHistory,
} from '@/api/generated/bemaqui'
import type { NfseView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
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
import { PageHeading } from '@/shell/PageHeading'
import { decimalField, isNfseSlow, NFSE_DICAS, NFSE_TEXTS, nfseLabel } from './nfse'
import { INVOICE_STATUS } from './outgoing-invoices'
import { TAX_LABEL } from './tax-rules'

const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-11 items-center rounded-md border border-border bg-card px-3 text-sm font-semibold disabled:opacity-60'

interface NfseDraft { description: string; sendToCustomer: boolean; emailCc: string }

const toDraft = (v: NfseView): NfseDraft => ({ description: v.service?.description ?? '', sendToCustomer: v.sendToCustomer, emailCc: v.emailCc ?? '' })

const today = () => new Date(Date.now() - 3 * 3_600_000).toISOString().slice(0, 10)

/**
 * NFS-e da instalação (/app/fiscal/saida/nfse/$id, FIS-06): o rascunho com o tomador, o serviço e o ISS pela regra (nada de imposto digitado),
 * "Emitir NFS-e" pelo Emissor quando a NFS-e do município está habilitada (ConfirmDialog grande com o impacto e as pré-condições, padrão 6) e,
 * até lá, o processo provisório: os valores para copiar no portal da prefeitura e "Registrar NFS-e emitida na prefeitura" (CA 5 e 6);
 * "Registrar cancelamento feito na prefeitura" com o 2FA (CA 9); Documentos e Histórico.
 */
export function NfsePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const client = useQueryClient()
  const navigate = useNavigate()
  const query = useGetNfse(id, { query: { refetchInterval: (q) => (q.state.data?.status === 'Processing' ? 5_000 : false) } })
  const [draft, setDraft] = useState<NfseDraft | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [warnings, setWarnings] = useState<string[]>([])
  const [now, setNow] = useState(() => Date.now())
  const [synced, setSynced] = useState<string | null>(null)
  const data = query.data
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
  if (query.isError || !data) return <ErrorState title="Não foi possível carregar a NFS-e." message="Ela pode não existir. Volte às notas de saída e tente de novo." onRetry={() => void query.refetch()} />
  const v = data
  const refresh = async (next?: NfseView) => {
    if (next) client.setQueryData(getGetNfseQueryKey(id), next)
    await client.invalidateQueries({ queryKey: getGetNfseQueryKey(id) })
    await client.invalidateQueries({ queryKey: getCheckNfseIssueQueryKey(id) })
    await client.invalidateQueries({ queryKey: getListOutgoingInvoicesQueryKey() })
    await client.invalidateQueries({ queryKey: getListInstallationsToInvoiceQueryKey() })
  }
  const save = async () => {
    if (!draft) return
    setSaving(true)
    setError(null)
    setFieldErrors({})
    try {
      const saved = await updateNfse(id, { description: draft.description, sendToCustomer: draft.sendToCustomer, emailCc: draft.emailCc || null, version: v.version })
      notifySuccess(NFSE_TEXTS.saved)
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
  const status = INVOICE_STATUS[v.status]
  const slow = isNfseSlow(v.status, v.processingSince, now)
  const registered = v.origin === 'PortalRegistered'
  const title = v.number ? `${nfseLabel(v.number)} — ${v.recipient.name ?? ''}` : `Rascunho de NFS-e — ${v.installationOrderNumber ?? ''}`

  return (
    <>
      <PageHeading title={title}>
        <p className="mt-2 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <StatusChip tone={status.tone}>{status.label}</StatusChip>
          {registered ? <span className="inline-flex items-center"><StatusChip tone="info">{NFSE_TEXTS.registeredOrigin}</StatusChip><Dica nome="Registrada (portal da prefeitura)" texto={NFSE_DICAS.registrada} /></span> : null}
          {v.projectId ? <Link to={'/app/projetos/$id' as never} params={{ id: v.projectId } as never} className="font-semibold text-primary tabular-nums hover:underline">{v.projectNumber}</Link> : null}
          {v.installationOrderId ? <Link to={'/app/producao/instalacoes/$id' as never} params={{ id: v.installationOrderId } as never} className="font-semibold text-primary tabular-nums hover:underline">{v.installationOrderNumber}</Link> : null}
          {v.linkedInvoiceId ? <Link to={'/app/fiscal/saida/$id' as never} params={{ id: v.linkedInvoiceId } as never} className="font-semibold text-primary hover:underline">{v.linkedInvoiceLabel}</Link> : null}
          <span>{v.environment === 'Production' ? 'Produção' : 'Homologação'}</span>
        </p>
      </PageHeading>
      <EnvironmentBanner service="sefaz" test={v.environment === 'Homologation'} message="Ambiente de homologação: a NFS-e registrada ou emitida aqui não conta na receita nem no custo do projeto." />
      {v.discardedAt ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Rascunho descartado em {formatDateTime(v.discardedAt)}. Só leitura.</p> : null}
      {v.status === 'Processing' ? (
        <p role="status" aria-live="polite" className={cn('mb-4 rounded-md border px-3 py-2 text-sm font-semibold', slow ? 'border-warn-soft bg-warn-soft text-warn-ink' : 'border-info-soft bg-info-soft text-info-ink')}>
          {slow ? <>Demorando. {NFSE_TEXTS.slow}</> : NFSE_TEXTS.waiting}
        </p>
      ) : null}
      {v.status === 'Rejected' ? (
        <div role="alert" className="mb-4 rounded-md border border-l-4 border-crit-soft border-l-crit bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <p className="font-semibold">Recusada pela prefeitura{v.rejectionCode ? ` (código ${v.rejectionCode})` : ''}</p>
          <p>{v.rejectionReason}</p>
          {v.suggestedAction ? <p className="mt-1"><b>O que fazer:</b> {v.suggestedAction}</p> : null}
          {v.actions.canReopen ? (
            <ConfirmDialog verb="Corrigir e emitir de novo" done={NFSE_TEXTS.reopened} title="Corrigir e emitir de novo?" description="A NFS-e volta a rascunho com os dados; a próxima emissão usa outra chave."
              onConfirm={async () => refresh(await reopenNfse(id, { version: v.version }))} trigger={<button type="button" className={cn(primary, 'mt-2')}>Corrigir e emitir de novo</button>} />
          ) : null}
        </div>
      ) : null}
      {v.status === 'Cancelled' && v.cancellation ? (
        <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">
          Cancelamento feito na prefeitura e registrado em {formatDateTime(v.cancellation.at)}{v.cancellation.byName ? ` por ${v.cancellation.byName}` : ''}: protocolo {v.cancellation.protocol}. {v.cancellation.reason}
        </p>
      ) : null}
      {warnings.length > 0 ? <ul role="status" className="mb-4 space-y-1 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{warnings.map((w) => <li key={w}>{w}</li>)}</ul> : null}
      {v.issWarning && warnings.length === 0 ? <p role="note" className="mb-4 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{v.issWarning}</p> : null}
      {v.status === 'Authorized' && v.projectId ? (
        <div className="mb-4"><NextSteps title="Próximos passos" onDismiss={() => undefined} steps={[
          { label: 'Ver projeto', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: v.projectId! }, search: { aba: 'resumo' } } as never) },
          { label: 'Ver a fila A emitir', onSelect: () => void navigate({ to: '/app/fiscal/saida' } as never) },
        ]} /></div>
      ) : null}
      {v.calculationError && v.actions.canEdit ? <p role="note" className="mb-4 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{v.calculationError}</p> : null}
      {error ? <p role="alert" className="mb-4 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
      {v.portalNotice ? <PortalArea invoice={v} onRegistered={async (next, w) => { setWarnings(w); await refresh(next) }} /> : null}
      <div className="mb-4 flex flex-wrap gap-2">
        {v.actions.canEdit ? <button type="button" disabled={saving} onClick={() => void save()} className={secondary}>{saving ? 'Salvando…' : 'Salvar rascunho'}</button> : null}
        {v.actions.canIssue ? <IssueDialog invoice={v} onDone={refresh} /> : null}
        {v.actions.canDiscard ? (
          <ConfirmDialog destructive verb="Descartar rascunho" done={NFSE_TEXTS.discarded} title="Descartar o rascunho?" description="O rascunho fica registrado, sem número. A instalação volta para A emitir."
            onConfirm={async () => refresh(await discardNfse(id, { version: v.version }))} trigger={<button type="button" className={secondary}>Descartar rascunho</button>} />
        ) : null}
        {v.actions.canRegisterCancellation ? <CancellationDialog invoice={v} onDone={refresh} /> : null}
      </div>
      <FactsBar label="Totais da NFS-e" facts={[
        { label: 'Valor do serviço', value: <span className="tabular-nums font-semibold">{formatMoney(v.totalAmount)}</span> },
        { label: 'ISS', value: <span className="tabular-nums">{formatMoney(v.service?.issAmount ?? 0)}{registered ? ` · ${NFSE_TEXTS.issTyped}` : ''}</span> },
        { label: 'Valor líquido', value: <span className="tabular-nums">{formatMoney(v.service?.netAmount ?? v.totalAmount)}</span> },
        { label: 'No custo do projeto', value: <span className="tabular-nums">{formatMoney(v.costTaxAmount)}</span> },
      ]} />
      {draft ? (
        <UrlTabs label="Seções da NFS-e" fallback="dados" tabs={[
          { id: 'dados', label: 'Dados', keepMounted: true, content: <NfseData invoice={v} draft={draft} setDraft={setDraft} errors={fieldErrors} /> },
          { id: 'documentos', label: 'Documentos', content: <NfseDocuments invoice={v} onDone={refresh} /> },
          { id: 'historico', label: 'Histórico de alterações', content: <NfseHistory id={v.id} /> },
        ]} />
      ) : null}
    </>
  )
}

function NfseData({ invoice: v, draft, setDraft, errors }: { invoice: NfseView; draft: NfseDraft; setDraft: (d: NfseDraft) => void; errors: Record<string, string> }) {
  const r = v.recipient
  const s = v.service
  const edit = v.actions.canEdit
  return (
    <div className="space-y-4">
      <section aria-labelledby="tomador" className="rounded-md border border-border bg-card p-3 text-sm">
        <h2 id="tomador" className="mb-1 font-semibold">Tomador</h2>
        <p className="font-semibold">{r.name ?? '—'}</p>
        <p className="tabular-nums">{r.document ?? 'Sem documento'}</p>
        <p>{[r.street, r.number, r.complement].filter(Boolean).join(', ')} — {r.district} — {r.cityName}/{r.state} — CEP {r.postalCode}</p>
        <p className="text-muted-foreground">E-mail de cobrança: {r.email ?? 'não informado'}</p>
        {v.partyId ? <Link to={'/app/terceiros/$id' as never} params={{ id: v.partyId } as never} className="mt-1 inline-block font-semibold text-primary hover:underline">Corrigir no cadastro do cliente</Link> : null}
      </section>
      <section aria-labelledby="servico" className="grid gap-2 rounded-md border border-border bg-card p-3 text-sm sm:grid-cols-2">
        <h2 id="servico" className="font-semibold sm:col-span-2">Serviço</h2>
        <p className="flex items-center">Item da LC 116: <b className="ml-1 tabular-nums">{s?.serviceItem ?? '—'}</b><Dica nome="Item da LC 116" texto={NFSE_DICAS.item} /></p>
        <p>Código do serviço (CodSrv): <b className="tabular-nums">{s?.municipalCode ?? 'a informar em Configurações → Fiscal'}</b></p>
        <p>CTN: <span className="tabular-nums">{s?.ctn ?? '—'}</span> · NBS: <span className="tabular-nums">{s?.nbs ?? '—'}</span></p>
        <p className="flex items-center">Município da prestação: <b className="ml-1">{s?.serviceCityName ? `${s.serviceCityName} (${s.serviceCityCode})` : 'informe o endereço da obra no projeto'}</b><Dica nome="Município da prestação" texto={NFSE_DICAS.municipio} /></p>
        <p className="tabular-nums">ISS: {s?.issRate ?? 0}% = {formatMoney(s?.issAmount ?? 0)}{v.issTyped != null ? ` (calculado pela regra: ${formatMoney(v.issCalculated)})` : ''}</p>
        <p className="flex items-center">Retenção do ISS: <b className="ml-1">{s?.issWithheld ? 'retido pelo tomador' : 'não retido'}</b><Dica nome="Retenção do ISS" texto={NFSE_DICAS.retencao} /></p>
        <ul className="grid gap-1 rounded-md bg-muted p-2 text-xs tabular-nums sm:col-span-2" aria-label="Tributos do serviço">
          {v.items.flatMap((i) => i.taxes.map((t) => <li key={`${i.id}-${t.code}`}>{TAX_LABEL[t.code] ?? t.code}: base {formatMoney(t.base)} × {t.rate}% = {formatMoney(t.amount)}{t.inProjectCost ? ' · custo do projeto' : ''}</li>))}
        </ul>
      </section>
      <div className="space-y-1 text-sm">
        <label htmlFor="discriminacao" className="font-medium">Discriminação do serviço (até 2000)</label>
        <textarea id="discriminacao" rows={3} maxLength={2000} disabled={!edit} value={draft.description} onChange={(e) => setDraft({ ...draft, description: e.target.value })} className={inputClass} />
        {errors.description ? <p role="alert" className="text-xs font-medium text-crit-ink">{errors.description}</p> : null}
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" disabled={!edit} checked={draft.sendToCustomer} onChange={(e) => setDraft({ ...draft, sendToCustomer: e.target.checked })} className="size-5" />
          Enviar ao cliente (a nota por e-mail depois de autorizada ou registrada)
        </label>
        <div className="space-y-1 text-sm">
          <label htmlFor="copia-nfse" className="font-medium">Cópia para (até 3 e-mails)</label>
          <input id="copia-nfse" type="text" disabled={!edit} value={draft.emailCc} onChange={(e) => setDraft({ ...draft, emailCc: e.target.value })} className={inputClass} />
          {errors.emailCc ? <p role="alert" className="text-xs font-medium text-crit-ink">{errors.emailCc}</p> : null}
        </div>
      </div>
    </div>
  )
}

/** Processo provisório (CA 5 e 6): o aviso, o link do portal, os valores para copiar e "Registrar NFS-e emitida na prefeitura". */
function PortalArea({ invoice: v, onRegistered }: { invoice: NfseView; onRegistered: (next: NfseView, warnings: string[]) => Promise<void> }) {
  const [copied, setCopied] = useState<string | null>(null)
  const copy = async (key: string, value: string) => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(key)
    } catch {
      setCopied(null)
    }
  }
  return (
    <section aria-labelledby="portal" className="mb-4 rounded-md border border-l-4 border-info-soft border-l-info bg-card p-3 text-sm">
      <h2 id="portal" className="font-semibold">Para copiar no portal da prefeitura</h2>
      <p className="mt-1">{v.portalNotice} <a href={v.portalUrl} target="_blank" rel="noopener noreferrer" className="font-semibold text-primary hover:underline">Abrir o portal da prefeitura</a></p>
      <dl className="mt-3 divide-y divide-border">
        {v.portalCopy.map((f) => (
          <div key={f.key} className="flex flex-wrap items-center justify-between gap-2 py-1">
            <dt className="text-muted-foreground">{f.label}</dt>
            <dd className="flex min-w-0 items-center gap-2">
              <span className="break-words tabular-nums">{f.value}</span>
              <button type="button" onClick={() => void copy(f.key, f.value)} className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-md border border-border px-2 text-xs font-semibold" aria-label={`Copiar ${f.label}`}>
                {copied === f.key ? 'Copiado' : 'Copiar'}
              </button>
            </dd>
          </div>
        ))}
      </dl>
      {v.actions.canRegister ? <RegisterDialog invoice={v} onDone={onRegistered} /> : null}
    </section>
  )
}

function RegisterDialog({ invoice: v, onDone }: { invoice: NfseView; onDone: (next: NfseView, warnings: string[]) => Promise<void> }) {
  const [number, setNumber] = useState('')
  const [code, setCode] = useState('')
  const [date, setDate] = useState(today())
  const [amount, setAmount] = useState('')
  const [iss, setIss] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const reset = () => {
    setNumber('')
    setCode('')
    setDate(today())
    setAmount(v.totalAmount.toFixed(2))
    setIss((v.service?.issAmount ?? 0).toFixed(2))
    setFile(null)
  }
  const ready = [
    { ok: /\d/.test(number), text: 'Informe o número da NFS-e como saiu na nota da prefeitura.' },
    { ok: code.trim().length > 0, text: 'Informe o código de verificação.' },
    { ok: decimalField(amount) !== '' && Number(decimalField(amount)) > 0, text: 'Informe o valor do serviço da nota.' },
    { ok: decimalField(iss) !== '', text: 'Informe o ISS da nota.' },
    { ok: !!file, text: 'Anexe o PDF ou o XML da nota da prefeitura.' },
  ]
  return (
    <ConfirmDialog size="lg" verb="Registrar NFS-e emitida na prefeitura" done={NFSE_TEXTS.registered} title="Registrar a NFS-e emitida na prefeitura?" onOpen={reset}
      description="Informe os valores como saíram na nota da prefeitura. A nota passa a contar no faturamento e no ISS do projeto pela data dela."
      trigger={<button type="button" className={cn(primary, 'mt-3')}>Registrar NFS-e emitida na prefeitura</button>}
      preconditions={ready}
      impact={<p>Valor {formatMoney(Number(decimalField(amount) || 0))} · ISS {formatMoney(Number(decimalField(iss) || 0))} (calculado pela regra: {formatMoney(v.service?.issAmount ?? 0)})</p>}
      onConfirm={async () => {
        const result = await registerNfse(v.id, { number, verificationCode: code.trim(), issuedOn: date, servicesAmount: decimalField(amount), issAmount: decimalField(iss), version: String(v.version), file: file! })
        await onDone(result.invoice, result.warnings)
      }}>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <div className="space-y-1 text-sm"><label htmlFor="nfse-numero" className="font-medium">Número da NFS-e</label><input id="nfse-numero" inputMode="numeric" value={number} onChange={(e) => setNumber(e.target.value)} className={inputClass} /></div>
        <div className="space-y-1 text-sm"><label htmlFor="nfse-codigo" className="font-medium">Código de verificação</label><input id="nfse-codigo" value={code} maxLength={60} onChange={(e) => setCode(e.target.value)} className={inputClass} /></div>
        <div className="space-y-1 text-sm"><label htmlFor="nfse-data" className="font-medium">Data de emissão</label><input id="nfse-data" type="date" max={today()} value={date} onChange={(e) => setDate(e.target.value)} className={inputClass} /></div>
        <div className="space-y-1 text-sm"><label htmlFor="nfse-valor" className="font-medium">Valor do serviço (R$)</label><input id="nfse-valor" type="number" min={0} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} className={inputClass} /></div>
        <div className="space-y-1 text-sm"><label htmlFor="nfse-iss" className="font-medium">ISS da nota (R$)</label><input id="nfse-iss" type="number" min={0} step="0.01" value={iss} onChange={(e) => setIss(e.target.value)} className={inputClass} /></div>
        <div className="space-y-1 text-sm"><label htmlFor="nfse-arquivo" className="font-medium">PDF ou XML da nota</label><input id="nfse-arquivo" type="file" accept="application/pdf,application/xml,text/xml,.pdf,.xml" onChange={(e) => setFile(e.target.files?.[0] ?? null)} className="block min-h-11 text-sm" /></div>
      </div>
    </ConfirmDialog>
  )
}

/** "Emitir NFS-e" (CA 3): ConfirmDialog grande com o impacto, as pré-condições com link e o ambiente. */
function IssueDialog({ invoice: v, onDone }: { invoice: NfseView; onDone: (next?: NfseView) => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()
  const check = useCheckNfseIssue(v.id, { query: { enabled: open } })
  const c = check.data
  const preconditions = c ? c.preconditions.filter((p) => !p.ok).map((p) => ({ ok: false, text: p.text, fix: p.link ? { label: 'Resolver', onSelect: () => void navigate({ to: p.link! } as never) } : undefined })) : [{ ok: false, text: 'Conferindo as pré-condições…' }]
  return (
    <ConfirmDialog size="lg" verb="Emitir NFS-e" done={NFSE_TEXTS.sent} title="Emitir a NFS-e?" open={open} onOpenChange={setOpen}
      description="A NFS-e vai para a prefeitura pelo Emissor Fiscal com os valores abaixo. Depois de autorizada, só o cancelamento muda a nota."
      trigger={<button type="button" className={primary}>Emitir NFS-e</button>}
      preconditions={preconditions}
      impact={c ? (
        <div className="space-y-1">
          <EnvironmentBanner service="sefaz" test={c.environment === 'Homologation'} className="mb-2" message="Ambiente de homologação: a NFS-e não tem valor fiscal." />
          <p><b>Tomador:</b> {c.takerName}</p>
          <p><b>Serviço:</b> {formatMoney(c.totalAmount)} · ISS {formatMoney(c.issAmount)}{c.service?.issWithheld ? ' (retido pelo tomador)' : ''}</p>
          <p><b>No custo do projeto {c.projectNumber ?? ''}:</b> {formatMoney(c.costTaxAmount)}{c.projectCostApplies ? '' : ' (homologação: não entra no custo nem na receita)'}</p>
          {c.warnings.map((w) => <p key={w} className="text-warn-ink">{w}</p>)}
        </div>
      ) : undefined}
      onConfirm={async () => {
        const result = await issueNfse(v.id, { version: c?.version ?? v.version })
        await onDone(result.invoice)
      }} />
  )
}

/** "Registrar cancelamento feito na prefeitura" (CA 9): motivo, protocolo e o código do autenticador (2FA de novo, D6). */
function CancellationDialog({ invoice: v, onDone }: { invoice: NfseView; onDone: (next?: NfseView) => Promise<void> }) {
  const [reason, setReason] = useState('')
  const [protocol, setProtocol] = useState('')
  const [code, setCode] = useState('')
  return (
    <ConfirmDialog size="lg" destructive verb="Registrar cancelamento feito na prefeitura" done={NFSE_TEXTS.cancelled} title="Registrar o cancelamento feito na prefeitura?"
      description={`${NFSE_TEXTS.cancelInPortal} A nota fica cancelada, o imposto sai do custo do projeto e nada é apagado.`}
      onOpen={() => { setReason(''); setProtocol(''); setCode('') }}
      trigger={<button type="button" className={secondary}>Registrar cancelamento feito na prefeitura</button>}
      preconditions={[
        { ok: reason.trim().length >= 15, text: 'Informe o motivo, com pelo menos 15 caracteres.' },
        { ok: protocol.trim().length > 0, text: 'Informe o protocolo do cancelamento na prefeitura.' },
        { ok: /^\d{6}$/.test(code), text: 'Informe o código de 6 dígitos do aplicativo autenticador.' },
      ]}
      impact={<p>{nfseLabel(v.number)} · {formatMoney(v.totalAmount)} · ISS {formatMoney(v.service?.issAmount ?? 0)} sai do custo do projeto.</p>}
      onConfirm={async () => onDone(await registerNfseCancellation(v.id, { reason: reason.trim(), protocol: protocol.trim(), code, version: v.version }))}>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <div className="space-y-1 text-sm sm:col-span-2"><label htmlFor="canc-motivo" className="font-medium">Motivo</label><textarea id="canc-motivo" rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} /></div>
        <div className="space-y-1 text-sm"><label htmlFor="canc-protocolo" className="font-medium">Protocolo da prefeitura</label><input id="canc-protocolo" maxLength={60} value={protocol} onChange={(e) => setProtocol(e.target.value)} className={inputClass} /></div>
        <div className="space-y-1 text-sm"><label htmlFor="canc-codigo" className="font-medium">Código do autenticador</label><input id="canc-codigo" inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} className={inputClass} /></div>
      </div>
    </ConfirmDialog>
  )
}

function NfseDocuments({ invoice: v, onDone }: { invoice: NfseView; onDone: (next?: NfseView) => Promise<void> }) {
  const [error, setError] = useState<string | null>(null)
  if (!v.actions.canDownload) return <EmptyState title="Sem documentos ainda." description="O XML e o PDF da NFS-e ficam aqui depois da autorização ou do registro da nota da prefeitura." />
  const name = `nfse-${v.number ?? v.id}`
  const download = async (xml: boolean) => {
    setError(null)
    try {
      await apiDownload(xml ? getDownloadOutgoingInvoiceXmlUrl(v.id) : getDownloadOutgoingInvoiceDanfeUrl(v.id), `${name}.${xml ? 'xml' : 'pdf'}`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível baixar.')
    }
  }
  return (
    <div className="space-y-3">
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
      <div className="flex flex-wrap gap-2">
        {v.xmlAttachmentId ? <button type="button" onClick={() => void download(true)} className={secondary}>Baixar XML</button> : null}
        {v.pdfAttachmentId ? <button type="button" onClick={() => void download(false)} className={secondary}>Baixar PDF</button> : null}
        {v.actions.canResend ? (
          <ConfirmDialog verb="Reenviar ao cliente" done={NFSE_TEXTS.resent} title="Reenviar a NFS-e ao cliente?" description={`Vai para ${v.recipient.email ?? 'o e-mail de cobrança'}${v.emailCc ? `, com cópia para ${v.emailCc}` : ''}.`}
            onConfirm={async () => onDone(await resendNfse(v.id))} trigger={<button type="button" className={secondary}>Reenviar ao cliente</button>} />
        ) : null}
      </div>
      <p className="text-sm text-muted-foreground">{v.emailedAt ? `Enviada ao cliente em ${formatDateTime(v.emailedAt)}.` : v.sendToCustomer ? 'Ainda não enviada ao cliente.' : 'Envio ao cliente desmarcado.'}</p>
      <dl className="grid gap-2 rounded-md border border-border bg-card p-3 text-sm sm:grid-cols-2">
        <div><dt className="font-medium">Número</dt><dd className="tabular-nums">{v.number ?? '—'}</dd></div>
        <div><dt className="font-medium">Código de verificação</dt><dd className="break-all tabular-nums">{v.verificationCode ?? '—'}</dd></div>
        <div><dt className="font-medium">Data da nota</dt><dd className="tabular-nums">{v.issuedOn ? formatDate(v.issuedOn) : '—'}</dd></div>
        <div><dt className="font-medium">Tentativa</dt><dd className="tabular-nums">{v.attempt}</dd></div>
      </dl>
    </div>
  )
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Excluiu' }
const ENTITY: Record<string, string> = { OutgoingInvoice: 'a NFS-e', OutgoingInvoiceItem: 'um serviço' }

function NfseHistory({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const query = useGetOutgoingInvoiceHistory(id, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => void query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="As mudanças na NFS-e aparecem aqui, com quem fez e quando." />
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
