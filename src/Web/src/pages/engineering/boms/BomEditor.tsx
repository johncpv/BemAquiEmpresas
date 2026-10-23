import { type ReactNode, useState } from 'react'
import { flushSync } from 'react-dom'
import { listBomItemOptions } from '@/api/generated/bemaqui'
import type { BomItemOption } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { PercentInput, QuantityInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { InlineError } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { BOM_DICAS, type BomLineDraft, emptyLine, lineProblems, qty, requirement, shortage } from './boms'

interface BomEditorProps {
  orderId: string
  orderQuantity: number
  initial: BomLineDraft[]
  canEdit: boolean
  /** Saldo do estoque ligado (EST-01a): sem ele, a falta não é calculada. */
  stockConnected: boolean
  onSubmit: (lines: BomLineDraft[]) => Promise<void>
  /** Avisa o pai quando há mudança não salva (a liberação pede salvar antes). */
  onDirtyChange?: (dirty: boolean) => void
  footerExtra?: ReactNode
  estimatedCost?: ReactNode
}

/**
 * Editor do rascunho da BOM (ENG-02 CA 2, 3, 5, 6): item (ativos, sem Serviço e sem o produto da OS), quantidade por unidade do produto,
 * unidade (a de estoque por padrão), perda % e observação. A necessidade da OS, o disponível e o chip "Falta" aparecem ao vivo; o total
 * de itens em falta fica no rodapé fixo (aria-live). A 400 px cada linha vira cartão (RowsEditor).
 */
export function BomEditor({ orderId, orderQuantity, initial, canEdit, stockConnected, onSubmit, onDirtyChange, footerExtra, estimatedCost }: BomEditorProps) {
  const [lines, setLines] = useState<BomLineDraft[]>(initial)
  const [baseline, setBaseline] = useState<BomLineDraft[]>(initial)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<string | undefined>()
  const dirty = JSON.stringify(lines) !== JSON.stringify(baseline)
  const [lastDirty, setLastDirty] = useState(dirty)
  if (lastDirty !== dirty) {
    setLastDirty(dirty)
    onDirtyChange?.(dirty)
  }

  const lineField = (i: number, name: string) => fields[`lines[${i}].${name}`]
  const change = (next: BomLineDraft[]) => {
    setLines(next)
    setFields(Object.fromEntries(Object.entries(fields).filter(([k]) => !k.startsWith('lines'))))
  }

  const focusFirstError = () => requestAnimationFrame(() => document.querySelector<HTMLElement>('#bom [aria-invalid="true"]')?.focus())

  const save = async () => {
    const problems = lineProblems(lines)
    if (Object.keys(problems).length > 0) {
      setFields(problems)
      setMessage(undefined)
      focusFirstError()
      return false
    }

    try {
      await onSubmit(lines)
      flushSync(() => {
        setBaseline(lines)
        setFields({})
        setMessage(undefined)
      })
      return true
    } catch (e) {
      if (e instanceof ApiError) {
        const flat = Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))
        setFields(flat)
        setMessage(Object.keys(flat).length === 0 || Object.keys(flat).every((k) => k === 'lines') ? e.message : undefined)
        if (Object.keys(flat).length > 0) focusFirstError()
      } else {
        setMessage('Não foi possível salvar a BOM. Tente de novo.')
      }
      return false
    }
  }

  const items = (term: string) => listBomItemOptions(orderId, { q: term || undefined }).then((found) => found.map((i) => ({ id: i.id, label: `${i.code} · ${i.name}`, hint: i.stockUnitCode, item: i })))
  const pick = (option: { id: string } | null, found: Map<string, BomItemOption>): Partial<BomLineDraft> => {
    const item = option ? found.get(option.id) : undefined
    if (!item) return { itemId: null, itemCode: '', itemName: '', units: [], unitId: null }
    // Unidade de estoque preenchida (propositivo); outra só com a conversão do item.
    return {
      itemId: item.id, itemCode: item.code, itemName: item.name, itemActive: true, stockUnitId: item.stockUnitId, stockUnitCode: item.stockUnitCode, units: item.units,
      unitId: item.stockUnitId, available: item.available ?? null, onHand: null, reserved: null,
    }
  }
  const [options] = useState(() => new Map<string, BomItemOption>())

  const computed = lines.map((l) => {
    const required = requirement(l, orderQuantity)
    return { required, missing: stockConnected ? shortage(required, l.available) : null }
  })
  const missingCount = computed.filter((c) => (c.missing ?? 0) > 0).length

  return (
    <form id="bom" noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="space-y-4 pb-4">
      <UnsavedChangesGuard when={dirty && canEdit} onSave={save} />
      <FormError message={message} />
      <fieldset disabled={!canEdit}>
        <legend className="sr-only">Linhas da BOM</legend>
        <p className="mb-2 flex flex-wrap items-center gap-x-3 text-xs text-muted-foreground">
          <span className="inline-flex items-center">Perda %<Dica nome="Perda %" texto={BOM_DICAS.perda} /></span>
          <span className="inline-flex items-center">Necessidade da OS<Dica nome="Necessidade da OS" texto={BOM_DICAS.necessidade} /></span>
          <span>Quantidade por unidade do produto; a OS tem {qty(orderQuantity)} {orderQuantity === 1 ? 'unidade' : 'unidades'}.</span>
        </p>
        <RowsEditor noun="material" rows={lines} onChange={change} newRow={emptyLine} min={0} max={200} disabled={!canEdit}
          rowError={(i) => lineField(i, 'id')}
          columns={[
            {
              label: 'Item', width: 'minmax(0, 2.4fr)', render: (row, i, update) => (
                <div>
                  <Combobox aria-label={`Item da linha ${i + 1}`} aria-required aria-invalid={!!lineField(i, 'itemId')} aria-describedby={lineField(i, 'itemId') ? `bom-${i}-item` : undefined}
                    queryKey={['bom-item-options', orderId]} placeholder="Buscar item"
                    search={async (term) => {
                      const found = await items(term)
                      found.forEach((f) => options.set(f.id, f.item))
                      return found.map(({ id, label, hint }) => ({ id, label, hint }))
                    }}
                    selected={row.itemId ? { id: row.itemId, label: `${row.itemCode} · ${row.itemName}` } : null}
                    onChange={(o) => update(pick(o, options))} />
                  {row.itemId && !row.itemActive ? <span className="mt-1 inline-flex"><StatusChip tone="warn">Item inativo: revise</StatusChip></span> : null}
                  <InlineError id={`bom-${i}-item`} message={lineField(i, 'itemId')} />
                </div>
              ),
            },
            {
              label: 'Qtd. por unidade', width: '8.5rem', render: (row, i, update) => (
                <div>
                  <QuantityInput aria-label={`Quantidade por unidade da linha ${i + 1}`} aria-invalid={!!lineField(i, 'quantity')} aria-describedby={lineField(i, 'quantity') ? `bom-${i}-qty` : undefined}
                    value={row.quantity} onChange={(v) => update({ quantity: v })} />
                  <InlineError id={`bom-${i}-qty`} message={lineField(i, 'quantity')} />
                </div>
              ),
            },
            {
              label: 'Unidade', width: '6rem', render: (row, i, update) => (
                <div>
                  <select aria-label={`Unidade da linha ${i + 1}`} aria-invalid={!!lineField(i, 'unitId')} aria-describedby={lineField(i, 'unitId') ? `bom-${i}-unit` : undefined}
                    value={row.unitId ?? ''} disabled={!row.itemId} onChange={(e) => update({ unitId: e.target.value || null })} className={inputClass}>
                    {row.units.length === 0 ? <option value="">—</option> : null}
                    {row.units.map((u) => <option key={u.unitId} value={u.unitId}>{u.code}</option>)}
                  </select>
                  <InlineError id={`bom-${i}-unit`} message={lineField(i, 'unitId')} />
                </div>
              ),
            },
            {
              label: 'Perda %', width: '7rem', render: (row, i, update) => (
                <div>
                  <PercentInput aria-label={`Perda da linha ${i + 1}`} aria-invalid={!!lineField(i, 'scrapPct')} value={row.scrapPct} onChange={(v) => update({ scrapPct: v })} />
                  <InlineError id={`bom-${i}-scrap`} message={lineField(i, 'scrapPct')} />
                </div>
              ),
            },
            {
              label: 'Necessidade da OS', width: '9rem', render: (row, i) => (
                <div className="text-sm tabular-nums" aria-label={`Necessidade da OS na linha ${i + 1}`}>
                  <b className="font-semibold">{qty(computed[i]?.required, row.stockUnitCode || undefined)}</b>
                  {stockConnected && row.itemId ? <span className="block text-xs text-muted-foreground">Disponível {qty(row.available, row.stockUnitCode)}</span> : null}
                  {(computed[i]?.missing ?? 0) > 0 ? <span className="mt-1 inline-flex"><StatusChip tone="crit">Falta {qty(computed[i].missing, row.stockUnitCode)}</StatusChip></span> : null}
                </div>
              ),
            },
            {
              label: 'Observação', width: 'minmax(0, 1.4fr)', render: (row, i, update) => (
                <div>
                  <input aria-label={`Observação da linha ${i + 1}`} aria-invalid={!!lineField(i, 'notes')} value={row.notes} maxLength={200} onChange={(e) => update({ notes: e.target.value })} className={inputClass} />
                  <InlineError id={`bom-${i}-notes`} message={lineField(i, 'notes')} />
                </div>
              ),
            },
          ]} />
      </fieldset>

      <div className="sticky bottom-20 z-10 flex flex-wrap items-center justify-between gap-3 rounded-md border border-border bg-card px-4 py-2 shadow-sm">
        <p aria-live="polite" className="text-sm tabular-nums">
          {lines.length} {lines.length === 1 ? 'item' : 'itens'}
          {stockConnected ? <> · <b className={missingCount > 0 ? 'text-crit-ink' : undefined}>{missingCount} em falta</b></> : <> · saldo do estoque ainda não aparece</>}
          {estimatedCost ? <> · {estimatedCost}</> : null}
        </p>
        <div className="flex flex-wrap gap-2">
          {canEdit ? <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar BOM</button> : null}
          {footerExtra}
        </div>
      </div>
    </form>
  )
}
