import { useRef, useState } from 'react'
import { addQuoteItem, listQuoteItems } from '@/api/generated/bemaqui'
import type { ItemType, QuoteCostDetail, QuoteDetail, QuoteItemKind } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { QuantityInput } from '@/components/form/inputs'
import { EmptyState } from '@/components/ui/states'
import { TYPE_LABELS } from '../inventory/item-labels'
import { ItemCard } from './ItemCard'
import { ITEM_KINDS, ITEM_SEARCH_LIMIT, itemOptionHint, kindForItemType } from './quotes'

/** Resultado da busca de item (PRC-00): o que a linha precisa para nascer certa. */
type PickedItem = ComboOption & { name: string; unit: string; type: ItemType }

const searchItems = async (term: string): Promise<PickedItem[]> =>
  (await listQuoteItems({ q: term })).map((i) => ({
    id: i.id, label: `${i.code} ${i.name}`, name: i.name, unit: i.unitCode, type: i.type,
    hint: itemOptionHint(TYPE_LABELS[i.type] ?? i.type, i.unitCode, i.averageCost),
  }))

const kindLabel = (kind: QuoteItemKind) => ITEM_KINDS.find((k) => k.value === kind)?.label ?? kind

/** Aba Itens (CA 7 a 15): cada item com a composição; "Adicionar item" no fim, buscando o item cadastrado ou digitando à mão (PRC-00). */
export function ItemsTab({ quote, costs, editable, refresh }: { quote: QuoteDetail; costs?: QuoteCostDetail; editable: boolean; refresh: () => Promise<void> }) {
  const [manual, setManual] = useState(false)
  const [picked, setPicked] = useState<PickedItem | null>(null)
  const [kind, setKind] = useState<QuoteItemKind>('Equipment')
  const [description, setDescription] = useState('')
  const [quantity, setQuantity] = useState<string | null>('1')
  const [error, setError] = useState<string | undefined>()
  const quantityRef = useRef<HTMLInputElement>(null)

  const pick = (option: ComboOption | null) => {
    const item = option as PickedItem | null
    setPicked(item)
    if (item) {
      setDescription(item.name)
      setTimeout(() => quantityRef.current?.focus(), 0)
    }
  }
  const switchMode = () => { setManual(!manual); setPicked(null); setDescription(''); setError(undefined) }

  const add = async () => {
    setError(undefined)
    if (!manual && !picked) { setError('Escolha o item cadastrado ou use "Digitar à mão".'); return }
    try {
      await addQuoteItem(quote.id, {
        kind: manual || !picked ? kind : kindForItemType(picked.type), itemId: manual || !picked ? null : picked.id,
        description: description.trim(), quantity: quantity ? Number(quantity) : 0, unitPrice: null, resetPrice: false, version: quote.version,
      })
      setDescription('')
      setPicked(null)
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
        <div className="space-y-2 rounded-md border border-dashed border-border p-3">
          {manual ? (
            <div className="grid gap-2 sm:grid-cols-[9rem_minmax(0,1fr)_7rem_auto] sm:items-end">
              <label className="space-y-1 text-xs font-medium">Tipo<select value={kind} onChange={(e) => setKind(e.target.value as QuoteItemKind)} className={inputClass}>{ITEM_KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}</select></label>
              <label className="space-y-1 text-xs font-medium">Descrição do novo item<input value={description} maxLength={300} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Ex.: Elevador de passageiros, 6 paradas" /></label>
              <label className="space-y-1 text-xs font-medium">Quantidade<QuantityInput value={quantity} onChange={setQuantity} /></label>
              <button type="button" onClick={() => void add()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Adicionar item</button>
            </div>
          ) : (
            <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_8rem_auto] sm:items-end">
              <div className="min-w-0 space-y-1 text-xs font-medium">
                <span aria-hidden="true">Item cadastrado</span>
                <Combobox aria-label="Item cadastrado" queryKey={['quote-item-search']} search={searchItems} value={picked?.id ?? null} selected={picked} onChange={pick} placeholder="Busque por código, nome ou código de barras" />
              </div>
              <label className="space-y-1 text-xs font-medium">Descrição<input value={description} maxLength={300} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Vem do item escolhido" /></label>
              <label className="space-y-1 text-xs font-medium">Quantidade{picked ? ` (${picked.unit})` : ''}<QuantityInput ref={quantityRef} value={quantity} onChange={setQuantity} /></label>
              <button type="button" onClick={() => void add()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Adicionar item</button>
            </div>
          )}
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
            {!manual && picked ? <span>Entra como {kindLabel(kindForItemType(picked.type))}, ligado ao item.</span> : null}
            {!manual && !picked ? <span>Mostra até {ITEM_SEARCH_LIMIT} itens; digite mais do código ou do nome para refinar.</span> : null}
            <button type="button" onClick={switchMode} className="font-semibold text-foreground underline">{manual ? 'Buscar item cadastrado' : 'Digitar à mão'}</button>
          </p>
        </div>
      ) : null}
    </div>
  )
}
