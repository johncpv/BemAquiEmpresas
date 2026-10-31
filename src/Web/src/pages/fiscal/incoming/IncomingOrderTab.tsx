import { useState } from 'react'
import { mapIncomingInvoiceItem, setIncomingInvoiceOrder } from '@/api/generated/bemaqui'
import type { IncomingInvoiceItemView, IncomingInvoiceView, IncomingOrderRequest, TotalComparison } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { divergenceTone, formatPrice, formatQty, priceText, quantityText, totalText } from './incoming'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'
const small = 'inline-flex min-h-11 items-center rounded-md border border-border bg-card px-3 py-1.5 text-sm font-semibold disabled:opacity-50'

/**
 * Pedido e divergências (FIS-04a CA 9 a 11): o pedido sugerido (citado na nota ou com mais itens em comum) para confirmar, a escolha de
 * outro ou "Sem pedido"; cada item lado a lado pedido × nota em unidade de estoque, com a diferença em quantidade, valor e % (acima em
 * vermelho, abaixo em âmbar, sempre com o texto); frete e IPI da nota × do pedido. Sem `purchasing.view`, os valores do pedido não aparecem.
 */
export function IncomingOrderTab({ view, onChange }: { view: IncomingInvoiceView; onChange: (v: IncomingInvoiceView) => Promise<void> }) {
  const editable = view.status === 'Imported' && view.can.import
  const order = view.order
  const [choosing, setChoosing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const save = async (body: IncomingOrderRequest) => {
    setBusy(true)
    setError(null)
    try {
      await onChange(await setIncomingInvoiceOrder(view.id, body))
      setChoosing(false)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível ligar o pedido. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  const header = order.purchaseOrderId ? (
    <p className="flex flex-wrap items-center gap-2">
      Pedido <b className="font-semibold tabular-nums">{order.number}</b>
      {editable ? <button type="button" onClick={() => setChoosing(true)} className={small}>Trocar pedido</button> : null}
    </p>
  ) : view.withoutOrder ? (
    <p className="flex flex-wrap items-center gap-2">
      <StatusChip tone="info">Sem pedido</StatusChip>
      <span className="text-muted-foreground">A conferência fica com quem aprova compras, com o motivo.</span>
      {editable ? <button type="button" onClick={() => setChoosing(true)} className={small}>Escolher pedido</button> : null}
    </p>
  ) : null

  return (
    <div className="space-y-4 text-sm">
      {header}
      {editable && !order.purchaseOrderId && !view.withoutOrder && order.suggestedId && !choosing ? (
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-primary-soft bg-primary-soft p-3">
          <p><b className="font-semibold">Pedido sugerido: {order.suggestedNumber}</b> — {order.suggestionReason}</p>
          <span className="flex flex-wrap gap-2">
            <button type="button" disabled={busy} onClick={() => void save({ purchaseOrderId: order.suggestedId, withoutOrder: false })} className={primary}>Confirmar pedido</button>
            <button type="button" onClick={() => setChoosing(true)} className={small}>Escolher outro</button>
          </span>
        </div>
      ) : null}
      {editable && (choosing || (!order.purchaseOrderId && !view.withoutOrder && !order.suggestedId)) ? (
        <fieldset className="space-y-2 rounded-md border border-border p-3">
          <legend className="px-1 font-semibold">Pedido da nota</legend>
          {view.supplier.state !== 'Found' ? <p className="text-muted-foreground">Cadastre o fornecedor para ver os pedidos dele.</p> : null}
          {order.options.length === 0 && view.supplier.state === 'Found' ? <p className="text-muted-foreground">O fornecedor não tem pedido aprovado, enviado ou parcialmente recebido.</p> : null}
          <ul className="space-y-1">
            {order.options.map((o) => (
              <li key={o.id} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border px-3 py-2">
                <span>
                  <b className="font-semibold tabular-nums">{o.number}</b> · entrega prevista <span className="tabular-nums">{formatDate(o.expectedDate)}</span>
                  {' · '}{o.commonItems === 1 ? '1 item em comum' : `${o.commonItems} itens em comum`}
                  {o.citedInInvoice ? ' · citado na nota' : ''}
                  {o.suggested ? <StatusChip tone="info" className="ml-2">Sugerido</StatusChip> : null}
                </span>
                <button type="button" disabled={busy || o.id === order.purchaseOrderId} onClick={() => void save({ purchaseOrderId: o.id, withoutOrder: false })} className={small}>Usar este pedido</button>
              </li>
            ))}
          </ul>
          <div className="flex flex-wrap gap-2">
            {!view.withoutOrder ? <button type="button" disabled={busy} onClick={() => void save({ purchaseOrderId: null, withoutOrder: true })} className={small}>Sem pedido</button> : null}
            {choosing ? <button type="button" onClick={() => setChoosing(false)} className={small}>Cancelar</button> : null}
          </div>
        </fieldset>
      ) : null}
      {error ? <p role="alert" className="text-crit-ink">{error}</p> : null}

      {order.purchaseOrderId ? (
        <>
          {!order.valuesVisible ? <p className="rounded-md bg-muted p-2 text-muted-foreground">Os valores do pedido aparecem para quem vê Compras. O vínculo continua valendo.</p> : null}
          <ul className="space-y-3">
            {view.items.filter((i) => !i.noStockItem).map((item) => <Comparison key={item.id} view={view} item={item} editable={editable} onChange={onChange} />)}
          </ul>
          {order.freight || order.ipi ? (
            <section aria-label="Frete e IPI" className="space-y-1">
              <h2 className="font-semibold">Frete e IPI da nota × do pedido</h2>
              {order.freight ? <Total name="Frete" total={order.freight} /> : null}
              {order.ipi ? <Total name="IPI" total={order.ipi} /> : null}
            </section>
          ) : null}
        </>
      ) : !view.withoutOrder && !editable ? <EmptyState title="A nota não foi ligada a um pedido." /> : null}
    </div>
  )
}

function Total({ name, total }: { name: string; total: TotalComparison }) {
  return (
    <p className="flex flex-wrap items-center gap-2 tabular-nums">
      {name}: pedido {formatPrice(total.order)} × nota {formatPrice(total.invoice)}
      <StatusChip tone={divergenceTone(total.level)}>{totalText(name, total.difference, total.percent)}</StatusChip>
    </p>
  )
}

/** Um item lado a lado (CA 10): saldo × faturado e preço do pedido × da nota, em unidade de estoque; a linha do pedido escolhida. */
function Comparison({ view, item, editable, onChange }: { view: IncomingInvoiceView; item: IncomingInvoiceItemView; editable: boolean; onChange: (v: IncomingInvoiceView) => Promise<void> }) {
  const [error, setError] = useState<string | null>(null)
  const c = item.comparison
  const lines = view.order.lines.filter((l) => !item.itemId || l.itemId === item.itemId)
  const pickLine = async (poLineId: string) => {
    setError(null)
    try {
      await onChange(await mapIncomingInvoiceItem(view.id, item.id, { itemId: item.itemId!, conversionFactor: item.conversionFactor ?? null, noStockItem: false, expenseAccountId: null, poLineId }))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível ligar a linha. Tente de novo.')
    }
  }

  const critical = c && (c.quantityLevel === 'Critical' || c.priceLevel === 'Critical')
  const warning = c && !critical && (c.quantityLevel === 'Warning' || c.priceLevel === 'Warning')
  return (
    <li className={cn('rounded-md border border-border bg-card p-3', critical && 'border-l-4 border-l-crit', warning && 'border-l-4 border-l-warn')}>
      <p className="font-semibold"><span className="tabular-nums">{item.sequence}.</span> {item.itemName ?? item.description}</p>
      {!item.itemId ? <p className="text-warn-ink">Ligue o item ao estoque na aba Itens e de-para.</p> : null}
      {item.itemId && editable ? (
        <div className="mt-1">
          <label htmlFor={`linha-${item.id}`} className="text-xs font-medium">Linha do pedido</label>
          <select id={`linha-${item.id}`} value={item.poLineId ?? ''} onChange={(e) => { if (e.target.value) void pickLine(e.target.value) }} className={inputClass}>
            <option value="">{lines.length === 0 ? 'O pedido não tem este item' : 'Escolha a linha do pedido'}</option>
            {lines.map((l) => <option key={l.lineId} value={l.lineId}>Linha {l.sequence} — {l.itemCode} {l.itemName}{l.openQuantity != null ? ` · saldo ${formatQty(l.openQuantity)}` : ''}</option>)}
          </select>
        </div>
      ) : item.poLineSequence ? <p className="text-muted-foreground">Linha {item.poLineSequence} do pedido</p> : null}
      {c ? (
        <dl className="mt-2 grid gap-2 sm:grid-cols-2">
          <div>
            <dt className="text-xs text-muted-foreground">Quantidade (saldo do pedido × faturado)</dt>
            <dd className="tabular-nums">{formatQty(c.openQuantity)} × {formatQty(c.billedQuantity)} {item.stockUnit}</dd>
            <dd className="mt-1"><StatusChip tone={divergenceTone(c.quantityLevel)}>{quantityText(c.quantityDifference, item.stockUnit)}</StatusChip></dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">Preço por {item.stockUnit} (pedido × nota)</dt>
            <dd className="tabular-nums">{formatPrice(c.orderPrice)} × {formatPrice(c.invoicePrice)}</dd>
            <dd className="mt-1"><StatusChip tone={divergenceTone(c.priceLevel)}>{priceText(c.priceDifference, c.pricePercent)}</StatusChip></dd>
            {c.valueDifference !== 0 ? <dd className="mt-1 text-xs tabular-nums text-muted-foreground">Diferença no item: {formatPrice(c.valueDifference)}</dd> : null}
            {c.priceDifference !== 0 ? <dd className="mt-1 text-xs text-muted-foreground">O recebimento usa o preço do pedido até quem aprova compras corrigir o preço no pedido.</dd> : null}
          </div>
        </dl>
      ) : null}
      {error ? <p role="alert" className="mt-1 text-crit-ink">{error}</p> : null}
    </li>
  )
}
