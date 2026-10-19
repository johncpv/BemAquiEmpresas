import { useState } from 'react'
import { createSchedule, deleteSchedule, inactivateSchedule, reactivateSchedule, updateSchedule } from '@/api/generated/bemaqui'
import type { ScheduleDay, ScheduleDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { TimeInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { toastDone } from '@/components/ui/toast'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { dayMinutes, formatHours, WEEKDAY_LABELS } from './hr'

const nextDay = (days: ScheduleDay[]): ScheduleDay => {
  const last = days.at(-1)
  const weekday = [1, 2, 3, 4, 5, 6, 7].find((d) => !days.some((x) => x.weekday === d)) ?? 1
  return { weekday, start: last?.start ?? '', end: last?.end ?? '', breakMinutes: last?.breakMinutes ?? 60 }
}

/** Painel da jornada (CAD-04a CA 15): um dia por linha, com o total semanal e mensal ao vivo. O dia novo copia o anterior. */
export function SchedulePanel({ schedule, onClose, onSaved }: { schedule: ScheduleDetail | null; onClose: () => void; onSaved: () => void }) {
  const start = { name: schedule?.name ?? '', days: schedule?.days ?? [{ weekday: 1, start: '', end: '', breakMinutes: 60 }] }
  const [draft, setDraft] = useState(start)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan(schedule ? 'hr.edit' : 'hr.create')
  const dirty = JSON.stringify(draft) !== JSON.stringify(start)
  const weekly = draft.days.reduce((sum, d) => sum + dayMinutes(d.start, d.end, d.breakMinutes), 0) / 60

  const save = async () => {
    try {
      if (schedule) await updateSchedule(schedule.id, draft)
      else await createSchedule(draft)
      toastDone('Jornada salva')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a jornada.', fields: {} })
      return false
    }
  }

  const actions = schedule ? (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="hr.edit">
        {schedule.isActive ? (
          <ConfirmDialog verb="Inativar" done="Inativada" title={`Inativar a jornada ${schedule.name}?`} description="Ela some dos seletores e continua nos cargos que já a usam."
            onConfirm={async () => { await inactivateSchedule(schedule.id); onSaved() }} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar</button>} />
        ) : <button type="button" onClick={async () => { await reactivateSchedule(schedule.id); toastDone('Reativada'); onSaved() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>}
      </Can>
      <Can perm="hr.delete">
        <ConfirmDialog verb="Excluir" done="Excluída" destructive title={`Excluir a jornada ${schedule.name}?`} description="Só dá para excluir jornada que nenhum cargo usa."
          onConfirm={async () => { await deleteSchedule(schedule.id); onSaved() }}
          recovery={schedule.isActive ? { verb: 'Inativar', done: 'Inativada', when: (code) => code === 'schedule_in_use', onSelect: async () => { await inactivateSchedule(schedule.id); onSaved() } } : undefined}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir</button>} />
      </Can>
    </span>
  ) : null

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={schedule ? `Jornada ${schedule.name}` : 'Nova jornada'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          {actions}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar jornada</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error?.fields.days?.[0] ?? (error && Object.keys(error.fields).length === 0 ? error.message : undefined)} />
        <Field label="Nome" required error={error?.fields.name?.[0]}>{(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}</Field>
        <RowsEditor noun="dia" rows={draft.days} min={1} max={7} disabled={!canWrite} headers={['Dia', 'Entrada', 'Saída', 'Intervalo (min)']} newRow={() => nextDay(draft.days)}
          onChange={(days) => setDraft({ ...draft, days })}
          renderRow={(row, i, update) => [
            <select key="d" aria-label={`Dia ${i + 1}`} value={row.weekday} onChange={(e) => update({ weekday: Number(e.target.value) })} className={inputClass}>
              {WEEKDAY_LABELS.slice(1).map((label, k) => <option key={label} value={k + 1}>{label}</option>)}
            </select>,
            <TimeInput key="s" aria-label={`Entrada do dia ${i + 1}`} value={row.start} onChange={(start) => update({ start })} />,
            <TimeInput key="e" aria-label={`Saída do dia ${i + 1}`} value={row.end} onChange={(end) => update({ end })} />,
            <input key="b" aria-label={`Intervalo do dia ${i + 1} em minutos`} inputMode="numeric" value={row.breakMinutes} onChange={(e) => update({ breakMinutes: Number(e.target.value.replace(/\D/g, '') || 0) })} className={inputClass + ' tabular-nums'} />,
          ]}
          summary={<>Total: <b>{formatHours(weekly)} h</b> por semana · <b>{formatHours(weekly * 5)} h</b> por mês</>} />
      </fieldset>
    </SidePanel>
  )
}
