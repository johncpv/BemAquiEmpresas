import { useState } from 'react'
import { getLastMaterialCost, setQuoteItemCosts, useListQuotePositions, useListQuoteWorkCenters } from '@/api/generated/bemaqui'
import type { CostType, QuoteCostDetail, QuoteCostLineView, QuoteItemCostView, QuotePricingMode } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { PercentInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { type CostDraft, costColumns, emptyCost } from './cost-columns'
import { COST_SECTIONS, lineTotal, pctLabel, QUOTE_DICAS, suggested } from './quotes'

const toDraft = (l: QuoteCostLineView): CostDraft => ({
  item: l.itemId ? { id: l.itemId, label: l.itemName ?? '', unit: l.unitCode ?? undefined } : null, party: l.partyId ? { id: l.partyId, label: 'Instalador' } : null,
  description: l.description ?? '', workCenterId: l.workCenterId ?? '', positionId: l.positionId ?? '', quantity: String(l.quantity), unitCost: String(l.unitCost),
})
const num = (v: string | null) => (v ? Number(v) : 0)

interface Props { quoteId: string; itemId: string; cost: QuoteItemCostView; mode: QuotePricingMode; defaultPct: number | null; version: number; editable: boolean; onSaved: (d: QuoteCostDetail) => Promise<void> }

/** Composição de custo do item (CA 8 a 12): uma seção por natureza, subtotal, custo unitário e preço sugerido ao vivo. */
export function CostEditor({ quoteId, itemId, cost, mode, defaultPct, version, editable, onSaved }: Props) {
  const positions = useListQuotePositions()
  const centers = useListQuoteWorkCenters()
  const [rows, setRows] = useState<Record<CostType, CostDraft[]>>(() => Object.fromEntries(COST_SECTIONS.map((s) => [s.type, cost.lines.filter((l) => l.costType === s.type).map(toDraft)])) as Record<CostType, CostDraft[]>)
  const [pct, setPct] = useState<string | null>(cost.pct === null || cost.pct === undefined ? null : String(cost.pct))
  const [error, setError] = useState<string | undefined>()
  const [saving, setSaving] = useState(false)
  const hourly = (id: string) => positions.data?.find((p) => p.id === id)?.hourlyCost ?? 0
  const lineCost = (type: CostType, r: CostDraft) => (type === 'Labor' ? lineTotal(num(r.quantity), hourly(r.positionId)) : type === 'Material' ? lineTotal(num(r.quantity), num(r.unitCost)) : num(r.unitCost))
  const subtotal = (type: CostType) => rows[type].reduce((n, r) => n + lineCost(type, r), 0)
  const unitCost = COST_SECTIONS.reduce((n, s) => n + subtotal(s.type), 0)
  const effective = pct === null || pct === '' ? defaultPct : Number(pct)
  const price = effective === null ? null : suggested(unitCost, mode, effective)
  const material = (row: number, id: string) => {
    void getLastMaterialCost(id).then((r) => {
      if (r.unitCost !== null && r.unitCost !== undefined) setRows((s) => ({ ...s, Material: s.Material.map((x, k) => (k === row && !x.unitCost ? { ...x, unitCost: String(r.unitCost) } : x)) }))
    })
  }

  const save = async () => {
    setSaving(true)
    setError(undefined)
    try {
      const lines = COST_SECTIONS.flatMap((s) => rows[s.type].map((r) => ({
        costType: s.type, description: r.description.trim() || null, itemId: r.item?.id ?? null, workCenterId: r.workCenterId || null, positionId: r.positionId || null, partyId: r.party?.id ?? null,
        quantity: s.type === 'Material' || s.type === 'Labor' ? num(r.quantity) : 1, unitCost: s.type === 'Labor' ? null : num(r.unitCost),
      })))
      const saved = await setQuoteItemCosts(quoteId, itemId, { lines, pct: pct === null || pct === '' ? null : Number(pct), version })
      notifySuccess('Composição salva')
      await onSaved(saved)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar a composição.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <fieldset disabled={!editable} className="space-y-4 rounded-md border border-border bg-background p-3">
      <legend className="px-1 text-sm font-semibold">Composição de custo</legend>
      <FormError message={error} />
      {COST_SECTIONS.map((s) => (
        <section key={s.type} aria-label={s.label} className="space-y-2">
          <h3 className="flex items-center justify-between text-sm font-semibold">
            <span className="inline-flex items-center">{s.label}{s.type === 'Tax' ? <Dica nome="Impostos estimados" texto={QUOTE_DICAS.impostos} /> : s.type === 'Labor' ? <Dica nome="Custo-hora do cargo" texto={QUOTE_DICAS.custoHora} /> : null}</span>
            <span className="text-xs font-normal">Subtotal <Money value={subtotal(s.type)} /></span>
          </h3>
          <RowsEditor noun={`linha de ${s.label.toLowerCase()}`} rows={rows[s.type]} onChange={(next) => setRows((r) => ({ ...r, [s.type]: next }))} newRow={emptyCost}
            columns={costColumns(s.type, positions.data ?? [], centers.data ?? [], material)} duplicate disabled={!editable} />
        </section>
      ))}
      <div className="flex flex-wrap items-end justify-between gap-3 border-t border-border pt-3 text-sm">
        <label className="space-y-1 font-medium">
          <span className="inline-flex items-center">{mode === 'Markup' ? 'Markup do item' : 'Margem do item'}<Dica nome={mode === 'Markup' ? 'Markup' : 'Margem'} texto={mode === 'Markup' ? QUOTE_DICAS.markup : QUOTE_DICAS.margem} /></span>
          <PercentInput scale={4} value={pct} placeholder={defaultPct === null ? 'Sem padrão' : `Padrão ${defaultPct}`} onChange={setPct} />
        </label>
        <p aria-live="polite" className="space-y-0.5 text-right">
          <span className="block">Custo unitário<Dica nome="Custo unitário do item" texto={QUOTE_DICAS.custoUnitario} />: <Money value={unitCost} className="font-semibold" /></span>
          <span className="block">Preço sugerido: {price === null ? <span className="text-muted-foreground">{effective === null ? 'defina o %' : '—'}</span> : <Money value={price} className="font-semibold" />}</span>
          {effective !== null ? <span className="block text-xs text-muted-foreground">{pctLabel(mode, effective)}</span> : <span className="block text-xs text-muted-foreground">{QUOTE_DICAS.padrao}</span>}
        </p>
      </div>
      {editable ? <div className="flex justify-end"><button type="button" disabled={saving} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50">Salvar composição</button></div> : null}
    </fieldset>
  )
}
