import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { payPaymentOrder, resubmitPaymentOrder, useGetCashFlow, useListBankAccounts } from '@/api/generated/bemaqui'
import type { PaymentOrderDetail, PaymentResult } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { today, toNumber } from '../receivables/receivables'
import { destination, itemTotal } from './payment-orders'

interface Charges { interest: string | null; fine: string | null; discount: string | null }

/**
 * Pagar (CA 13 a 18): conta, data, acréscimos por título, total ao vivo e o saldo da conta antes e depois. Com <c>?acao=pagar</c> (link
 * da Minha pauta, DSH-04) o diálogo já abre.
 */
export function PayDialog({ order, refresh, onPaid }: { order: PaymentOrderDetail; refresh: () => Promise<void>; onPaid: (result: PaymentResult) => void }) {
  const { acao } = useSearch({ strict: false }) as { acao?: string }
  const navigate = useNavigate()
  const [open, setOpen] = useState(acao === 'pagar')
  const clearAction = () => { if (acao) void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, acao: undefined }), replace: true } as never) }
  const accounts = useListBankAccounts({ pageSize: 100 })
  const active = (accounts.data?.items ?? []).filter((a) => a.isActive)
  const [account, setAccount] = useState<string>('')
  const bankAccountId = account || active.find((a) => a.isDefault)?.id || active[0]?.id || ''
  const [date, setDate] = useState(today())
  const [reason, setReason] = useState('')
  const [charges, setCharges] = useState<Record<string, Charges>>(() => Object.fromEntries(order.items.map((i) => [i.id, { interest: i.expectedCharges ? String(i.expectedCharges) : null, fine: null, discount: null }])))
  const idempotency = useIdempotencyKey()
  const balance = useGetCashFlow({ bankAccountId: bankAccountId || undefined, days: 1 }, { query: { enabled: !!bankAccountId } })
  const totals = order.items.map((i) => { const c = charges[i.id]; return itemTotal(i.amount, toNumber(c.interest), toNumber(c.fine), toNumber(c.discount)) })
  const total = totals.reduce((a, b) => a + b, 0)
  const current = balance.data?.currentBalance
  const items = order.items.map((i) => { const c = charges[i.id]; return { itemId: i.id, interest: toNumber(c.interest), fine: toNumber(c.fine), discount: toNumber(c.discount) } })
  const set = (id: string, patch: Partial<Charges>) => setCharges((s) => ({ ...s, [id]: { ...s[id], ...patch } }))

  return (
    <ConfirmDialog size="lg" verb="Pagar" done="Pagamento registrado" title={`Pagar a ${order.summary.number}?`} description="O valor sai da conta escolhida na data informada."
      open={open} onOpenChange={(o) => { setOpen(o); if (!o) clearAction() }}
      preconditions={[{ ok: !!bankAccountId, text: 'Conta bancária escolhida.' }]}
      impact={(
        <div aria-live="polite" className="space-y-1 text-sm">
          <p>Sai da conta: <Money value={total} className="font-semibold" /></p>
          {current !== undefined ? <p>Saldo atual <Money value={current} highlightNegative /> → depois <Money value={current - total} highlightNegative /></p> : null}
          {current !== undefined && current - total < 0 ? <p className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">O saldo da conta fica negativo.</p> : null}
        </div>
      )}
      onConfirm={async () => {
        const result = await payPaymentOrder(order.summary.id, { bankAccountId, date, items, backdateReason: reason.trim() || null, version: order.version }, idempotency.options)
        idempotency.renew()
        await refresh()
        onPaid(result)
      }}
      onReload={refresh}
      recovery={{ verb: 'Reenviar para aprovação', done: 'Reenviada para aprovação', when: (code) => code === 'exceeds_approved', onSelect: async () => { await resubmitPaymentOrder(order.summary.id, { items, version: order.version }); await refresh() } }}
      trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Pagar</button>}>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="space-y-1 text-sm font-medium">Conta
          <select value={bankAccountId} onChange={(e) => setAccount(e.target.value)} className={inputClass}>{active.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium">Data do pagamento
          <DateInput value={date} onChange={setDate} />
        </label>
      </div>
      {date && date < today() ? (
        <label className="mt-2 block space-y-1 text-sm font-medium">Motivo da data retroativa (se passar do limite)
          <input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />
        </label>
      ) : null}
      <ul className="mt-3 divide-y divide-border rounded-md border border-border text-sm" aria-label="Pagamento por título">
        {order.items.map((i, k) => (
          <li key={i.id} className="space-y-2 p-3">
            <p className="flex flex-wrap justify-between gap-2"><span><b>{i.payableNumber}</b> — {i.partyName}</span><span>Total <Money value={totals[k]} className="font-semibold" /> de <Money value={i.approvedTotal} /> aprovado</span></p>
            <p className="text-xs text-muted-foreground">Para: {destination(i.payment)}</p>
            {i.warnings.map((w) => <p key={w} className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">{w}</p>)}
            <div className="grid gap-2 sm:grid-cols-3">
              <MoneyInput aria-label={`Juros de ${i.payableNumber}`} placeholder="Juros" value={charges[i.id].interest} onChange={(v) => set(i.id, { interest: v })} />
              <MoneyInput aria-label={`Multa de ${i.payableNumber}`} placeholder="Multa" value={charges[i.id].fine} onChange={(v) => set(i.id, { fine: v })} />
              <MoneyInput aria-label={`Desconto de ${i.payableNumber}`} placeholder="Desconto" value={charges[i.id].discount} onChange={(v) => set(i.id, { discount: v })} />
            </div>
          </li>
        ))}
      </ul>
    </ConfirmDialog>
  )
}
