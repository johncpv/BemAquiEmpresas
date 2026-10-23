import { useState } from 'react'
import { createOpeningBalanceBatch, searchItems, useListLocations } from '@/api/generated/bemaqui'
import type { ManualPostingDetail } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput, QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { DICAS, lineTotal, parsePastedLines } from './stock'

interface Row { key: string; item: ComboOption | null; locationId: string; quantity: string | null; unitCost: string | null; lotNumber: string }

const search = async (term: string): Promise<ComboOption[]> =>
  (await searchItems({ q: term || undefined })).filter((i) => i.type !== 'Service').map((i) => ({ id: i.id, label: `${i.code} — ${i.name}` }))

const todayIso = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
const newRow = (locationId: string): Row => ({ key: crypto.randomUUID(), item: null, locationId, quantity: null, unitCost: null, lotNumber: '' })

/**
 * "Saldo inicial em lote" (CA 4, R-PRP-3): várias linhas numa transação só, com um número MOV; colar do Excel (código,
 * quantidade, custo e lote) preenche as linhas. Uma linha inválida volta com o erro nela e nada é gravado.
 */
export function OpeningBatchDialog({ onClose, onPosted }: { onClose: () => void; onPosted: (detail: ManualPostingDetail) => void }) {
  const locations = useListLocations({ pageSize: 100 })
  const active = (locations.data?.items ?? []).filter((l) => l.isActive)
  const fallback = active.find((l) => l.isDefault)?.id ?? active[0]?.id ?? ''
  const [cutoff, setCutoff] = useState(todayIso())
  const [rows, setRows] = useState<Row[]>(() => [newRow('')])
  const [paste, setPaste] = useState('')
  const [pasteError, setPasteError] = useState<string | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const idempotency = useIdempotencyKey()
  const set = (key: string, patch: Partial<Row>) => setRows((all) => all.map((r) => (r.key === key ? { ...r, ...patch } : r)))
  const loc = (r: Row) => r.locationId || fallback
  const filled = rows.filter((r) => r.item)
  const total = filled.reduce((sum, r) => sum + lineTotal(Number(r.quantity ?? 0), Number(r.unitCost ?? 0)), 0)

  const applyPaste = async () => {
    setPasteError(null)
    const lines = parsePastedLines(paste)
    const resolved: Row[] = []
    const missing: string[] = []
    for (const line of lines) {
      const hit = (await searchItems({ q: line.code })).find((i) => i.code.toLowerCase() === line.code.toLowerCase())
      if (!hit) { missing.push(line.code); continue }
      resolved.push({ key: crypto.randomUUID(), item: { id: hit.id, label: `${hit.code} — ${hit.name}` }, locationId: '', quantity: line.quantity, unitCost: line.unitCost, lotNumber: line.lotNumber })
    }
    setRows((all) => [...all.filter((r) => r.item), ...resolved])
    setPaste('')
    if (missing.length) setPasteError(`Código não encontrado: ${missing.join(', ')}.`)
  }

  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb="Lançar saldo inicial" done="Saldo inicial lançado" title="Saldo inicial em lote"
      description="Todas as linhas entram juntas, com um número só. Se uma linha tiver erro, nada é gravado."
      preconditions={[
        { ok: filled.length > 0, text: 'Ao menos uma linha com item.' },
        { ok: filled.every((r) => Number(r.quantity ?? 0) > 0 && Number(r.unitCost ?? 0) > 0), text: 'Quantidade e custo maiores que zero em todas as linhas.' },
        { ok: !!cutoff && cutoff <= todayIso(), text: 'Data de corte até hoje.' },
      ]}
      impact={<p>{filled.length} {filled.length === 1 ? 'linha' : 'linhas'} · valor total <Money value={total} className="font-semibold" /></p>}
      onFieldErrors={(e) => { setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))); return true }}
      onConfirm={async () => {
        setErrors({})
        const result = await createOpeningBalanceBatch({
          cutoffDate: cutoff,
          lines: filled.map((r) => ({ itemId: r.item!.id, locationId: loc(r), quantity: Number(r.quantity), unitId: null, unitCost: Number(r.unitCost), lotNumber: r.lotNumber.trim() || null })),
        }, idempotency.options)
        idempotency.renew()
        onPosted(result)
      }}>
      <Field label="Data de corte" required dica={DICAS.saldoInicial} error={errors.cutoffDate}>
        {(p) => <DateInput {...p} value={cutoff} onChange={setCutoff} />}
      </Field>
      <div className="space-y-1">
        <label htmlFor="colar-saldo" className="text-sm font-medium">Colar do Excel (código, quantidade, custo e lote por linha)</label>
        <textarea id="colar-saldo" value={paste} rows={3} onChange={(e) => setPaste(e.target.value)} className={inputClass} placeholder={'MP-001\t10\t25,50\nMP-002\t4\t8\tL-01'} />
        <button type="button" disabled={!paste.trim()} onClick={() => void applyPaste()} className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold disabled:opacity-60">Preencher linhas</button>
        {pasteError ? <p role="alert" className="text-sm text-crit-ink">{pasteError}</p> : null}
      </div>
      <ol className="space-y-2" aria-label="Linhas do saldo inicial">
        {rows.map((r, i) => (
          <li key={r.key} className="grid gap-2 rounded-md border border-border p-2 sm:grid-cols-[2fr_1fr_1fr_1fr_1fr_auto]">
            <div>
              <Combobox aria-label={`Item da linha ${i + 1}`} queryKey={['stock-items']} search={search} value={r.item?.id ?? null} selected={r.item} placeholder="Item" onChange={(o) => set(r.key, { item: o })} />
              {errors[`lines[${i}].itemId`] ? <p className="text-xs text-crit-ink">{errors[`lines[${i}].itemId`]}</p> : null}
            </div>
            <select aria-label={`Local da linha ${i + 1}`} value={loc(r)} onChange={(e) => set(r.key, { locationId: e.target.value })} className={inputClass}>
              {active.map((l) => <option key={l.id} value={l.id}>{l.code}</option>)}
            </select>
            <div>
              <QuantityInput aria-label={`Quantidade da linha ${i + 1}`} value={r.quantity} onChange={(v) => set(r.key, { quantity: v })} />
              {errors[`lines[${i}].quantity`] ? <p className="text-xs text-crit-ink">{errors[`lines[${i}].quantity`]}</p> : null}
            </div>
            <div>
              <MoneyInput aria-label={`Custo unitário da linha ${i + 1}`} value={r.unitCost} onChange={(v) => set(r.key, { unitCost: v })} />
              {errors[`lines[${i}].unitCost`] ? <p className="text-xs text-crit-ink">{errors[`lines[${i}].unitCost`]}</p> : null}
            </div>
            <div>
              <input aria-label={`Lote da linha ${i + 1}`} value={r.lotNumber} maxLength={40} onChange={(e) => set(r.key, { lotNumber: e.target.value })} placeholder="Lote" className={inputClass} />
              {errors[`lines[${i}].lotNumber`] ? <p className="text-xs text-crit-ink">{errors[`lines[${i}].lotNumber`]}</p> : null}
            </div>
            <button type="button" aria-label={`Remover linha ${i + 1}`} onClick={() => setRows((all) => (all.length > 1 ? all.filter((x) => x.key !== r.key) : [newRow('')]))} className="rounded-md border border-border px-2 text-sm">Remover</button>
          </li>
        ))}
      </ol>
      <button type="button" onClick={() => setRows((all) => [...all, newRow('')])} className="text-sm font-semibold text-primary">Adicionar linha</button>
    </ConfirmDialog>
  )
}
