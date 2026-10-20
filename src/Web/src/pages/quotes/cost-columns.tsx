import { listQuoteInstallers, listQuoteItems } from '@/api/generated/bemaqui'
import type { CostType, LaborPositionOption, LaborWorkCenterOption } from '@/api/generated/model'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput, QuantityInput } from '@/components/form/inputs'
import type { RowsColumn } from '@/components/form/rows-editor'
import { Money } from '@/components/ui/money'
import { lineTotal } from './quotes'

/** Linha da composição no editor (dinheiro e quantidade como string normalizada). */
export interface CostDraft { item: (ComboOption & { unit?: string }) | null; party: ComboOption | null; description: string; workCenterId: string; positionId: string; quantity: string | null; unitCost: string | null }

export const emptyCost = (): CostDraft => ({ item: null, party: null, description: '', workCenterId: '', positionId: '', quantity: null, unitCost: null })

const num = (v: string | null) => (v ? Number(v) : 0)
const searchItems = async (term: string) => (await listQuoteItems({ q: term })).map((i) => ({ id: i.id, label: `${i.code} ${i.name}`, unit: i.unitCode }))
const searchInstallers = async (term: string) => (await listQuoteInstallers({ q: term })).map((p) => ({ id: p.id, label: p.name, hint: p.document ?? undefined }))

/** Colunas por natureza (CA 8 a 11): material com item e quantidade; mão de obra com centro, cargo e horas; demais pelo valor. */
export function costColumns(type: CostType, positions: LaborPositionOption[], centers: LaborWorkCenterOption[], onMaterial: (row: number, id: string) => void): RowsColumn<CostDraft>[] {
  const total = (r: CostDraft) => <Money value={type === 'Labor' ? lineTotal(num(r.quantity), positions.find((p) => p.id === r.positionId)?.hourlyCost ?? 0) : type === 'Material' ? lineTotal(num(r.quantity), num(r.unitCost)) : num(r.unitCost)} />
  if (type === 'Material') return [
    { label: 'Item', width: 'minmax(10rem, 2fr)', render: (r, i, set) => <Combobox aria-label={`Item da linha ${i + 1}`} queryKey={['quote-items']} search={searchItems} value={r.item?.id ?? null} selected={r.item} placeholder="Busque o item" onChange={(o) => { set({ item: o as CostDraft['item'] }); if (o) onMaterial(i, o.id) }} /> },
    { label: 'Quantidade', width: '7rem', render: (r, i, set) => <span className="flex items-center gap-1"><QuantityInput aria-label={`Quantidade da linha ${i + 1}`} value={r.quantity} onChange={(v) => set({ quantity: v })} /><span className="text-xs text-muted-foreground">{r.item?.unit}</span></span> },
    { label: 'Custo unitário', width: '8rem', render: (r, i, set) => <MoneyInput aria-label={`Custo unitário da linha ${i + 1}`} value={r.unitCost} onChange={(v) => set({ unitCost: v })} /> },
    { label: 'Total', width: '7rem', render: total },
  ]
  if (type === 'Labor') return [
    { label: 'Centro de trabalho', width: 'minmax(8rem, 1fr)', render: (r, i, set) => (
      <select aria-label={`Centro da linha ${i + 1}`} value={r.workCenterId} onChange={(e) => { const only = positions.filter((p) => p.defaultWorkCenterId === e.target.value); set({ workCenterId: e.target.value, positionId: only.length === 1 ? only[0].id : r.positionId }) }} className={inputClass}>
        <option value="">Sem centro</option>{centers.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select>
    ) },
    { label: 'Cargo', width: 'minmax(8rem, 1fr)', render: (r, i, set) => <select aria-label={`Cargo da linha ${i + 1}`} value={r.positionId} onChange={(e) => set({ positionId: e.target.value })} className={inputClass}><option value="">Escolha</option>{positions.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select> },
    { label: 'Horas', width: '6rem', render: (r, i, set) => <QuantityInput aria-label={`Horas da linha ${i + 1}`} value={r.quantity} onChange={(v) => set({ quantity: v })} /> },
    { label: 'Total', width: '7rem', render: total },
  ]
  return [
    ...(type === 'Installation' ? [{ label: 'Instalador (opcional)', width: 'minmax(8rem, 1fr)', render: (r: CostDraft, i: number, set: (p: Partial<CostDraft>) => void) => <Combobox aria-label={`Instalador da linha ${i + 1}`} queryKey={['quote-installers']} search={searchInstallers} value={r.party?.id ?? null} selected={r.party} placeholder="Busque" onChange={(o) => set({ party: o })} /> }] : []),
    { label: 'Descrição', width: 'minmax(8rem, 2fr)', render: (r, i, set) => <input aria-label={`Descrição da linha ${i + 1}`} value={r.description} maxLength={200} onChange={(e) => set({ description: e.target.value })} className={inputClass} /> },
    { label: 'Valor', width: '8rem', render: (r, i, set) => <MoneyInput aria-label={`Valor da linha ${i + 1}`} value={r.unitCost} onChange={(v) => set({ unitCost: v })} /> },
  ]
}
