import { inputClass } from '@/components/form/form-utils'
import { DateInput, PercentInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { emptyMilestone, milestoneAmounts, type MilestoneDraft } from './amendments'

const EVENTS = [{ value: 'Signature', label: 'Assinatura' }, { value: 'FabricationDone', label: 'Fabricação concluída' }, { value: 'Delivered', label: 'Entrega' }, { value: 'TechnicalAcceptance', label: 'Aceite técnico' }]

/** Marcos do acréscimo (COM-07 CA 3): somam 100%; o valor de cada um sai pelo maior resto, como no servidor. */
export function AmendmentMilestones({ value, rows, onChange }: { value: number; rows: MilestoneDraft[]; onChange: (rows: MilestoneDraft[]) => void }) {
  const amounts = milestoneAmounts(value, rows.map((r) => Number(r.percent ?? 0)))
  const sum = rows.reduce((a, r) => a + Number(r.percent ?? 0), 0)
  const set = (i: number, patch: Partial<MilestoneDraft>) => onChange(rows.map((r, k) => (k === i ? { ...r, ...patch } : r)))
  return (
    <div className="space-y-2">
      {rows.map((r, i) => (
        <div key={i} className="grid gap-2 rounded-md border border-border bg-card p-3 sm:grid-cols-[2fr_1fr_1fr_1fr_auto]" aria-label={`Marco ${i + 1}`}>
          <input aria-label={`Descrição do marco ${i + 1}`} value={r.description} maxLength={120} onChange={(e) => set(i, { description: e.target.value })} className={inputClass} placeholder="Descrição" />
          <PercentInput aria-label={`Percentual do marco ${i + 1}`} value={r.percent} onChange={(percent) => set(i, { percent })} />
          <select aria-label={`Gatilho do marco ${i + 1}`} value={r.trigger === 'Date' ? 'Date' : r.triggerEvent} onChange={(e) => set(i, e.target.value === 'Date' ? { trigger: 'Date' } : { trigger: 'Event', triggerEvent: e.target.value })} className={inputClass}>
            <option value="Date">Data</option>
            {EVENTS.map((ev) => <option key={ev.value} value={ev.value}>{ev.label}</option>)}
          </select>
          {r.trigger === 'Date'
            ? <DateInput aria-label={`Data do marco ${i + 1}`} value={r.dueDate} onChange={(dueDate) => set(i, { dueDate })} />
            : <input aria-label={`Dias após o evento do marco ${i + 1}`} inputMode="numeric" value={r.days} onChange={(e) => set(i, { days: e.target.value.replace(/\D/g, '') })} className={inputClass} placeholder="Dias após" />}
          <span className="flex items-center gap-2"><Money value={amounts[i] ?? 0} />{rows.length > 1 ? <button type="button" onClick={() => onChange(rows.filter((_, k) => k !== i))} className="text-xs font-semibold text-crit-ink">Remover</button> : null}</span>
        </div>
      ))}
      <p className="flex flex-wrap items-center justify-between gap-2 text-sm">
        <button type="button" onClick={() => onChange([...rows, emptyMilestone()])} className="rounded-md border border-border px-3 py-1.5 font-semibold">Adicionar marco</button>
        <span className={sum === 100 ? 'tabular-nums' : 'tabular-nums text-crit-ink'}>Soma {sum.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}%</span>
      </p>
    </div>
  )
}

