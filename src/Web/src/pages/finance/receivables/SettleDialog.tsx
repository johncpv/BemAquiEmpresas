import { useState } from 'react'
import { settleReceivable, useGetFinanceLimits, useListBankAccounts } from '@/api/generated/bemaqui'
import type { ReceivableDetail } from '@/api/generated/model'
import type { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog, type Precondition } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { useCan } from '@/pages/use-can'
import { RECEIVABLE_DICAS, toNumber, today } from './receivables'

interface Props { receivable: ReceivableDetail; open: boolean; onOpenChange: (o: boolean) => void; onDone: (r: ReceivableDetail) => void; onReload: () => Promise<unknown> }

/** Ação com efeito financeiro (FIN-01 CA 8 a 10, 13 a 15): saldo antes e depois e o total que entra na conta, ao vivo. */
export function SettleDialog({ receivable, open, onOpenChange, onDone, onReload }: Props) {
  const accounts = useListBankAccounts({ pageSize: 100 }, { query: { enabled: open } })
  const limits = useGetFinanceLimits({ query: { enabled: open } })
  const canApprove = useCan('finance.approve')
  const active = (accounts.data?.items ?? []).filter((a) => a.isActive)
  const fallback = active.find((a) => a.isDefault)?.id ?? active[0]?.id ?? ''
  const [bank, setBank] = useState('')
  const [date, setDate] = useState(today())
  const [principal, setPrincipal] = useState<string | null>(String(receivable.balance))
  const [interest, setInterest] = useState<string | null>(null)
  const [fine, setFine] = useState<string | null>(null)
  const [discount, setDiscount] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [fields, setFields] = useState<Record<string, string>>({})
  const reset = () => { setBank(''); setDate(today()); setPrincipal(String(receivable.balance)); setInterest(null); setFine(null); setDiscount(null); setReason(''); setFields({}) }

  const total = toNumber(principal) + toNumber(interest) + toNumber(fine) - toNumber(discount)
  const after = receivable.balance - toNumber(principal)
  const pct = receivable.balance > 0 ? (toNumber(discount) / receivable.balance) * 100 : 0
  const overLimit = !!limits.data && toNumber(discount) > 0 && (pct > limits.data.discountApprovalPct || toNumber(discount) > limits.data.discountApprovalAmount)
  const backdated = !!limits.data && !!date && date < new Date(Date.parse(today() + 'T00:00:00Z') - limits.data.backdateDays * 86_400_000).toISOString().slice(0, 10)
  const preconditions: Precondition[] = [{ ok: !overLimit || canApprove, text: 'Desconto acima do limite: só quem aprova em Caixa e pagamentos pode confirmar.' }]

  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={(o) => { if (o) reset(); onOpenChange(o) }} verb="Dar baixa" done="Baixa registrada" title={`Dar baixa no título ${receivable.number}`} description={`${receivable.partyName} · ${receivable.description}`}
      preconditions={preconditions} onReload={onReload}
      onFieldErrors={(e: ApiError) => { setFields(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))); return true }}
      impact={(
        <dl className="grid grid-cols-2 gap-1" aria-live="polite">
          <dt>Saldo antes</dt><dd className="text-right"><Money value={receivable.balance} /></dd>
          <dt>Saldo depois</dt><dd className="text-right"><Money value={Math.max(0, after)} /></dd>
          <dt className="font-semibold">Entra na conta</dt><dd className="text-right font-semibold"><Money value={Math.max(0, total)} /></dd>
        </dl>
      )}
      onConfirm={async () => {
        setFields({})
        onDone(await settleReceivable(receivable.id, {
          bankAccountId: bank || fallback, date, principal: toNumber(principal), interest: toNumber(interest), fine: toNumber(fine), discount: toNumber(discount),
          backdateReason: reason.trim() || null, version: receivable.version,
        }))
      }}>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Conta" required error={fields.bankAccountId}>
          {(p) => (
            <select {...p} value={bank || fallback} onChange={(e) => setBank(e.target.value)} className={inputClass}>
              {active.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Data do recebimento" required error={fields.date}>{(p) => <DateInput {...p} value={date} onChange={setDate} />}</Field>
        <Field label="Principal" required error={fields.principal} dica={RECEIVABLE_DICAS.principal}>{(p) => <MoneyInput {...p} value={principal} onChange={setPrincipal} />}</Field>
        <Field label="Juros" error={fields.interest} dica={RECEIVABLE_DICAS.juros}>{(p) => <MoneyInput {...p} value={interest} onChange={setInterest} />}</Field>
        <Field label="Multa" error={fields.fine} dica={RECEIVABLE_DICAS.multa}>{(p) => <MoneyInput {...p} value={fine} onChange={setFine} />}</Field>
        <Field label="Desconto" error={fields.discount} dica={RECEIVABLE_DICAS.desconto} warning={overLimit ? 'Desconto acima do limite de Configurações → Parâmetros.' : undefined}>{(p) => <MoneyInput {...p} value={discount} onChange={setDiscount} />}</Field>
      </div>
      {backdated ? (
        <Field label="Motivo da data retroativa" required error={fields.backdateReason} hint="Data mais antiga que o limite: o Proprietário é avisado.">
          {(p) => <input {...p} value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
        </Field>
      ) : null}
    </ConfirmDialog>
  )
}
