import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { Download, ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import {
  checkIncomingInvoice, createParty, getDownloadIncomingInvoicePdfUrl, getDownloadIncomingInvoiceXmlUrl, getGetIncomingInvoiceQueryKey, getListIncomingInvoicesQueryKey,
  refreshIncomingInvoiceSupplier, rejectIncomingInvoice, replaceIncomingInvoiceWithXml, requestIncomingInvoiceXml, useGetIncomingInvoice, useGetIncomingInvoiceHistory,
} from '@/api/generated/bemaqui'
import type { IncomingInvoiceView } from '@/api/generated/model'
import { apiDownload, ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { describeChanges } from '@/components/history/describe-changes'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime, formatInstantDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { addRoles } from '@/pages/parties/party-actions'
import { PageHeading } from '@/shell/PageHeading'
import { DICAS, formatAccessKey, formatCnpj, INCOMING_STATUS, SEFAZ_TITLE } from './incoming'
import { READING_DICAS } from './reading'
import { IncomingItemsTab } from './IncomingItemsTab'
import { IncomingOrderTab } from './IncomingOrderTab'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'
const secondary = 'inline-flex min-h-11 items-center gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-50'

/**
 * Nota de entrada (FIS-04a, `/app/fiscal/entrada/$id`): aviso "Situação na SEFAZ não conferida", fornecedor pelo CNPJ (cadastrar com os
 * dados da nota ou adicionar o papel), abas Itens e de-para, Pedido e divergências, Documentos e Histórico; "Conferir nota" com o motivo
 * do bloqueio e "Recusar nota" com o motivo. Conferida, recebida ou recusada fica em modo leitura.
 */
export function IncomingInvoicePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetIncomingInvoice(id, { query: { retry: false } })
  const client = useQueryClient()
  const set = async (view: IncomingInvoiceView) => {
    client.setQueryData(getGetIncomingInvoiceQueryKey(id), view)
    await client.invalidateQueries({ queryKey: getListIncomingInvoicesQueryKey() })
  }

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-24" /><Skeleton className="h-40" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Nota de entrada não encontrada.' : undefined} message={notFound ? 'Volte para a lista de notas de entrada.' : undefined} onRetry={notFound ? undefined : () => void query.refetch()} />
  }

  return <InvoiceDetail view={query.data} onChange={set} />
}

