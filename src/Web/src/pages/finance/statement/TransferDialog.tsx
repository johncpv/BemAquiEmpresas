import { useState } from 'react'
import { createTransfer, useGetBankBalances } from '@/api/generated/bemaqui'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { today, toNumber } from '../receivables/receivables'

/** Transferir entre contas (FIN-04 CA 5 a 8): saldo das duas antes e depois, aviso âmbar de saldo negativo. */
export function TransferDialog({ fromId, onDone }: { fromId: string; onDone: (toId: string) => Promise<void> }) {
  const balances = useGetBankBalances()
  const accounts = balances.data?.accounts ?? []
  const [from, setFrom] = useState(fromId)
  const [to, setTo] = useState('')
  const [amount, setAmount] = useState<string | null>(null)
  const [date, setDate] = useState(today())
  const [description, setDescription] = useState('')
  const [reason, setReason] = useState('')
  const idempotency = useIdempotencyKey()
  const value = toNumber(amount)
  const source = accounts.find((a) => a.id === from)
  const target = accounts.find((a) => a.id === to)
  return (
    <ConfirmDialog size="lg" verb="Transferir" done="Transferência registrada" title="Transferir entre contas"
      preconditions={[{ ok: !!to && to !== from, text: 'Contas de origem e destino diferentes.' }, { ok: value > 0, text: 'Valor maior que zero.' }]}
      impact={source && target ? (
        <div aria-live="polite" className="space-y-1 text-sm">
          <p>{source.name}: <Money value={source.currentBalance} /> → <Money value={source.currentBalance - value} highlightNegative /></p>
          <p>{target.name}: <Money value={target.currentBalance} /> → <Money value={target.currentBalance + value} /></p>
          {source.currentBalance - value < 0 ? <p className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">O saldo de {source.name} fica negativo.</p> : null}
        </div>
      ) : null}
      onConfirm={async () => {
        await createTransfer({ fromAccountId: from, toAccountId: to, amount: value, date, description: description.trim() || null, backdateReason: reason.trim() || null }, idempotency.options)
        idempotency.renew()
        await onDone(to)
      }}
      trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Transferir</button>}>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="space-y-1 text-sm font-medium">Origem
          <select value={from} onChange={(e) => setFrom(e.target.value)} className={inputClass}>{accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium">Destino
          <select value={to} onChange={(e) => setTo(e.target.value)} className={inputClass}><option value="">Escolha</option>{accounts.filter((a) => a.id !== from).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium">Valor<MoneyInput value={amount} onChange={setAmount} /></label>
        <label className="space-y-1 text-sm font-medium">Data<DateInput value={date} onChange={setDate} /></label>
        <label className="space-y-1 text-sm font-medium sm:col-span-2">Descrição (opcional)<input value={description} maxLength={200} onChange={(e) => setDescription(e.target.value)} className={inputClass} /></label>
        {date < today() ? <label className="space-y-1 text-sm font-medium sm:col-span-2">Motivo da data retroativa (se passar do limite)<input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label> : null}
      </div>
    </ConfirmDialog>
  )
}
