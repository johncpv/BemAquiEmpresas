import type { PaymentOrderItemView } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { destination } from './payment-orders'

/** Itens com para onde vai o dinheiro (retrato do envio, mascarado) e os avisos âmbar (CA 6 e 15). */
export function OrderItemsList({ items, compact }: { items: PaymentOrderItemView[]; compact?: boolean }) {
  return (
    <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Títulos e destino do dinheiro">
      {items.map((i) => (
        <li key={i.id} className="space-y-1 p-3">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <span><b>{i.payableNumber}</b> — {i.partyName}{compact ? null : <span className="text-muted-foreground"> · {i.description}</span>}</span>
            <Money value={i.approvedTotal} className="font-semibold" />
          </div>
          <p className="text-xs text-muted-foreground">Para: {destination(i.payment)}</p>
          {typeof i.installationBudget === 'number' ? (
            <p className={i.netAmount > i.installationBudget ? 'rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink' : 'text-xs text-muted-foreground'}>
              Combinado <Money value={i.netAmount} /> · Orçado <Money value={i.installationBudget} />{i.netAmount > i.installationBudget ? ' — acima do orçado de instalação' : ''}
            </p>
          ) : null}
          {i.amount < i.netAmount ? <p className="text-xs">Pagamento parcial: <Money value={i.amount} /> de <Money value={i.netAmount} /></p> : null}
          {i.warnings.map((w) => <p key={w} className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">{w}</p>)}
        </li>
      ))}
    </ul>
  )
}
