import { useState } from 'react'
import { issueBillingSlip, useGetBillingSlipDraft } from '@/api/generated/bemaqui'
import type { BillingSlipDraft, BillingSlipResult } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput, PercentInput } from '@/components/form/inputs'
import { ConfirmDialog, type Precondition } from '@/components/ui/confirm-dialog'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'
import { APPROVAL_MESSAGE, BOLETO_DICAS, messageError, needsApproval } from './billing-slips'
import { today, toNumber } from './receivables'

interface Props {
  receivableId: string
  open: boolean
  onOpenChange: (o: boolean) => void
  onIssued: (result: BillingSlipResult) => void
  onShowSlip: () => void
  onNavigate: (to: string) => void
}

/**
 * "Emitir boleto" (BNK-01b CA 4, 5, 10, 11, 15): diálogo grande com o impacto (valor = saldo, vencimento, conta, multa, juros,
 * desconto e o e-mail que recebe), tudo pré-preenchido pelos padrões de Configurações → Cobrança. Pré-condições do cliente e da conta
 * antes de confirmar; acima dos limites, só quem aprova no Financeiro emite. Envia com Idempotency-Key.
 */
export function IssueBillingSlipDialog({ receivableId, open, onOpenChange, onIssued, onShowSlip, onNavigate }: Props) {
  const draft = useGetBillingSlipDraft(receivableId, { query: { enabled: open, retry: false } })
  if (!open) return null
  if (draft.isPending || draft.isError || !draft.data) {
    return (
      <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb="Emitir boleto" done="Boleto solicitado" title="Emitir boleto" onConfirm={async () => undefined}
        preconditions={[{ ok: false, text: draft.isError ? 'Não foi possível carregar os dados do boleto.' : 'Carregando os dados do boleto…' }]}>
        {draft.isError ? <ErrorState message={draft.error instanceof ApiError ? draft.error.message : undefined} onRetry={() => void draft.refetch()} /> : <Skeleton className="h-48" />}
      </ConfirmDialog>
    )
  }

  return <DraftDialog key={draft.dataUpdatedAt} draft={draft.data} open={open} onOpenChange={onOpenChange} onIssued={onIssued} onShowSlip={onShowSlip} onNavigate={onNavigate} />
}

