import { useState } from 'react'
import { createCalendarException, updateCalendarException } from '@/api/generated/bemaqui'
import { type CalendarExceptionDetail, CalendarExceptionType } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ToggleGroup } from '@/components/form/choice'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, DecimalInput } from '@/components/form/inputs'
import { SidePanel } from '@/components/ui/side-panel'
import { notifySuccess } from '@/components/ui/messages'
import { HOURS_DICA } from './calendar'

type Draft = { date: string; type: CalendarExceptionType; hours: string | null; description: string }
const toDraft = (e: CalendarExceptionDetail | null): Draft => ({ date: e?.date ?? '', type: e?.type ?? CalendarExceptionType.Holiday, hours: String(e?.hoursAvailable ?? 0), description: e?.description ?? '' })

/** Feriado ou parada (CAD-03 CA 9): feriado vem com 0 h; parada parcial informa as horas que sobram no dia. */
export function ExceptionPanel({ calendarId, exception, canEdit, onClose, onSaved }: { calendarId: string; exception: CalendarExceptionDetail | null; canEdit: boolean; onClose: () => void; onSaved: () => void }) {
  const [draft, setDraft] = useState<Draft>(() => toDraft(exception))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(toDraft(exception))
  const field = (name: string) => error?.fields[name]?.[0]

  const save = async () => {
    const input = { date: draft.date, type: draft.type, hoursAvailable: Number(draft.hours ?? 0), description: draft.description.trim() }
    try {
      if (exception) await updateCalendarException(calendarId, exception.id, input)
      else await createCalendarException(calendarId, input)
      notifySuccess('Exceção salva')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a exceção.', fields: {} })
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={exception ? 'Exceção do calendário' : 'Nova exceção'} dirty={dirty && canEdit} onSave={save}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar exceção</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canEdit} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Data" required error={field('date')}>
          {(p) => <DateInput {...p} value={draft.date} onChange={(date) => setDraft({ ...draft, date })} />}
        </Field>
        <div className="space-y-1">
          <p className="text-sm font-medium">Tipo</p>
          <ToggleGroup label="Tipo" value={draft.type} disabled={!canEdit}
            onChange={(type) => setDraft({ ...draft, type, hours: type === CalendarExceptionType.Holiday ? '0' : draft.hours })}
            options={[{ value: CalendarExceptionType.Holiday, label: 'Feriado' }, { value: CalendarExceptionType.Stoppage, label: 'Parada' }]} />
        </div>
        <Field label="Horas disponíveis" required error={field('hoursAvailable')} dica={HOURS_DICA}>
          {(p) => <DecimalInput {...p} scale={2} suffix="h" value={draft.hours} onChange={(hours) => setDraft({ ...draft, hours })} />}
        </Field>
        <Field label="Descrição" required error={field('description')} hint="Ex.: Aniversário da cidade, Inventário anual.">
          {(p) => <input {...p} value={draft.description} maxLength={100} onChange={(e) => setDraft({ ...draft, description: e.target.value })} className={inputClass} />}
        </Field>
      </fieldset>
    </SidePanel>
  )
}