function InvoiceDetail({ view, onChange }: { view: IncomingInvoiceView; onChange: (v: IncomingInvoiceView) => Promise<void> }) {
  const navigate = useNavigate()
  const [stepsShown, setStepsShown] = useState(false)
  const status = INCOMING_STATUS[view.status]
  const mark = view.sourceMark ?? null
  const editable = view.status === 'Imported' && view.can.import
  const steps: Step[] = stepsShown && view.status === 'Checked' ? [
    ...(view.can.receive && view.order.purchaseOrderId ? [{ label: 'Receber pela nota', onSelect: () => void navigate({ to: '/app/fiscal/entrada/$id/recebimento', params: { id: view.id } } as never) }] : []),
    { label: 'Importar outra nota', onSelect: () => void navigate({ to: '/app/fiscal/entrada/importar' }) },
    { label: 'Ver notas a conferir', onSelect: () => void navigate({ to: '/app/fiscal/entrada', search: { situacao: 'Imported' } } as never) },
  ] : []

  return (
    <>
      <PageHeading title={`NF-e ${view.number} · série ${view.series} — ${view.issuer.tradeName ?? view.issuer.name}`}>
        <div className="mt-2 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <StatusChip tone={status.tone}>{status.label}</StatusChip>
          <Money value={view.totals.total} className="font-semibold text-foreground" />
          <span>emitida em <span className="tabular-nums">{formatInstantDate(view.issuedAt)}</span></span>
          {mark ? (
            <>
              <StatusChip tone="warn">{mark}</StatusChip>
              <Dica nome={mark} texto={view.source === 'Manual' ? READING_DICAS.manual : READING_DICAS.pdf} />
            </>
          ) : (
            <>
              <StatusChip tone="ok"><ShieldCheck aria-hidden="true" className="size-3" />Assinatura conferida</StatusChip>
              <Dica nome="Assinatura da nota" texto={DICAS.assinatura} />
            </>
          )}
        </div>
      </PageHeading>

      {mark && view.status !== 'Rejected' ? <WithoutXml view={view} onChange={onChange} /> : null}
      {!mark && view.replacedByXmlAt ? <XmlDifferences view={view} /> : null}
      {!mark && !view.sefazStatusChecked && view.status !== 'Rejected' ? (
        <div role="note" className="mb-3 flex flex-wrap items-center gap-2 rounded-md border border-info-soft bg-info-soft p-3 text-sm text-info-ink">
          <b className="font-semibold">{SEFAZ_TITLE}</b>
          <Dica nome={SEFAZ_TITLE} texto={DICAS.sefaz} />
        </div>
      ) : null}
      {steps.length > 0 ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setStepsShown(false)} /></div> : null}
      {view.status === 'Rejected' ? (
        <div role="note" className="mb-3 rounded-md border border-crit-soft bg-crit-soft p-3 text-sm text-crit-ink">
          Recusada por {view.rejectedByName ?? 'usuário removido'} em {view.rejectedAt ? formatDateTime(view.rejectedAt) : '—'}: {view.rejectedReason}. A chave pode ser importada de novo.
        </div>
      ) : null}
      {view.status === 'Checked' || view.status === 'Received' ? (
        <p className="mb-3 text-sm text-muted-foreground">
          Conferida por {view.checkedByName ?? 'usuário removido'} em {view.checkedAt ? formatDateTime(view.checkedAt) : '—'}.
          {view.withoutOrder ? <> Sem pedido, autorizada por {view.withoutOrderApprovedByName ?? 'usuário removido'}: {view.withoutOrderReason}</> : null}
        </p>
      ) : null}
      {view.status === 'Received' && view.goodsReceiptId ? (
        <div role="note" className="mb-3 flex flex-wrap items-center gap-2 rounded-md border border-border bg-muted p-3 text-sm">
          <span>Recebida pelo recebimento do pedido: só para consulta.{view.inputTaxCreditApplied ? <> Regra de crédito usada: <b className="font-semibold">{view.inputTaxCreditApplied}</b>{view.inputTaxCreditAmount ? <> (crédito <Money value={view.inputTaxCreditAmount} />)</> : null}.</> : null}</span>
          <Link to={'/app/compras/recebimentos/$id' as never} params={{ id: view.goodsReceiptId } as never} className="font-semibold text-primary">Ver o recebimento</Link>
          <Link to={'/app/compras/recebimentos/$id' as never} params={{ id: view.goodsReceiptId } as never} search={{ aba: 'financeiro' } as never} className="font-semibold text-primary">Ver os títulos a pagar</Link>
        </div>
      ) : null}
      {view.status === 'Checked' && view.can.receive ? (
        <div className="mb-4 flex flex-wrap items-center gap-2">
          {view.order.purchaseOrderId
            ? <button type="button" onClick={() => void navigate({ to: '/app/fiscal/entrada/$id/recebimento', params: { id: view.id } } as never)} className={primary}>Receber pela nota</button>
            : <><button type="button" disabled aria-describedby="receber-sem-pedido" className={primary}>Receber pela nota</button><p id="receber-sem-pedido" className="text-sm text-muted-foreground">Ligue a nota a um pedido para receber.</p></>}
        </div>
      ) : null}

      <Supplier view={view} onChange={onChange} />

      {editable ? <Actions view={view} onChange={onChange} onChecked={() => setStepsShown(true)} /> : null}

      <UrlTabs label="Nota de entrada" tabs={[
        { id: 'itens', label: 'Itens e de-para', content: <IncomingItemsTab view={view} onChange={onChange} /> },
        { id: 'pedido', label: 'Pedido e divergências', content: <IncomingOrderTab view={view} onChange={onChange} /> },
        { id: 'documentos', label: 'Documentos', content: <Documents view={view} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <History invoiceId={view.id} /> },
      ]} />
    </>
  )
}