function DraftDialog({ draft, open, onOpenChange, onIssued, onShowSlip, onNavigate }: { draft: BillingSlipDraft } & Omit<Props, 'receivableId'>) {
  const idempotency = useIdempotencyKey()
  const defaultAccount = draft.accounts.find((a) => a.isDefault)?.id ?? draft.accounts[0]?.id ?? ''
  const [account, setAccount] = useState(defaultAccount)
  const [due, setDue] = useState<string | null>(draft.suggestedDueDate)
  const [fine, setFine] = useState<string | null>(String(draft.finePct))
  const [interest, setInterest] = useState<string | null>(String(draft.interestMonthPct))
  const [discount, setDiscount] = useState<string | null>(draft.discountAmount > 0 ? String(draft.discountAmount) : null)
  const [until, setUntil] = useState<string | null>(draft.discountUntil ?? draft.suggestedDueDate)
  const [message, setMessage] = useState('')
  const [send, setSend] = useState(draft.autoEmail && !!draft.email)
  const [fields, setFields] = useState<Record<string, string>>({})
  const sandbox = draft.accounts.find((a) => a.id === account)?.sandbox ?? false

  const useDefaults = () => {
    setFine(String(draft.finePct))
    setInterest(String(draft.interestMonthPct))
    setDiscount(draft.discountAmount > 0 ? String(draft.discountAmount) : null)
  }

  const overLimit = needsApproval(draft, toNumber(discount), toNumber(fine), toNumber(interest))
  const preconditions: Precondition[] = [
    ...draft.preconditions.filter((p) => p.blocking).map((p) => ({ ok: false, text: p.text, fix: p.fixLink && p.fixLabel ? { label: p.fixLabel, onSelect: () => onNavigate(p.fixLink!) } : undefined })),
    { ok: !overLimit || draft.canApprove, text: APPROVAL_MESSAGE, fix: { label: 'Usar os valores padrão', onSelect: useDefaults, keepOpen: true } },
  ]
  const warning = draft.preconditions.find((p) => !p.blocking)
  const exists = draft.preconditions.some((p) => p.code === 'billing_slip_exists' || p.code === 'billing_slip_failed')
  const messageInvalid = messageError(message)

  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb="Emitir boleto" done="" title={`Emitir boleto do título ${draft.receivableNumber}`} description={draft.partyName}
      preconditions={preconditions}
      recovery={exists ? undefined : { verb: 'Ver boleto', when: (code) => code === 'billing_slip_exists', onSelect: async () => { onOpenChange(false); onShowSlip() } }}
      onFieldErrors={(e) => { setFields(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))); return true }}
      impact={(
        <dl className="grid grid-cols-2 gap-1">
          <dt>Valor (saldo do título)</dt><dd className="text-right font-semibold"><Money value={draft.amount} /></dd>
          <dt>Vencimento</dt><dd className="text-right">{due ? formatDate(due) : '—'}</dd>
          <dt>Conta de crédito</dt><dd className="text-right">{draft.accounts.find((a) => a.id === account)?.name ?? '—'}</dd>
          <dt>Multa e juros</dt><dd className="text-right">{toNumber(fine).toLocaleString('pt-BR', { minimumFractionDigits: 2 })}% · {toNumber(interest).toLocaleString('pt-BR', { minimumFractionDigits: 2 })}% a.m.</dd>
          <dt>Desconto</dt><dd className="text-right">{toNumber(discount) > 0 ? <><Money value={toNumber(discount)} /> até {until ? formatDate(until) : '—'}</> : 'Sem desconto'}</dd>
          <dt>E-mail</dt><dd className="break-all text-right">{send && draft.email ? draft.email : 'Sem envio por e-mail'}</dd>
        </dl>
      )}
      onConfirm={async () => {
        setFields({})
        if (messageInvalid) throw new ApiError(422, { detail: messageInvalid, errors: { message: [messageInvalid] } })
        const result = await issueBillingSlip(draft.receivableId, {
          bankAccountId: account, dueDate: due ?? draft.suggestedDueDate, finePct: toNumber(fine), interestMonthPct: toNumber(interest), discountAmount: toNumber(discount),
          discountUntil: toNumber(discount) > 0 ? until : null, message: message.trim() || null, sendEmail: send, version: draft.version,
        }, idempotency.options)
        idempotency.renew()
        notifySuccess(result.message)
        onIssued(result)
      }}>
      <EnvironmentBanner service="bank" test={sandbox} className="mb-0" />
      {warning ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{warning.text}</p> : null}
      {draft.isOverdue && draft.receivableDueDate ? (
        <p role="note" className="rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
          O título mantém o vencimento de {formatDate(draft.receivableDueDate)}; a multa e os juros contam dessa data.
        </p>
      ) : null}
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Conta de crédito" required error={fields.bankAccountId}>
          {(p) => (
            <select {...p} value={account} onChange={(e) => setAccount(e.target.value)} className={inputClass}>
              {draft.accounts.map((a) => <option key={a.id} value={a.id}>{a.name}{a.sandbox ? ' (teste)' : ''}</option>)}
            </select>
          )}
        </Field>
        <Field label="Vencimento do boleto" required error={fields.dueDate} hint={draft.isOverdue ? 'Título vencido: escolha uma data a partir de hoje. Dia não útil segue a regra do banco.' : undefined}>
          {(p) => <DateInput {...p} value={due} min={today()} onChange={setDue} />}
        </Field>
        <Field label="Multa" dica={BOLETO_DICAS.multa} error={fields.finePct}>{(p) => <PercentInput {...p} value={fine} onChange={setFine} />}</Field>
        <Field label="Juros de mora (ao mês)" dica={BOLETO_DICAS.juros} error={fields.interestMonthPct}>{(p) => <PercentInput {...p} value={interest} onChange={setInterest} />}</Field>
        <Field label="Desconto" error={fields.discountAmount} warning={overLimit ? APPROVAL_MESSAGE : undefined}>{(p) => <MoneyInput {...p} value={discount} onChange={setDiscount} />}</Field>
        {toNumber(discount) > 0 ? <Field label="Desconto até" required error={fields.discountUntil}>{(p) => <DateInput {...p} value={until} min={today()} onChange={setUntil} />}</Field> : null}
      </div>
      <Field label="Mensagem ao pagador" error={fields.message ?? messageInvalid} hint="Até 3 linhas. Não escreva dado de outro cliente.">
        {(p) => <textarea {...p} rows={3} value={message} onChange={(e) => setMessage(e.target.value)} className={inputClass} />}
      </Field>
      <label className="flex min-h-11 items-start gap-2 text-sm">
        <input type="checkbox" checked={send} disabled={!draft.email} onChange={(e) => setSend(e.target.checked)} className="mt-0.5 size-4 accent-primary" />
        <span><b>Enviar por e-mail ao emitir</b><span className="block break-all text-xs text-muted-foreground">{draft.email ? `Para ${draft.email}, quando o banco confirmar.` : 'O cliente não tem e-mail de cobrança.'}</span></span>
      </label>
    </ConfirmDialog>
  )
}
