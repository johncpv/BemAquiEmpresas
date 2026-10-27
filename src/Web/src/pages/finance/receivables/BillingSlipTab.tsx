import { useQueryClient } from '@tanstack/react-query'
import { Check, Copy, Download, Loader2, Receipt } from 'lucide-react'
import QRCode from 'qrcode'
import { useEffect, useState } from 'react'
import { cancelBillingSlip, getDownloadAttachmentUrl, getListBillingSlipsQueryKey, retryBillingSlip, sendBillingSlip, useListBillingSlips } from '@/api/generated/bemaqui'
import type { BillingSlipResult, BillingSlipView, ReceivableDetail } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { BOLETO_DICAS, CANCEL_REASONS, type CancelReason, issuePhase, PHASE_TEXT, POLL_MS, slipStatus } from './billing-slips'

const button = 'inline-flex min-h-11 items-center justify-center gap-1.5 rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'inline-flex min-h-11 items-center justify-center gap-1.5 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

interface Props {
  receivable: ReceivableDetail
  canEdit: boolean
  canConfigure: boolean
  onIssue: () => void
  onChanged: () => void
  onNavigate: (to: string) => void
}

/**
 * Aba "Boleto" do título (BNK-01b CA 8, 12, 13, 16, 18): sem boleto, em emissão (padrão 6: Enviado / Aguardando / Demorando depois
 * de 30 s, sem reenvio enquanto processa), falha com o motivo e as ações, emitido com linha digitável, Pix copia-e-cola, QR Code,
 * PDF, envio e cancelamento; expirado oferece "Emitir novo boleto". Histórico dos boletos anteriores e dos envios.
 */
export function BillingSlipTab({ receivable, canEdit, canConfigure, onIssue, onChanged, onNavigate }: Props) {
  const query = useListBillingSlips(receivable.id, { query: { refetchInterval: (q) => (q.state.data?.some((s) => s.status === 'Requested' || s.cancelPending) ? POLL_MS : false) } })
  const client = useQueryClient()
  const open = receivable.status === 'Open' || receivable.status === 'PartiallyPaid'
  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-24" /><Skeleton className="h-48" /></div>
  if (query.isError) return <ErrorState message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />

  const slips = query.data
  const current = slips.find((s) => s.id === receivable.billingSlipId) ?? null
  const latest = slips[0]
  const previous = slips.filter((s) => s.id !== current?.id)
  const refresh = (result?: BillingSlipResult) => {
    if (result?.message) notifySuccess(result.message)
    void client.invalidateQueries({ queryKey: getListBillingSlipsQueryKey(receivable.id) })
    onChanged()
  }

  return (
    <div className="space-y-4">
      {current ? (
        <CurrentSlip slip={current} receivable={receivable} canEdit={canEdit} onDone={refresh} onNavigate={onNavigate} />
      ) : (
        <>
          {latest?.status === 'Expired' && canEdit && open ? <NextSteps steps={[{ label: 'Emitir novo boleto', onSelect: onIssue }]} onDismiss={() => undefined} /> : null}
          <EmptyState
            title="Este título ainda não tem boleto."
            description={open ? 'O boleto com Pix sai pelo Inter, com multa e juros de Configurações → Cobrança.' : 'Só título em aberto recebe boleto.'}
            action={canEdit && open ? <button type="button" onClick={onIssue} className={primary}><Receipt aria-hidden="true" className="size-4" />Emitir boleto</button> : null}
          />
          {canConfigure ? (
            <p className="text-sm text-muted-foreground">Sem a conta do Inter conectada? <button type="button" onClick={() => onNavigate('/app/configuracoes/integracoes/inter')} className="font-semibold text-primary underline">Conecte a conta do Inter para emitir boletos.</button></p>
          ) : null}
        </>
      )}
      {previous.length > 0 ? (
        <section aria-label="Boletos anteriores" className="rounded-md border border-border bg-card p-4">
          <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">Boletos anteriores</h2>
          <ul className="divide-y divide-border text-sm">
            {previous.map((s) => {
              const st = slipStatus(s.status)
              return (
                <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 py-2 tabular-nums">
                  <span>Seu número {s.ourReference} · <Money value={s.amount} /> · vence {formatDate(s.dueDate)}{s.cancelReasonText ? ` · ${s.cancelReasonText}` : ''}</span>
                  <StatusChip tone={st.tone}>{st.label}</StatusChip>
                </li>
              )
            })}
          </ul>
        </section>
      ) : null}
    </div>
  )
}

