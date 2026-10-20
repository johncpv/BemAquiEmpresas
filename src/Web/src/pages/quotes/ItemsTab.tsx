import { useState } from 'react'
import { addQuoteItem } from '@/api/generated/bemaqui'
import type { QuoteCostDetail, QuoteDetail, QuoteItemKind } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { QuantityInput } from '@/components/form/inputs'
import { EmptyState } from '@/components/ui/states'
import { ItemCard } from './ItemCard'
import { ITEM_KINDS } from './quotes'

/** Aba Itens (CA 7 a 15): cada item com a composição; "Adicionar item" no fim. */
export function ItemsTab({ quote, costs, editable, refresh }: { quote: QuoteDetail; costs?: QuoteCostDetail; editable: boolean; refresh: () => Promise<void> }) {
  const [kind, setKind] = useState<QuoteItemKind>('Equipment')
  const [description, setDescription] = useState('')
  const [quantity, setQuantity] = useState<string | null>('1')
  const [error, setError] = useState<string | undefined>()
  const add = async () => {
    setError(undefined)
    try {
      await addQuoteItem(quote.id, { kind, itemId: null, description: description.trim(), quantity: quantity ? Number(quantity) : 0, unitPrice: null, resetPrice: false, version: quote.version })
      setDescription('')
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível adicionar o item.')
    }
  }

  return (
    <div className="space-y-3">
      <FormError message={error} />
      {quote.items.length === 0 ? <EmptyState title="Adicione o equipamento ou serviço que o cliente pediu." /> : (
        <ul className="space-y-3" aria-label="Itens do orçamento">
          {quote.items.map((i) => <ItemCard key={`${i.id}-${quote.version}`} quote={quote} item={i} costs={costs} editable={editable} refresh={refresh} />)}
        </ul>
      )}
      {editable ? (
        <div className="grid gap-2 rounded-md border border-dashed border-border p-3 sm:grid-cols-[9rem_minmax(0,1fr)_7rem_auto] sm:items-end">
          <label className="space-y-1 text-xs font-medium">Tipo<select value={kind} onChange={(e) => setKind(e.target.value as QuoteItemKind)} className={inputClass}>{ITEM_KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}</select></label>
          <label className="space-y-1 text-xs font-medium">Descrição do novo item<input value={description} maxLength={300} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Ex.: Elevador de passageiros, 6 paradas" /></label>
          <label className="space-y-1 text-xs font-medium">Quantidade<QuantityInput value={quantity} onChange={setQuantity} /></label>
          <button type="button" onClick={() => void add()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Adicionar item</button>
        </div>
      ) : null}
    </div>
  )
}
