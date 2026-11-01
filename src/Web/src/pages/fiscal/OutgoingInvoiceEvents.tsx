import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { useId, useState } from 'react'
import {
  cancelOutgoingInvoice, correctOutgoingInvoice, createOutgoingInvoice, declineOutgoingInvoiceCancellation, getDownloadOutgoingInvoiceEventXmlUrl, getListOutgoingInvoiceEventsQueryKey,
  requestOutgoingInvoiceCancellation, useListOutgoingInvoiceEvents,
} from '@/api/generated/bemaqui'
import type { CancellationRequestView, FiscalEventResultView, OutgoingInvoiceView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { remainingText } from '@/components/ui/remaining-text'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime, formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { correctionWarnings, EVENT_DICAS, EVENT_STATUS, EVENT_TEXTS, INVOICE_DICAS, invoiceLabel } from './outgoing-invoices'

const secondary = 'inline-flex min-h-11 items-center rounded-md border border-border bg-card px-3 text-sm font-semibold disabled:opacity-60'
const danger = 'inline-flex min-h-11 items-center rounded-md border border-crit-soft bg-card px-3 text-sm font-semibold text-crit-ink disabled:opacity-60'

type Done = (next?: OutgoingInvoiceView) => Promise<void>

/** Comprimento do texto como o servidor conta (espaços normalizados, sem as pontas). */
const cleanLength = (text: string) => text.replace(/\s+/g, ' ').trim().length

/** Campo de texto do diálogo (motivo, correção, justificativa) com o contador e o erro do servidor; o rótulo não leva Dica dentro. */
function ReasonField({ label, value, onChange, max, min = 15, error, rows = 3 }: { label: string; value: string; onChange: (v: string) => void; max: number; min?: number; error?: string | null; rows?: number }) {
  const id = useId()
  const length = cleanLength(value)
  return (
    <div className="space-y-1 text-sm">
      <label htmlFor={id} className="font-semibold">{label}</label>
      <textarea id={id} rows={rows} maxLength={max + 50} value={value} onChange={(e) => onChange(e.target.value)} aria-invalid={!!error} aria-describedby={`${id}-n`} className={inputClass} />
      <p id={`${id}-n`} className={cn('text-xs tabular-nums', length > max || (length > 0 && length < min) ? 'text-crit-ink' : 'text-muted-foreground')}>{length} de {min} a {max} caracteres</p>
      {error ? <p role="alert" className="text-xs font-medium text-crit-ink">{error}</p> : null}
    </div>
  )
}

/**
 * Cancelamento, pedido, recusa e carta de correção na nota (FIS-03b CA 1 a 9): o servidor diz o que a pessoa pode fazer agora (o
 * Proprietário cancela em uma etapa, os demais pedem, quem edita registra a carta) e confere de novo no envio. A contagem do prazo e o
 * impacto em lista ficam no ConfirmDialog (padrão 7); "Cancelamento enviado / Demorando" segue o padrão 6.
 */
export function InvoiceEventActions({ invoice: v, onDone, now }: { invoice: OutgoingInvoiceView; onDone: Done; now: number }) {
  const c = v.cancellation
  const search = useSearch({ strict: false }) as { pedido?: string }
  const fromLink = !!c.pendingRequest && search.pedido === c.pendingRequest.id && c.canCancel
  const [cancelOpen, setCancelOpen] = useState(fromLink)
  const [withRequest, setWithRequest] = useState<CancellationRequestView | null>(fromLink ? c.pendingRequest ?? null : null)

  if (v.status === 'Cancelled') return <CancelledBanner invoice={v} />
  if (v.status !== 'Authorized') return null
  const sentFor = c.cancellationSentAt ? now - new Date(c.cancellationSentAt).getTime() : 0
  const left = c.deadline ? new Date(c.deadline).getTime() - now : 0
  return (
    <div className="mb-4 space-y-3">
      {c.cancellationSentAt ? (
        <p role="status" aria-live="polite" className={cn('rounded-md border px-3 py-2 text-sm font-semibold', sentFor > 30_000 ? 'border-warn-soft bg-warn-soft text-warn-ink' : 'border-info-soft bg-info-soft text-info-ink')}>
          {sentFor > 30_000 ? <>Demorando. {EVENT_TEXTS.slow}</> : EVENT_TEXTS.sent}
        </p>
      ) : null}
      {c.pendingRequest ? (
        <section aria-label="Pedido de cancelamento" className="rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <p className="font-semibold">Cancelamento pedido por {c.pendingRequest.requestedByName ?? 'um usuário'} em {formatDateTime(c.pendingRequest.requestedAt)}</p>
          <p>{c.pendingRequest.reason}</p>
          {c.withinWindow ? <p className="tabular-nums">Restam {remainingText(left)} para decidir.</p> : null}
          {c.canDecline ? (
            <div className="mt-2 flex flex-wrap gap-2">
              {c.canCancel ? <button type="button" className={danger} onClick={() => { setWithRequest(c.pendingRequest ?? null); setCancelOpen(true) }}>Cancelar NF-e pelo pedido</button> : null}
              <DeclineDialog invoice={v} request={c.pendingRequest} onDone={onDone} />
            </div>
          ) : <p className="mt-1">Pedido de cancelamento enviado. Aguardando o Proprietário.</p>}
        </section>
      ) : null}
      {!c.withinWindow ? (
        <p role="note" className="flex items-center rounded-md border border-border bg-muted px-3 py-2 text-sm">{EVENT_TEXTS.outOfWindow}<Dica nome="Devolução" texto={EVENT_DICAS.devolucao} /></p>
      ) : !c.canCancel && !c.canDecline ? (
        <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">{EVENT_TEXTS.onlyOwner}</p>
      ) : null}
      <div className="flex flex-wrap items-center gap-2">
        {c.canCancel ? (
          <>
            <button type="button" className={danger} onClick={() => { setWithRequest(null); setCancelOpen(true) }}>Cancelar NF-e</button>
            <CancelDialog invoice={v} request={withRequest} open={cancelOpen} setOpen={setCancelOpen} onDone={onDone} />
          </>
        ) : null}
        {c.canRequest ? <RequestDialog invoice={v} onDone={onDone} /> : null}
        {c.canCorrect ? <CorrectionDialog invoice={v} onDone={onDone} /> : null}
        {c.withinWindow && c.deadline ? (
          <span className="inline-flex items-center text-sm tabular-nums text-muted-foreground">Prazo de cancelamento até {formatDateTime(c.deadline)}<Dica nome="Prazo de cancelamento" texto={EVENT_DICAS.prazo} /></span>
        ) : null}
      </div>
    </div>
  )
}

function CancelledBanner({ invoice: v }: { invoice: OutgoingInvoiceView }) {
  const c = v.cancellation
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const [error, setError] = useState<string | null>(null)
  const reissue = async () => {
    if (!v.shipmentId) return
    setError(null)
    try {
      const created = await createOutgoingInvoice({ shipmentId: v.shipmentId }, idempotency.options)
      idempotency.renew()
      void navigate({ to: '/app/fiscal/saida/$id', params: { id: created.id } } as never)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível criar a nota. Tente de novo.')
    }
  }
  return (
    <div className="mb-4 space-y-2">
      <div role="status" className="rounded-md border border-l-4 border-border border-l-muted-foreground bg-muted px-3 py-2 text-sm">
        <p className="font-semibold">NF-e cancelada{c.cancelledAt ? ` em ${formatDateTime(c.cancelledAt)}` : ''}{c.cancelledByName ? ` por ${c.cancelledByName}` : ''}.</p>
        {c.cancellationReason ? <p>Motivo: {c.cancellationReason}</p> : null}
        {c.cancellationProtocol ? <p className="tabular-nums">Protocolo do cancelamento: {c.cancellationProtocol}</p> : null}
        <p className="text-muted-foreground">O estoque não mudou: o romaneio {v.shipmentNumber} continua expedido. O XML, os DANFEs e o evento ficam guardados.</p>
      </div>
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
      {v.shipmentId ? (
        <NextSteps title="Próximos passos" onDismiss={() => undefined} steps={[
          { label: 'Emitir nova NF-e do romaneio', onSelect: () => void reissue() },
          { label: 'Estornar expedição', onSelect: () => void navigate({ to: '/app/producao/expedicao/$id', params: { id: v.shipmentId! } } as never) },
        ]} />
      ) : null}
    </div>
  )
}

/** "Cancelar NF-e" (CA 1 e 3): ConfirmDialog destrutivo com a contagem do prazo, o impacto em lista, a palavra digitada e o 2FA de novo (o passo global pede o código). */
function CancelDialog({ invoice: v, request, open, setOpen, onDone }: { invoice: OutgoingInvoiceView; request: CancellationRequestView | null; open: boolean; setOpen: (o: boolean) => void; onDone: Done }) {
  const [reason, setReason] = useState('')
  const [notify, setNotify] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const client = useQueryClient()
  const label = invoiceLabel(v.series, v.number)
  const length = cleanLength(reason)
  return (
    <ConfirmDialog destructive size="lg" verb="Cancelar NF-e" done="" title={`Cancelar a ${label}?`} open={open} onOpenChange={setOpen}
      onOpen={() => { setReason(request?.reason ?? ''); setNotify(true); setError(null) }}
      description={request ? `Pedido de ${request.requestedByName ?? 'um usuário'}: o motivo veio preenchido e pode ser editado.` : 'O cancelamento vai à SEFAZ em uma etapa. O motivo fica no evento e no histórico.'}
      deadline={v.cancellation.deadline ? { until: v.cancellation.deadline, purpose: 'para cancelar esta nota.' } : undefined}
      typed={{ word: 'CANCELAR', label: 'Para confirmar, digite CANCELAR' }}
      preconditions={[{ ok: length >= 15 && length <= 255, text: 'Escreva o motivo com 15 a 255 caracteres.' }]}
      impactList={{
        label: 'O que muda',
        items: [
          { key: 'nota', primary: `NF-e ${v.number ?? ''}, de ${formatMoney(v.totalAmount)}, de ${v.recipient.name ?? 'cliente'}` },
          { key: 'imposto', primary: `O imposto de ${formatMoney(v.costTaxAmount)} sai do custo do projeto ${v.projectNumber ?? ''}.`, secondary: v.environment === 'Homologation' ? 'Homologação: a nota de teste não entrou no custo.' : undefined },
          { key: 'estoque', primary: `O estoque não muda: o romaneio ${v.shipmentNumber ?? ''} continua expedido.` },
          { key: 'irreversivel', primary: EVENT_TEXTS.irreversible },
        ],
      }}
      onFieldErrors={(e) => { const m = e.fields.reason?.[0]; setError(m ?? null); return !!m }}
      onReload={async () => { await onDone(); setOpen(false) }}
      onConfirm={async () => {
        const result: FiscalEventResultView = await cancelOutgoingInvoice(v.id, { reason, requestId: request?.id ?? null, notifyCustomer: notify, version: v.version })
        notifySuccess(result.status === 'Registered' ? EVENT_TEXTS.cancelled : result.message)
        await client.invalidateQueries({ queryKey: getListOutgoingInvoiceEventsQueryKey(v.id) })
        await onDone(result.invoice)
      }}>
      <ReasonField label="Motivo do cancelamento (vai para a SEFAZ)" value={reason} onChange={setReason} max={255} error={error} />
      <label className="flex min-h-11 items-center gap-2 text-sm">
        <input type="checkbox" checked={notify} onChange={(e) => setNotify(e.target.checked)} className="size-5" />
        Avisar o cliente por e-mail (DANFE cancelado e XML do evento)
      </label>
    </ConfirmDialog>
  )
}

/** "Pedir cancelamento" (CA 2): grava o pedido com o motivo; a nota não muda e o Proprietário decide. */
function RequestDialog({ invoice: v, onDone }: { invoice: OutgoingInvoiceView; onDone: Done }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const length = cleanLength(reason)
  return (
    <ConfirmDialog size="lg" verb="Pedir cancelamento" done={EVENT_TEXTS.requested} title={`Pedir o cancelamento da ${invoiceLabel(v.series, v.number)}?`}
      description="O pedido vai para o Proprietário com o motivo e o prazo. A nota não muda até ele decidir; sem decisão, o pedido expira no fim do prazo."
      trigger={<button type="button" className={secondary}>Pedir cancelamento</button>}
      onOpen={() => { setReason(''); setError(null) }}
      deadline={v.cancellation.deadline ? { until: v.cancellation.deadline, purpose: 'para cancelar esta nota.' } : undefined}
      preconditions={[{ ok: length >= 15 && length <= 255, text: 'Escreva o motivo com 15 a 255 caracteres.' }]}
      onFieldErrors={(e) => { const m = e.fields.reason?.[0]; setError(m ?? null); return !!m }}
      onConfirm={async () => { await requestOutgoingInvoiceCancellation(v.id, { reason }); await onDone() }}>
      <ReasonField label="Motivo do pedido" value={reason} onChange={setReason} max={255} error={error} />
    </ConfirmDialog>
  )
}

/** "Recusar pedido" (CA 3): com o motivo, que vai para quem pediu. */
function DeclineDialog({ invoice: v, request, onDone }: { invoice: OutgoingInvoiceView; request: CancellationRequestView; onDone: Done }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const length = cleanLength(reason)
  return (
    <ConfirmDialog verb="Recusar pedido" done={EVENT_TEXTS.declined} title="Recusar o pedido de cancelamento?" size="lg"
      description={`${request.requestedByName ?? 'Quem pediu'} recebe o aviso com o motivo. A nota continua autorizada.`}
      trigger={<button type="button" className={secondary}>Recusar pedido</button>}
      onOpen={() => { setReason(''); setError(null) }}
      preconditions={[{ ok: length >= 15 && length <= 255, text: 'Escreva o motivo da recusa com 15 a 255 caracteres.' }]}
      onFieldErrors={(e) => { const m = e.fields.reason?.[0]; setError(m ?? null); return !!m }}
      onConfirm={async () => { await declineOutgoingInvoiceCancellation(v.id, request.id, { reason }); await onDone() }}>
      <ReasonField label="Motivo da recusa" value={reason} onChange={setReason} max={255} error={error} />
    </ConfirmDialog>
  )
}

/** "Carta de correção" (CA 8 e 9): lembra o que a carta não muda e avisa em âmbar o indício de mudança proibida, sem bloquear. */
function CorrectionDialog({ invoice: v, onDone }: { invoice: OutgoingInvoiceView; onDone: Done }) {
  const [text, setText] = useState('')
  const [notify, setNotify] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const client = useQueryClient()
  const length = cleanLength(text)
  const warnings = correctionWarnings(text)
  return (
    <ConfirmDialog size="lg" verb="Registrar carta de correção" done="" title={`Carta de correção da ${invoiceLabel(v.series, v.number)}`}
      description={<>{EVENT_TEXTS.correctionLimits} Restam {v.cancellation.correctionsLeft} de 20 cartas nesta nota; a nova vale sobre as anteriores.</>}
      trigger={<button type="button" className={secondary}>Carta de correção</button>}
      onOpen={() => { setText(''); setNotify(true); setError(null) }}
      preconditions={[{ ok: length >= 15 && length <= 1000, text: 'Escreva a correção com 15 a 1000 caracteres.' }]}
      onFieldErrors={(e) => { const m = e.fields.text?.[0]; setError(m ?? null); return !!m }}
      onReload={async () => onDone()}
      onConfirm={async () => {
        const result = await correctOutgoingInvoice(v.id, { text, notifyCustomer: notify, version: v.version })
        notifySuccess(result.status === 'Registered' ? EVENT_TEXTS.corrected : result.message)
        await client.invalidateQueries({ queryKey: getListOutgoingInvoiceEventsQueryKey(v.id) })
        await onDone(result.invoice)
      }}>
      <ReasonField label="Correção (vai para a SEFAZ)" value={text} onChange={setText} max={1000} rows={5} error={error} />
      {warnings.length > 0 ? (
        <ul role="note" aria-label="Avisos da correção" className="space-y-1 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          {warnings.map((w) => <li key={w}>{w}</li>)}
        </ul>
      ) : null}
      <label className="flex min-h-11 items-center gap-2 text-sm">
        <input type="checkbox" checked={notify} onChange={(e) => setNotify(e.target.checked)} className="size-5" />
        Avisar o cliente por e-mail (XML da carta)
      </label>
    </ConfirmDialog>
  )
}

/** Aba Eventos (CA 16): a autorização e, em cartões, cada cancelamento e carta, com a situação, o protocolo, quem pediu e o XML. */
export function InvoiceEventsTab({ invoice: v }: { invoice: OutgoingInvoiceView }) {
  const query = useListOutgoingInvoiceEvents(v.id)
  const [error, setError] = useState<string | null>(null)
  const download = async (eventId: string, name: string) => {
    setError(null)
    try {
      await apiDownload(getDownloadOutgoingInvoiceEventXmlUrl(v.id, eventId), name)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível baixar.')
    }
  }
  let list
  if (query.isPending) list = <div className="space-y-2"><Skeleton className="h-20" /><Skeleton className="h-20" /></div>
  else if (query.isError) list = <ErrorState title="Não foi possível carregar os eventos." onRetry={() => void query.refetch()} />
  else if (query.data.length === 0) list = <EmptyState title={EVENT_TEXTS.noEvents} description="Cancelamento e cartas de correção aparecem aqui, com o protocolo da SEFAZ." />
  else {
    list = (
      <ol className="space-y-2" aria-label="Eventos da nota">
        {query.data.map((e) => (
          <li key={e.id} className={cn('rounded-md border border-border bg-card p-3 text-sm', e.status === 'Rejected' && 'border-l-4 border-l-crit')}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="font-semibold">{e.typeLabel}{e.type === 'Correction' ? ` nº ${e.sequence}` : ''}</p>
              <StatusChip tone={EVENT_STATUS[e.status].tone}>{EVENT_STATUS[e.status].label}</StatusChip>
            </div>
            <p className="mt-1">{e.text}</p>
            <p className="mt-1 text-muted-foreground">
              Pedido por {e.requestedByName ?? 'sistema'} em <time className="tabular-nums" dateTime={e.requestedAt}>{formatDateTime(e.requestedAt)}</time>
              {e.registeredAt && e.status === 'Registered' ? <> · registrado em <time className="tabular-nums" dateTime={e.registeredAt}>{formatDateTime(e.registeredAt)}</time></> : null}
            </p>
            {e.protocol ? <p className="tabular-nums">Protocolo {e.protocol}</p> : null}
            {e.status === 'Rejected' ? <p role="alert" className="text-crit-ink">Recusado{e.rejectionCode ? ` (código ${e.rejectionCode})` : ''}: {e.rejectionReason}</p> : null}
            {e.hasXml ? <button type="button" onClick={() => void download(e.id, `${v.accessKey ?? v.id}-${e.type === 'Cancel' ? 'cancelamento' : `cce-${e.sequence}`}.xml`)} className={cn(secondary, 'mt-2')}>Baixar XML do evento</button> : null}
          </li>
        ))}
      </ol>
    )
  }

  return (
    <div className="space-y-3">
      <dl className="grid gap-2 rounded-md border border-border bg-card p-3 text-sm sm:grid-cols-2">
        <div><dt className="font-medium">Tentativa</dt><dd className="tabular-nums">{v.attempt}</dd></div>
        <div><dt className="flex items-center font-medium">Chave de acesso<Dica nome="Chave de acesso" texto={INVOICE_DICAS.chave} /></dt><dd className="break-all tabular-nums">{v.accessKey ?? '—'}</dd></div>
        <div><dt className="font-medium">Protocolo</dt><dd className="tabular-nums">{v.protocol ?? '—'}</dd></div>
        <div><dt className="font-medium">Autorizada em</dt><dd className="tabular-nums">{v.authorizedAt ? formatDateTime(v.authorizedAt) : '—'}</dd></div>
        <div><dt className="font-medium">Data da nota</dt><dd className="tabular-nums">{v.issuedOn ? formatDate(v.issuedOn) : '—'}</dd></div>
        <div><dt className="font-medium">Rejeição</dt><dd>{v.rejectionCode ? `${v.rejectionCode} — ${v.rejectionReason ?? ''}` : '—'}</dd></div>
        <div className="sm:col-span-2"><dt className="flex items-center font-medium">Cartas de correção<Dica nome="Carta de correção" texto={EVENT_DICAS.carta} /></dt><dd className="tabular-nums">{v.cancellation.corrections} de 20</dd></div>
      </dl>
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
      {list}
    </div>
  )
}

