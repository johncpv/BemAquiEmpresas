import { useState } from 'react'
import { getEmployeeScheduleDay } from '@/api/generated/bemaqui'
import type { AbsenceReason, AbsenceReasonOption, TimeAdjustmentDayEntry, TimeAdjustmentInput, TimeEntryType } from '@/api/generated/model'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { Dica } from '@/help/Dica'
import { absenceReasons, ADJUSTMENT_DICAS, hoursToMinutes, JUSTIFICATION_MAX, justificationError, lastWorkday, NO_CID } from './time-adjustments'
import { ENTRY_TYPES, entryLabel, formatTime, todayInSaoPaulo } from './time-clock'

export type AdjustmentMode = 'add' | 'absence' | 'void'

/** Marcação a desconsiderar (CA 5): o funcionário só escolhe uma das dele, na lista "Minhas marcações". */
export interface VoidTarget {
  id: string
  type: TimeEntryType
  at: string
}

interface AdjustmentDialogProps {
  mode: AdjustmentMode
  open: boolean
  onOpenChange: (open: boolean) => void
  /** "Lançar marcação" (RH) ou "Pedir ajuste" (funcionário). */
  verb: string
  done: string
  title: string
  description?: string
  /** Lista única de ausências (CA 7), já filtrada pelo uso; os de saúde só para quem pode (ou para o próprio funcionário). */
  reasons?: readonly AbsenceReasonOption[]
  canHealth: boolean
  target?: VoidTarget
  /** O documento do abono: o funcionário sempre; o RH só com hr.view_health (CA 8). */
  allowDocument?: boolean
  /** Envia o pedido e, no abono, o documento opcional. */
  submit: (body: TimeAdjustmentInput, file: File | null) => Promise<unknown>
  onFieldErrors?: Parameters<typeof ConfirmDialog>[0]['onFieldErrors']
}

/**
 * Diálogo do pedido de ajuste (RH-01b CA 2, 5 e 7): incluir marcação (data, hora e tipo), desconsiderar uma marcação ou abonar uma
 * ausência (dia, o dia inteiro ou as horas, motivo da lista única e documento opcional), sempre com a justificativa de 10 a 200 caracteres
 * sem CID. Tela cheia a 400 px; o erro do servidor fica no diálogo.
 */