/** Fornecedor pelo CNPJ (CA 6): cadastrar com os dados da nota (parties.create) ou adicionar o papel (parties.edit). */
function Supplier({ view, onChange }: { view: IncomingInvoiceView; onChange: (v: IncomingInvoiceView) => Promise<void> }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const s = view.supplier
  const run = async (action: () => Promise<unknown>, done: string) => {
    setBusy(true)
    setError(null)
    try {
      await action()
      await onChange(await refreshIncomingInvoiceSupplier(view.id))
      notifySuccess(done)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível concluir. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  const a = view.issuer.address
  const create = () => createParty({
    kind: 'PJ', document: view.issuer.document, legalName: view.issuer.name, tradeName: view.issuer.tradeName ?? null, roles: ['Supplier'],
    stateRegistration: view.issuer.stateRegistration ?? null,
    billingAddress: a?.street && a.number && a.district && a.city && a.state && a.zipCode
      ? { kind: 'Billing', zipCode: a.zipCode, street: a.street, number: a.number, complement: a.complement ?? null, district: a.district, city: a.city, ibgeCode: a.ibgeCode ?? null, state: a.state, isDefault: true }
      : null,
  })

  return (
    <section aria-label="Fornecedor" className="mb-4 rounded-md border border-border bg-card p-3 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p>
          <b className="font-semibold">Fornecedor:</b> {s.name ?? view.issuer.name} · CNPJ <span className="tabular-nums">{formatCnpj(view.issuer.document)}</span>
          {s.state === 'Found' ? <> · <Link to="/app/cadastros/terceiros/$id" params={{ id: s.partyId! }} className="font-semibold text-primary">Abrir cadastro</Link></> : null}
        </p>
        {s.state === 'NotFound' ? <StatusChip tone="warn">Sem cadastro</StatusChip> : s.state === 'NotSupplier' ? <StatusChip tone="warn">Sem o papel Fornecedor</StatusChip> : <StatusChip tone="ok">Fornecedor cadastrado</StatusChip>}
      </div>
      {view.status === 'Imported' && s.state === 'NotFound' ? (
        view.can.createParty
          ? <button type="button" disabled={busy} onClick={() => void run(create, 'Fornecedor cadastrado')} className={`${primary} mt-2`}>Cadastrar fornecedor com os dados da nota</button>
          : <p className="mt-2 text-muted-foreground">Peça a quem cadastra terceiros para cadastrar o fornecedor com o CNPJ da nota.</p>
      ) : null}
      {view.status === 'Imported' && s.state === 'NotSupplier' ? (
        view.can.editParty
          ? <button type="button" disabled={busy} onClick={() => void run(() => addRoles(s.partyId!, ['Supplier']), 'Papel adicionado')} className={`${primary} mt-2`}>Adicionar papel</button>
          : <p className="mt-2 text-muted-foreground">O terceiro está cadastrado sem o papel Fornecedor. Peça a quem edita terceiros para adicionar o papel.</p>
      ) : null}
      {error ? <p role="alert" className="mt-2 text-crit-ink">{error}</p> : null}
    </section>
  )
}

/** "Conferir nota" (CA 11 e 12) com o motivo do bloqueio; sem pedido, o motivo da compra. "Recusar nota" com o motivo. */
function Actions({ view, onChange, onChecked }: { view: IncomingInvoiceView; onChange: (v: IncomingInvoiceView) => Promise<void>; onChecked: () => void }) {
  const [reason, setReason] = useState('')
  const [rejectReason, setRejectReason] = useState('')
  const blocked = view.checkBlockedReason
  return (
    <div className="mb-4 flex flex-wrap items-center gap-2">
      {blocked ? (
        <>
          <button type="button" disabled aria-describedby="conferir-motivo" className={primary}>Conferir nota</button>
          <p id="conferir-motivo" className="text-sm text-muted-foreground">{blocked}</p>
        </>
      ) : (
        <ConfirmDialog verb="Conferir nota" done="Nota conferida" size="lg" title={`Conferir a NF-e ${view.number}?`}
          description={view.withoutOrder
            ? 'A nota não tem pedido de compra: a conferência fica registrada com o seu nome e o motivo. Depois, o recebimento pode ser feito pela nota.'
            : 'As divergências contra o pedido ficam registradas e o de-para novo é lembrado para a próxima nota. Depois, o recebimento pode ser feito pela nota.'}
          onOpen={() => setReason('')}
          preconditions={view.withoutOrder ? [{ ok: reason.trim().length >= 3, text: 'Motivo da compra sem pedido escrito.' }] : []}
          onConfirm={async () => { await onChange(await checkIncomingInvoice(view.id, { withoutOrderReason: view.withoutOrder ? reason.trim() : null })); onChecked() }}
          trigger={<button type="button" className={primary}>Conferir nota</button>}>
          {view.withoutOrder ? (
            <div className="space-y-1">
              <label htmlFor="motivo-sem-pedido" className="text-sm font-medium">Motivo da compra sem pedido</label>
              <textarea id="motivo-sem-pedido" value={reason} maxLength={500} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />
            </div>
          ) : null}
        </ConfirmDialog>
      )}
      <ConfirmDialog verb="Recusar nota" done="Nota recusada" title={`Recusar a NF-e ${view.number}?`}
        description="A nota fica recusada com o motivo e não pode ser recebida. A mesma chave pode ser importada de novo depois."
        onOpen={() => setRejectReason('')} preconditions={[{ ok: rejectReason.trim().length >= 3, text: 'Motivo da recusa escrito.' }]}
        onConfirm={async () => onChange(await rejectIncomingInvoice(view.id, { reason: rejectReason.trim() }))}
        trigger={<button type="button" className={secondary}>Recusar nota</button>}>
        <div className="space-y-1">
          <label htmlFor="motivo-recusa" className="text-sm font-medium">Motivo</label>
          <textarea id="motivo-recusa" value={rejectReason} maxLength={500} rows={3} onChange={(e) => setRejectReason(e.target.value)} className={inputClass} />
        </div>
      </ConfirmDialog>
    </div>
  )
}

/**
 * Nota sem XML (FIS-05 CA 6, 8 e 10): sem assinatura nem protocolo; "Pedir o XML ao fornecedor" e "Substituir pelo XML" (toda a validação
 * da FIS-04a, a mesma chave; os valores conferidos ficam e as diferenças aparecem em texto).
 */
function WithoutXml({ view, onChange }: { view: IncomingInvoiceView; onChange: (v: IncomingInvoiceView) => Promise<void> }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [requested, setRequested] = useState<string | null>(view.xmlRequestedAt ? `XML pedido em ${formatDateTime(view.xmlRequestedAt)}.` : null)
  const replace = async (file: File | undefined) => {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      await onChange(await replaceIncomingInvoiceWithXml(view.id, { file }))
      notifySuccess('Nota substituída pelo XML')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível substituir pelo XML. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  const request = async () => {
    if (!view.readingId) return
    setBusy(true)
    setError(null)
    try {
      const sent = await requestIncomingInvoiceXml(view.readingId)
      setRequested(`Pedido enviado para ${sent.to}.`)
      notifySuccess('Pedido enviado')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível pedir o XML. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <div role="note" className="mb-3 space-y-2 rounded-md border border-warn-soft bg-warn-soft p-3 text-sm text-warn-ink">
      <p><b className="font-semibold">{view.sourceMark}.</b> Sem assinatura nem protocolo da SEFAZ para conferir. O XML continua sendo o documento fiscal: peça ao fornecedor e substitua quando chegar.</p>
      {view.can.import ? (
        <div className="flex flex-wrap items-center gap-2">
          {view.readingId ? <button type="button" disabled={busy} onClick={() => void request()} className={secondary}>Pedir o XML ao fornecedor</button> : null}
          <input id="substituir-xml" type="file" accept=".xml,application/xml,text/xml" className="sr-only" onChange={(e) => { void replace(e.target.files?.[0]); e.target.value = '' }} />
          <label htmlFor="substituir-xml" className={`${primary} cursor-pointer`}>Substituir pelo XML</label>
          {requested ? <span className="text-xs">{requested}</span> : null}
        </div>
      ) : null}
      {error ? <p role="alert" className="text-crit-ink">{error}</p> : null}
    </div>
  )
}

/** Diferenças do XML que chegou depois contra o que foi conferido (CA 10), em texto. */
function XmlDifferences({ view }: { view: IncomingInvoiceView }) {
  const diffs = view.xmlDifferences ?? []
  return (
    <div role="note" className={cn('mb-3 rounded-md border p-3 text-sm', diffs.length > 0 ? 'border-warn-soft bg-warn-soft text-warn-ink' : 'border-border bg-muted')}>
      <p>
        Substituída pelo XML em {view.replacedByXmlAt ? formatDateTime(view.replacedByXmlAt) : '—'}{view.replacedByXmlByName ? ` por ${view.replacedByXmlByName}` : ''}.{' '}
        {diffs.length === 0 ? 'Os valores do XML são iguais aos conferidos.' : 'Os valores conferidos ficaram; confira as diferenças com o XML e, se precisar, ajuste o recebimento e os títulos:'}
      </p>
      {diffs.length > 0 ? (
        <ul className="mt-1 list-disc pl-5">
          {diffs.map((d) => <li key={d.field}>{d.field}: conferido <b className="font-semibold tabular-nums">{d.checked}</b>, no XML <b className="font-semibold tabular-nums">{d.xml}</b></li>)}
        </ul>
      ) : null}
    </div>
  )
}

/** Documentos (CA 5 e 13): o XML guardado pelo sistema (ou o PDF da nota lida do PDF), a chave, o protocolo, quem assinou, totais e duplicatas. */
function Documents({ view }: { view: IncomingInvoiceView }) {
  const [error, setError] = useState<string | null>(null)
  const download = async () => {
    setError(null)
    try {
      await apiDownload(getDownloadIncomingInvoiceXmlUrl(view.id), `${view.accessKey}-nfe.xml`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível baixar o XML. Tente de novo.')
    }
  }
  const downloadPdf = async () => {
    setError(null)
    try {
      await apiDownload(getDownloadIncomingInvoicePdfUrl(view.id), `${view.accessKey}-danfe.pdf`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível baixar o PDF. Tente de novo.')
    }
  }

  const t = view.totals
  const totals: [string, number][] = [['Produtos', t.products], ['Frete', t.freight], ['Seguro', t.insurance], ['Desconto', t.discount], ['Outras despesas', t.otherExpenses], ['IPI', t.ipi], ['ICMS', t.icms], ['ICMS-ST', t.icmsSt], ['Total da nota', t.total]]
  return (
    <div className="space-y-4 text-sm">
      <dl className="grid gap-2 sm:grid-cols-2">
        <div><dt className="text-muted-foreground">Chave de acesso</dt><dd className="break-all font-semibold tabular-nums">{formatAccessKey(view.accessKey)}</dd></div>
        {view.xmlAttachmentId ? (
          <>
            <div><dt className="text-muted-foreground">Protocolo de autorização</dt><dd className="tabular-nums">{view.protocol}{view.authorizedAt ? ` · ${formatDateTime(view.authorizedAt)}` : ''}</dd></div>
            <div><dt className="flex items-center gap-1 text-muted-foreground">Assinada por<Dica nome="Assinatura da nota" texto={DICAS.assinatura} /></dt><dd className="tabular-nums">CNPJ {formatCnpj(view.signerDocument)}</dd></div>
          </>
        ) : (
          <div><dt className="text-muted-foreground">Origem</dt><dd>{view.sourceMark ?? '—'}</dd></div>
        )}
        <div><dt className="text-muted-foreground">Importada</dt><dd>{view.importedByName ?? 'usuário removido'} · {formatDateTime(view.importedAt)}</dd></div>
      </dl>
      <div className="flex flex-wrap gap-2">
        {view.xmlAttachmentId ? <button type="button" onClick={() => void download()} className={secondary}><Download aria-hidden="true" className="size-4" />Baixar XML</button> : null}
        {view.pdfAttachmentId ? <button type="button" onClick={() => void downloadPdf()} className={secondary}><Download aria-hidden="true" className="size-4" />Baixar PDF da DANFE</button> : null}
      </div>
      {error ? <p role="alert" className="mt-1 text-crit-ink">{error}</p> : null}
      <section aria-label="Totais">
        <h2 className="mb-1 font-semibold">Totais</h2>
        <dl className="grid grid-cols-2 gap-1 sm:grid-cols-3">
          {totals.map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd><Money value={value} /></dd></div>)}
        </dl>
      </section>
      <section aria-label="Duplicatas">
        <h2 className="mb-1 font-semibold">Duplicatas</h2>
        {view.duplicates.length === 0 ? <p className="text-muted-foreground">A nota não traz duplicatas.</p> : (
          <ul className="space-y-1">
            {view.duplicates.map((d) => (
              <li key={d.number} className="flex flex-wrap justify-between gap-2 rounded-md border border-border px-3 py-2">
                <span>Duplicata <span className="tabular-nums">{d.number}</span>{d.dueDate ? <> · vence em <span className="tabular-nums">{formatDate(d.dueDate)}</span></> : null}</span>
                <Money value={d.amount} className="font-semibold" />
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Removeu' }
const ENTITY: Record<string, string> = { IncomingInvoice: 'a nota', IncomingInvoiceItem: 'um item', IncomingInvoiceReading: 'a leitura do PDF' }

/** Histórico de alterações (CA 15): importação, fornecedor, de-para, pedido, conferência e recusa, com quem e quando. */
function History({ invoiceId }: { invoiceId: string }) {
  const [page, setPage] = useState(1)
  const query = useGetIncomingInvoiceHistory(invoiceId, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="A importação, o de-para, o pedido e a conferência aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>{ENTITY[entry.entity] ?? entry.entity}</span>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'Sistema'} · </span>
              <time className="tabular-nums text-muted-foreground" dateTime={entry.at}>{formatDateTime(entry.at)}</time>
            </p>
            <ul className="mt-1 text-muted-foreground">{describeChanges(entry.changes).map((line) => <li key={line}>{line}</li>)}</ul>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="mt-3 flex items-center justify-between text-xs text-muted-foreground">
          <span className="tabular-nums">Página {data.page} de {pages}</span>
          <span className="flex gap-1">
            <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="min-h-11 rounded-sm border border-border px-2 py-1 disabled:opacity-50">Anterior</button>
            <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="min-h-11 rounded-sm border border-border px-2 py-1 disabled:opacity-50">Próxima</button>
          </span>
        </div>
      ) : null}
    </div>
  )
}