function CopyBlock({ label, value, dica }: { label: string; value: string; dica?: string }) {
  const [copied, setCopied] = useState(false)
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      notifySuccess(`${label} copiado`)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      notifyError('Não foi possível copiar. Selecione o texto e copie.')
    }
  }

  return (
    <div className="space-y-1">
      <p className="flex items-center text-sm font-medium">{label}{dica ? <Dica nome={label} texto={dica} /> : null}</p>
      <div className="flex flex-col gap-2 sm:flex-row sm:items-stretch">
        <code data-testid={label} className="min-w-0 flex-1 break-all rounded-md border border-border bg-muted px-3 py-2 font-mono text-sm tabular-nums">{value}</code>
        <button type="button" onClick={() => void copy()} aria-label={`Copiar ${label.toLowerCase()}`} className={button}>
          {copied ? <Check aria-hidden="true" className="size-4" /> : <Copy aria-hidden="true" className="size-4" />}Copiar
        </button>
      </div>
    </div>
  )
}

function QrCode({ text }: { text: string }) {
  const [src, setSrc] = useState<string | null>(null)
  useEffect(() => {
    let alive = true
    void QRCode.toDataURL(text, { margin: 1, width: 200 }).then((url) => { if (alive) setSrc(url) })
    return () => { alive = false }
  }, [text])
  return src ? <img src={src} width={200} height={200} alt="QR Code do Pix do boleto" className="size-[200px] rounded-md border border-border bg-card" /> : <Skeleton className="size-[200px]" />
}

function useNow(active: boolean) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!active) return
    const t = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(t)
  }, [active])
  return now
}