export function AdjustmentDialog({ mode, open, onOpenChange, verb, done, title, description, reasons, canHealth, target, allowDocument = true, submit, onFieldErrors }: AdjustmentDialogProps) {
  const today = todayInSaoPaulo()
  const [day, setDay] = useState(today)
  const [time, setTime] = useState('')
  const [type, setType] = useState<TimeEntryType>('In')
  const [fullDay, setFullDay] = useState(true)
  const [hours, setHours] = useState('')
  const [reason, setReason] = useState<AbsenceReason | ''>('')
  const [justification, setJustification] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const reset = () => { setDay(today); setTime(''); setType('In'); setFullDay(true); setHours(''); setReason(''); setJustification(''); setFile(null) }
  const options = absenceReasons(reasons, canHealth)
  const health = options.find((o) => o.code === reason)?.isHealth ?? false

  return (
    <ConfirmDialog verb={verb} done={done} title={title} description={description} size="lg" open={open} onOpenChange={onOpenChange} onOpen={reset} onFieldErrors={onFieldErrors}
      onConfirm={async () => {
        const invalid = justificationError(justification)
        if (invalid) throw new DialogMessage(invalid)
        if (mode === 'add') {
          if (!day || !time) throw new DialogMessage('Informe a data e a hora da marcação.')
          await submit({ kind: 'AddEntry', day, time, entryType: type, justification: justification.trim() }, null)
        } else if (mode === 'void') {
          if (!target) throw new DialogMessage('Escolha a marcação a desconsiderar.')
          await submit({ kind: 'VoidEntry', entryId: target.id, justification: justification.trim() }, null)
        } else {
          if (!day) throw new DialogMessage('Informe o dia da ausência.')
          if (!reason) throw new DialogMessage('Escolha o motivo da ausência.')
          const minutes = fullDay ? null : hoursToMinutes(hours)
          if (!fullDay && minutes == null) throw new DialogMessage('Informe as horas abonadas no formato 04:00 (até 24 h).')
          await submit({ kind: 'Absence', day, absenceReason: reason, absenceMinutes: minutes, justification: justification.trim() }, file)
        }
      }}>
      {mode === 'void' && target ? (
        <p className="flex items-center rounded-md border border-border bg-muted px-3 py-2 text-sm">
          Desconsiderar {entryLabel(target.type).toLowerCase()} às <span className="ml-1 tabular-nums">{formatTime(target.at)}</span>
          <Dica nome="Desconsiderar" texto={ADJUSTMENT_DICAS.desconsiderar} />
        </p>
      ) : null}
      {mode !== 'void' ? (
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label={mode === 'add' ? 'Data' : 'Dia da ausência'} required>
            {(p) => <input {...p} type="date" max={today} value={day} onChange={(e) => setDay(e.target.value)} className={inputClass} />}
          </Field>
          {mode === 'add' ? (
            <Field label="Hora" required hint="Hora local (Brasília).">
              {(p) => <input {...p} type="time" value={time} onChange={(e) => setTime(e.target.value)} className={inputClass} />}
            </Field>
          ) : null}
        </div>
      ) : null}
      {mode === 'add' ? (
        <Field label="Tipo" required>
          {(p) => (
            <select {...p} value={type} onChange={(e) => setType(e.target.value as TimeEntryType)} className={inputClass}>
              {ENTRY_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          )}
        </Field>
      ) : null}
      {mode === 'absence' ? (
        <>
          <Field label="Motivo" required dica={ADJUSTMENT_DICAS.abono}>
            {(p) => (
              <select {...p} value={reason} onChange={(e) => setReason(e.target.value as AbsenceReason | '')} className={inputClass}>
                <option value="">Escolha</option>
                {options.map((o) => <option key={o.code} value={o.code}>{o.label}</option>)}
              </select>
            )}
          </Field>
          {health ? (
            <p className="flex items-center text-sm text-muted-foreground">Motivo de saúde: só quem tem a permissão Ver dados de saúde vê o motivo e o documento.<Dica nome="Motivo de saúde" texto={ADJUSTMENT_DICAS.saude} /></p>
          ) : null}
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">Quanto abonar</legend>
            <label className="flex min-h-11 items-center gap-2 text-sm"><input type="radio" name="abono" checked={fullDay} onChange={() => setFullDay(true)} className="size-5" />O dia inteiro</label>
            <label className="flex min-h-11 items-center gap-2 text-sm"><input type="radio" name="abono" checked={!fullDay} onChange={() => setFullDay(false)} className="size-5" />Algumas horas</label>
            {!fullDay ? (
              <Field label="Horas abonadas" required hint="Ex.: 04:00 para quatro horas.">
                {(p) => <input {...p} inputMode="numeric" placeholder="04:00" value={hours} onChange={(e) => setHours(e.target.value)} className={inputClass} />}
              </Field>
            ) : null}
          </fieldset>
          {allowDocument ? <Field label="Documento (opcional)" hint="PDF, PNG ou JPG até 10 MB, como o atestado ou a declaração.">
            {(p) => <input {...p} type="file" accept="application/pdf,image/png,image/jpeg" onChange={(e) => setFile(e.target.files?.[0] ?? null)} className="min-h-11 text-sm" />}
          </Field> : null}
        </>
      ) : null}
      <Field label="Justificativa" required hint={`${justification.trim().length}/${JUSTIFICATION_MAX}. ${NO_CID}`} hintLive>
        {(p) => <textarea {...p} rows={3} maxLength={JUSTIFICATION_MAX} value={justification} onChange={(e) => setJustification(e.target.value)} className={inputClass} />}
      </Field>
    </ConfirmDialog>
  )
}

/**
 * "Lançar o dia pela jornada" (CA 3, R-PRP-1): o diálogo vem com as marcações da jornada do funcionário no dia escolhido, editáveis, e
 * uma justificativa única; confirmar cria um pedido por marcação.
 */
export function LaunchDayDialog({ employeeId, open, onOpenChange, submit }: {
  employeeId: string
  open: boolean
  onOpenChange: (open: boolean) => void
  submit: (day: string, entries: TimeAdjustmentDayEntry[], justification: string) => Promise<unknown>
}) {
  const today = todayInSaoPaulo()
  const [day, setDay] = useState(() => lastWorkday(today))
  const [entries, setEntries] = useState<TimeAdjustmentDayEntry[]>([])
  const [schedule, setSchedule] = useState<string | null>(null)
  const [existing, setExisting] = useState(0)
  const [justification, setJustification] = useState('')
  const load = async (value: string) => {
    setDay(value)
    if (!value) return
    try {
      const proposal = await getEmployeeScheduleDay(employeeId, { day: value })
      setEntries(proposal.entries.map((e) => ({ ...e })))
      setSchedule(proposal.schedule)
      setExisting(proposal.existingEntries)
    } catch {
      setEntries([])
      setSchedule(null)
    }
  }

  const change = (index: number, patch: Partial<TimeAdjustmentDayEntry>) => setEntries((list) => list.map((e, i) => (i === index ? { ...e, ...patch } : e)))
  return (
    <ConfirmDialog verb="Lançar marcação" done="Marcação enviada para aprovação" title="Lançar o dia pela jornada" size="lg"
      description="As marcações vêm da jornada do funcionário no dia. Ajuste o que for diferente; cada marcação vira um pedido para aprovação."
      open={open} onOpenChange={onOpenChange} onOpen={() => { setJustification(''); void load(day || lastWorkday(today)) }}
      onConfirm={async () => {
        const invalid = justificationError(justification)
        if (invalid) throw new DialogMessage(invalid)
        if (entries.length === 0 || entries.some((e) => !e.time)) throw new DialogMessage('Informe a hora de cada marcação do dia.')
        await submit(day, entries, justification.trim())
      }}>
      <Field label="Dia" required hint={schedule ? `Jornada: ${schedule}` : undefined}>
        {(p) => <input {...p} type="date" max={today} value={day} onChange={(e) => void load(e.target.value)} className={inputClass} />}
      </Field>
      {existing > 0 ? <p role="status" className="text-sm text-warn-ink">O dia já tem {existing === 1 ? '1 marcação' : `${existing} marcações`}. Lance só o que falta.</p> : null}
      {entries.length === 0 ? (
        <p className="text-sm text-muted-foreground">Sem jornada neste dia. Use "Lançar marcação" para incluir uma por vez.</p>
      ) : (
        <ul aria-label="Marcações do dia" className="space-y-2">
          {entries.map((e, i) => (
            <li key={`${e.type}-${i}`} className="flex flex-wrap items-end gap-2">
              <span className="w-44 text-sm font-medium">{entryLabel(e.type)}</span>
              <label className="flex flex-col text-xs font-semibold">Hora
                <input type="time" aria-label={`Hora: ${entryLabel(e.type)}`} value={e.time} onChange={(ev) => change(i, { time: ev.target.value })} className={inputClass} />
              </label>
              <button type="button" onClick={() => setEntries((list) => list.filter((_, j) => j !== i))} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Tirar</button>
            </li>
          ))}
        </ul>
      )}
      <Field label="Justificativa" required hint={`${justification.trim().length}/${JUSTIFICATION_MAX}. ${NO_CID}`} hintLive>
        {(p) => <textarea {...p} rows={3} maxLength={JUSTIFICATION_MAX} value={justification} onChange={(ev) => setJustification(ev.target.value)} className={inputClass} />}
      </Field>
    </ConfirmDialog>
  )
}
