import type { MilestoneTrigger, TriggerEvent } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, PercentInput } from '@/components/form/inputs'
import type { RowsColumn } from '@/components/form/rows-editor'
import { Money } from '@/components/ui/money'
import { EVENTS, type MilestoneDraft } from './milestones'

/** Colunas do editor de marcos (CA 1 e 2): descrição, %, valor ao lado, gatilho e quando (data ou evento + prazo). */
export function milestoneColumns(values: number[]): RowsColumn<MilestoneDraft>[] {
  return [
    { label: 'Descrição', width: 'minmax(8rem, 2fr)', render: (r, i, set) => <input aria-label={`Descrição do marco ${i + 1}`} value={r.description} maxLength={120} onChange={(e) => set({ description: e.target.value })} className={inputClass} /> },
    { label: '%', width: '7rem', render: (r, i, set) => <PercentInput aria-label={`Percentual do marco ${i + 1}`} scale={4} value={r.percent} onChange={(v) => set({ percent: v })} /> },
    { label: 'Valor', width: '8rem', render: (_r, i) => <Money value={values[i] ?? 0} /> },
    { label: 'Gatilho', width: '7rem', render: (r, i, set) => <select aria-label={`Gatilho do marco ${i + 1}`} value={r.trigger} onChange={(e) => set({ trigger: e.target.value as MilestoneTrigger })} className={inputClass}><option value="Event">Evento</option><option value="Date">Data</option></select> },
    { label: 'Quando', width: 'minmax(12rem, 2fr)', render: (r, i, set) => (r.trigger === 'Date'
      ? <DateInput aria-label={`Data do marco ${i + 1}`} value={r.dueDate} onChange={(v) => set({ dueDate: v })} />
      : (
        <span className="flex gap-1">
          <select aria-label={`Evento do marco ${i + 1}`} value={r.triggerEvent} onChange={(e) => set({ triggerEvent: e.target.value as NonNullable<TriggerEvent> })} className={inputClass}>{EVENTS.map((ev) => <option key={ev.value} value={ev.value}>{ev.label}</option>)}</select>
          <input aria-label={`Prazo após o evento do marco ${i + 1}`} inputMode="numeric" maxLength={4} value={r.days} onChange={(e) => set({ days: e.target.value.replace(/\D/g, '') })} className={inputClass + ' w-16 tabular-nums'} />
        </span>
      )) },
  ]
}