function CurrentSlip({ slip, receivable, canEdit, onDone, onNavigate }: { slip: BillingSlipView; receivable: ReceivableDetail; canEdit: boolean; onDone: (r?: BillingSlipResult) => void; onNavigate: (to: string) => void }) {
  const [dialog, setDialog] = useState<'send' | 'cancel' | null>(null)
  const [retrying, setRetrying] = useState(false)
  const now = useNow(slip.status === 'Requested')
  const st = slipStatus(slip.status, slip.cancelPending)
  const retry = async () => {
    setRetrying(true)
    try {
      onDone(await retryBillingSlip(receivable.id, slip.id))
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível reenviar. Tente de novo.')
    } finally {
      setRetrying(false)
    }
  }

  return (
    <section aria-label="Boleto atual" className="space-y-4 rounded-md border border-border bg-card p-4">
      <EnvironmentBanner service="bank" test={slip.sandbox} className="mb-0" />
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <StatusChip tone={st.tone}>{st.label}</StatusChip>
          <span className="text-sm tabular-nums text-muted-foreground">Seu número {slip.ourReference}<Dica nome="Seu número" texto={BOLETO_DICAS.seuNumero} /></span>
        </div>
        {slip.status === 'Issued' && canEdit && !slip.cancelPending ? (
          <div className="flex flex-wrap gap-2">
            {slip.pdfAttachmentId ? <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(slip.pdfAttachmentId!), `boleto-${receivable.number}.pdf`).catch((e) => notifyError(e instanceof ApiError ? e.message : 'Não foi possível baixar o PDF.'))} className={button}><Download aria-hidden="true" className="size-4" />Baixar PDF</button> : null}
            <button type="button" onClick={() => setDialog('send')} className={primary}>Enviar ao cliente</button>
            <button type="button" onClick={() => setDialog('cancel')} className={button + ' text-crit-ink'}>Cancelar boleto</button>
          </div>
        ) : slip.status === 'Issued' && slip.pdfAttachmentId ? (
          <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(slip.pdfAttachmentId!), `boleto-${receivable.number}.pdf`)} className={button}><Download aria-hidden="true" className="size-4" />Baixar PDF</button>
        ) : null}
      </div>

      {slip.status === 'Requested' ? (
        <p role="status" className="flex items-center gap-2 text-sm"><Loader2 aria-hidden="true" className="size-4 animate-spin" />{PHASE_TEXT[issuePhase(slip.requestedAt, now)]}</p>
      ) : null}
      {slip.cancelPending ? <p role="status" className="text-sm font-semibold">Cancelamento enviado ao Inter. Assim que o banco confirmar, o boleto aparece cancelado.</p> : null}
      {slip.cancelFailureReason && !slip.cancelPending ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">O cancelamento não foi feito: {slip.cancelFailureReason}</p> : null}

      {slip.status === 'Failed' ? (
        <div role="alert" className="space-y-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <p className="font-semibold">Recusado: {slip.failureReason}</p>
          <p>O título continua como estava.</p>
          {canEdit ? (
            <div className="flex flex-wrap gap-2">
              {slip.failureAction === 'fix_party' ? <button type="button" onClick={() => onNavigate(`/app/cadastros/terceiros/${receivable.partyId}`)} className={button + ' bg-card'}>Corrigir cadastro do cliente</button> : null}
              {slip.failureAction === 'fix_integration' ? <button type="button" onClick={() => onNavigate('/app/configuracoes/integracoes/inter')} className={button + ' bg-card'}>Ver a integração do Inter</button> : null}
              <button type="button" disabled={retrying} onClick={() => void retry()} className={primary + ' disabled:opacity-60'}>Tentar de novo</button>
              <button type="button" onClick={() => setDialog('cancel')} className={button + ' bg-card'}>Cancelar boleto</button>
            </div>
          ) : null}
        </div>
      ) : null}

      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
        <div><dt className="text-muted-foreground">Valor</dt><dd className="font-semibold tabular-nums"><Money value={slip.amount} /></dd></div>
        <div><dt className="text-muted-foreground">Vencimento</dt><dd className="font-semibold tabular-nums">{formatDate(slip.dueDate)}</dd></div>
        <div><dt className="flex items-center text-muted-foreground">Nosso número<Dica nome="Nosso número" texto={BOLETO_DICAS.nossoNumero} /></dt><dd className="font-semibold tabular-nums">{slip.ourNumber ?? '—'}</dd></div>
        <div><dt className="text-muted-foreground">Conta</dt><dd className="font-semibold">{slip.bankAccountName}</dd></div>
        <div><dt className="flex items-center text-muted-foreground">Multa<Dica nome="Multa" texto={BOLETO_DICAS.multa} /></dt><dd className="tabular-nums">{slip.finePct.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}%</dd></div>
        <div><dt className="flex items-center text-muted-foreground">Juros de mora<Dica nome="Juros de mora" texto={BOLETO_DICAS.juros} /></dt><dd className="tabular-nums">{slip.interestMonthPct.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}% a.m.</dd></div>
        <div><dt className="text-muted-foreground">Desconto</dt><dd className="tabular-nums">{slip.discountAmount > 0 ? <><Money value={slip.discountAmount} /> até {slip.discountUntil ? formatDate(slip.discountUntil) : '—'}</> : 'Sem desconto'}</dd></div>
        <div><dt className="text-muted-foreground">Multa e juros contam de</dt><dd className="tabular-nums">{formatDate(slip.fineFrom)}</dd></div>
      </dl>
      {slip.approvedByName ? <p className="text-sm text-muted-foreground">Acima dos limites, aprovado por {slip.approvedByName}.</p> : null}

      {slip.status === 'Issued' ? (
        <div className="grid gap-4 lg:grid-cols-[1fr_auto]">
          <div className="space-y-3">
            {slip.digitableLine ? <CopyBlock label="Linha digitável" value={slip.digitableLine} /> : null}
            {slip.pixCopyPaste ? <CopyBlock label="Pix copia-e-cola" value={slip.pixCopyPaste} dica={BOLETO_DICAS.pix} /> : null}
          </div>
          {slip.qrCode ? <div className="justify-self-center"><QrCode text={slip.qrCode} /></div> : null}
        </div>
      ) : null}

      {slip.scheduledEmailAt ? <p role="status" className="text-sm">Envio por e-mail agendado para {formatDateTime(slip.scheduledEmailAt)} (limite diário de e-mails atingido).</p> : null}
      {slip.emails.length > 0 ? (
        <section aria-label="Envios por e-mail">
          <h3 className="mb-1 text-sm font-semibold">Envios por e-mail</h3>
          <ul className="space-y-1 text-sm">
            {slip.emails.map((e) => <li key={e.id} className="tabular-nums">{formatDateTime(e.createdAt)} · {e.to} · {e.sentAt ? 'Enviado' : e.error ? `Falhou: ${e.error}` : 'Na fila'}</li>)}
          </ul>
        </section>
      ) : null}

      {dialog === 'send' ? <SendDialog slip={slip} receivableId={receivable.id} onOpenChange={(o) => !o && setDialog(null)} onDone={(r) => { setDialog(null); onDone(r) }} /> : null}
      {dialog === 'cancel' ? <CancelDialog slip={slip} receivableId={receivable.id} onOpenChange={(o) => !o && setDialog(null)} onDone={(r) => { setDialog(null); onDone(r) }} /> : null}
    </section>
  )
}

