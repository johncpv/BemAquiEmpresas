import { useState } from 'react'
import { addQuoteItem, removeQuoteItem, updateQuoteItem } from '@/api/generated/bemaqui'
import type { QuoteCostDetail, QuoteDetail, QuoteItemView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput, QuantityInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { CostEditor } from './CostEditor'
import { ITEM_KINDS, pctLabel, QUOTE_DICAS } from './quotes'

interface Props { quote: QuoteDetail; item: QuoteItemView; costs?: QuoteCostDetail; editable: boolean; refresh: () => Promise<void> }

/** Um item (CA 7, 14): descrição, quantidade e preço; preço digitado à mão com "Voltar ao sugerido"; composição expansível. */
export function ItemCard({ quote, item, costs, editable, refresh }: Props) {
  const [description, setDescription] = useState(item.description)
  const [quantity, setQuantity] = useState<string | null>(String(item.quantity))
  const [price, setPrice] = useState<string | null>(String(item.unitPrice))
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | undefined>()
  const cost = costs?.items.find((c) => c.itemId === item.id)
  const run = async (action: () => Promise<unknown>) => {
    setError(undefined)
    try { await action(); await refresh() } catch (e) { setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o item.') }
  }

  const body = (unitPrice: number | null, resetPrice = false) => ({ kind: item.kind, itemId: item.itemId ?? null, description: description.trim(), quantity: quantity ? Number(quantity) : 0, unitPrice, resetPrice, version: quote.version })
  const priceChanged = price !== String(item.unitPrice)
  const keep = item.priceOverridden ? item.unitPrice : null
  return (
    <li className="space-y-2 rounded-md border border-border bg-card p-3">
      <div className="grid gap-2 sm:grid-cols-[3rem_minmax(0,2fr)_7rem_9rem_8rem_auto] sm:items-end">
        <span className="text-xs text-muted-foreground">#{item.sequence} {ITEM_KINDS.find((k) => k.value === item.kind)?.label}</span>
        <label className="space-y-1 text-xs font-medium">Descrição<input value={description} disabled={!editable} maxLength={300} onChange={(e) => setDescription(e.target.value)} className={inputClass} /></label>
        <label className="space-y-1 text-xs font-medium">Quantidade<QuantityInput value={quantity} disabled={!editable} onChange={setQuantity} /></label>
        <label className="space-y-1 text-xs font-medium">Preço unitário<MoneyInput value={price} disabled={!editable} onChange={setPrice} /></label>
        <span className="text-right text-sm">Total <Money value={item.total} className="font-semibold" /></span>
        {editable ? (
          <span className="flex flex-wrap justify-end gap-1">
            <button type="button" onClick={() => void run(() => updateQuoteItem(quote.id, item.id, body(priceChanged ? Number(price ?? 0) : keep)))} className="rounded-md border border-border px-2 py-1 text-xs font-semibold">Salvar item</button>
            <button type="button" onClick={() => void run(() => addQuoteItem(quote.id, body(keep)))} className="rounded-md border border-border px-2 py-1 text-xs font-semibold">Duplicar item</button>
            <button type="button" onClick={() => void run(() => removeQuoteItem(quote.id, item.id, { version: quote.version }))} aria-label={`Remover item ${item.sequence}`} className="rounded-md border border-border px-2 py-1 text-xs font-semibold">Remover</button>
          </span>
        ) : null}
      </div>
      <div className="flex flex-wrap items-center gap-2 text-xs">
        {item.priceOverridden ? (
          <>
            <span className="inline-flex items-center"><Tag>Digitado à mão</Tag><Dica nome="Digitado à mão" texto={QUOTE_DICAS.digitado} /></span>
            {editable ? <button type="button" onClick={() => void run(() => updateQuoteItem(quote.id, item.id, body(null, true)))} className="font-semibold text-primary hover:underline">Voltar ao sugerido</button> : null}
          </>
        ) : null}
        {cost && costs ? <span className="text-muted-foreground">Custo unitário <Money value={cost.unitCost} /> · {pctLabel(costs.pricingMode, cost.effectivePct)}</span> : null}
        {cost ? <button type="button" aria-expanded={open} onClick={() => setOpen((o) => !o)} className="font-semibold text-primary hover:underline">{open ? 'Fechar composição' : 'Composição de custo'}</button> : null}
      </div>
      {error ? <p role="alert" className="text-xs text-crit-ink">{error}</p> : null}
      {open && cost && costs ? <CostEditor quoteId={quote.id} itemId={item.id} cost={cost} mode={costs.pricingMode} defaultPct={costs.defaultPct ?? null} version={quote.version} editable={editable} onSaved={refresh} /> : null}
    </li>
  )
}
