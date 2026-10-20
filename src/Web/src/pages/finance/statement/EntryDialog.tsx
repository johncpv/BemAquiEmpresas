import { useState } from 'react'
import { createBankEntry, useListAccounts, useListCostCenters } from '@/api/generated/bemaqui'
import type { BankEntryKind } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { today, toNumber } from '../receivables/receivables'

const ENTRY_KINDS: { value: BankEntryKind; label: string; out: boolean }[] = [
  { value: 'BankFee', label: 'Tarifa bancária', out: true },
  { value: 'Iof', label: 'IOF', out: true },
  { value: 'InterestPaid', label: 'Juros pagos', out: true },
  { value: 'InterestReceived', label: 'Juros recebidos', out: false },
  { value: 'Yield', label: 'Rendimento', out: false },
  { value: 'OtherOut', label: 'Outra saída', out: true },
  { value: 'OtherIn', label: 'Outra entrada', out: false },
]

/** Lançar tarifa ou rendimento sem OP (FIN-04 CA 11 e 12): conta do plano sugerida pelo tipo e motivo obrigatório. */
export function EntryDialog({ accountId, onDone }: { accountId: string; onDone: () => Promise<void> }) {
  const chart = useListAccounts()
  const centers = useListCostCenters()
  const [kind, setKind] = useState<BankEntryKind>('BankFee')
  const [amount, setAmount] = useState<string | null>(null)
  const [date, setDate] = useState(today())
  const [plan, setPlan] = useState('')
  const [center, setCenter] = useState('')
  const [reason, setReason] = useState('')
  const [backdate, setBackdate] = useState('')
  const idempotency = useIdempotencyKey()
  const out = ENTRY_KINDS.find((k) => k.value === kind)?.out ?? true
  const options = (chart.data ?? []).filter((a) => a.acceptsEntries && a.isActive && (out ? a.type === 'Expense' : a.type === 'Revenue'))
  const suggested = options.find((a) => a.code === (out ? '3.5' : '1.3'))?.id ?? ''
  const chosen = plan && options.some((a) => a.id === plan) ? plan : suggested
  return (
    <ConfirmDialog size="lg" verb="Lançar" done="Lançamento registrado" title="Lançar tarifa ou rendimento" description="Movimento sem ordem de pagamento. O Proprietário recebe o aviso."
      preconditions={[{ ok: toNumber(amount) > 0, text: 'Valor maior que zero.' }, { ok: reason.trim().length > 0, text: 'Motivo escrito.' }, { ok: !!chosen, text: 'Conta do plano escolhida.' }]}
      impact={<p className="text-sm">{out ? 'Sai' : 'Entra'} da conta: <Money value={toNumber(amount)} className="font-semibold" /></p>}
      onConfirm={async () => {
        await createBankEntry({ bankAccountId: accountId, kind, date, amount: toNumber(amount), accountId: chosen || null, costCenterId: center || null, reason: reason.trim(), backdateReason: backdate.trim() || null }, idempotency.options)
        idempotency.renew()
        await onDone()
      }}
      trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Lançar tarifa ou rendimento</button>}>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="space-y-1 text-sm font-medium">Tipo
          <select value={kind} onChange={(e) => { setKind(e.target.value as BankEntryKind); setPlan('') }} className={inputClass}>{ENTRY_KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium">Valor<MoneyInput value={amount} onChange={setAmount} /></label>
        <label className="space-y-1 text-sm font-medium">Data<DateInput value={date} onChange={setDate} /></label>
        <label className="space-y-1 text-sm font-medium">Conta do plano
          <select value={chosen} onChange={(e) => setPlan(e.target.value)} className={inputClass}><option value="">Escolha</option>{options.map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium">Centro de custo (opcional)
          <select value={center} onChange={(e) => setCenter(e.target.value)} className={inputClass}><option value="">Sem centro</option>{(centers.data ?? []).filter((c) => c.isActive).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium sm:col-span-2">Motivo<input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
        {date < today() ? <label className="space-y-1 text-sm font-medium sm:col-span-2">Motivo da data retroativa (se passar do limite)<input value={backdate} maxLength={200} onChange={(e) => setBackdate(e.target.value)} className={inputClass} /></label> : null}
      </div>
    </ConfirmDialog>
  )
}