function SendDialog({ slip, receivableId, onOpenChange, onDone }: { slip: BillingSlipView; receivableId: string; onOpenChange: (o: boolean) => void; onDone: (r: BillingSlipResult) => void }) {
  const [to, setTo] = useState('')
  const [cc, setCc] = useState('')
  const [fields, setFields] = useState<Record<string, string>>({})
  return (
    <ConfirmDialog open onOpenChange={onOpenChange} verb="Enviar ao cliente" done="" title="Enviar o boleto ao cliente"
      description="O e-mail leva o PDF do boleto e só os dados deste título: empresa, número, valor, vencimento e linha digitável."
      onFieldErrors={(e) => { setFields(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))); return true }}
      onConfirm={async () => {
        setFields({})
        const copies = cc.split(/[,;\s]+/).map((c) => c.trim()).filter(Boolean)
        const result = await sendBillingSlip(receivableId, slip.id, { to: to.trim() || null, cc: copies })
        onDone(result)
      }}>
      <Field label="Para" error={fields.to} hint="Vazio: o contato do cliente marcado &quot;recebe cobrança&quot;.">{(p) => <input {...p} type="email" value={to} onChange={(e) => setTo(e.target.value)} placeholder="contato de cobrança" className={inputClass} />}</Field>
      <Field label="Cópia (até 3)" error={fields.cc} hint="Separe por vírgula.">{(p) => <input {...p} value={cc} onChange={(e) => setCc(e.target.value)} className={inputClass} />}</Field>
    </ConfirmDialog>
  )
}

function CancelDialog({ slip, receivableId, onOpenChange, onDone }: { slip: BillingSlipView; receivableId: string; onOpenChange: (o: boolean) => void; onDone: (r: BillingSlipResult) => void }) {
  const [reason, setReason] = useState<CancelReason>('CustomerRequest')
  return (
    <ConfirmDialog open destructive onOpenChange={onOpenChange} verb="Cancelar boleto" done="" title="Cancelar o boleto no Inter?"
      description="O cancelamento vai ao banco; o título não é cancelado e pode receber outro boleto."
      onConfirm={async () => {
        const result = await cancelBillingSlip(receivableId, slip.id, { reason })
        notifySuccess('Boleto cancelado')
        onDone({ ...result, message: '' })
      }}>
      <Field label="Motivo" required>
        {(p) => (
          <select {...p} value={reason} onChange={(e) => setReason(e.target.value as CancelReason)} className={inputClass}>
            {CANCEL_REASONS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
          </select>
        )}
      </Field>
    </ConfirmDialog>
  )
}
