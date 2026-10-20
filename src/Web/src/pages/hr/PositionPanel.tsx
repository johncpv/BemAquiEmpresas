import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { createPosition, updatePosition, useListPositionWorkCenters, useListSchedules } from '@/api/generated/bemaqui'
import type { PositionDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { CboInput, DecimalInput } from '@/components/form/inputs'
import { RecordHistory } from '@/components/history/RecordHistory'
import { SidePanel } from '@/components/ui/side-panel'
import { notifySuccess } from '@/components/ui/messages'
import { useCan } from '@/pages/use-can'
import { DICAS } from './hr'
import { PositionActions } from './PositionActions'
import { PositionCost } from './PositionCost'
import { PositionSalary } from './PositionSalary'

type Draft = { name: string; cbo: string; centerId: string; scheduleId: string; monthlyHours: string | null }

const toDraft = (p: PositionDetail | null): Draft => ({ name: p?.name ?? '', cbo: p?.cbo ?? '', centerId: p?.defaultWorkCenterId ?? '', scheduleId: p?.workScheduleId ?? '', monthlyHours: p ? String(p.monthlyHours) : null })

/** Painel do cargo (CAD-04a CA 1 a 9): dados gerais, custo-hora (calculado ou ajustado), a faixa salarial protegida e o atalho para cadastrar funcionário (CAD-04b). */
export function PositionPanel({ position, chargesPct, onClose, onSaved }: { position: PositionDetail | null; chargesPct: number; onClose: () => void; onSaved: () => void }) {
  const schedules = useListSchedules()
  const centers = useListPositionWorkCenters()
  const [current, setCurrent] = useState(position)
  const [draft, setDraft] = useState(() => toDraft(position))
  const [initial, setInitial] = useState(() => toDraft(position))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan(position ? 'hr.edit' : 'hr.create')
  const canHistory = useCan('hr.view_sensitive')
  const canHire = useCan('hr.create') && canHistory
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const field = (name: string) => error?.fields[name]?.[0]

  const keep = (saved: PositionDetail) => {
    setCurrent(saved)
    setDraft(toDraft(saved))
    setInitial(toDraft(saved))
    onSaved()
  }

  const save = async () => {
    const input = { name: draft.name.trim(), cbo: draft.cbo || null, defaultWorkCenterId: draft.centerId || null, workScheduleId: draft.scheduleId || null, monthlyHours: draft.monthlyHours ? Number(draft.monthlyHours) : null }
    try {
      const saved = current ? await updatePosition(current.id, input) : await createPosition(input)
      notifySuccess('Cargo salvo')
      keep(saved)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o cargo.', fields: {} })
      return false
    }
  }

  const pickSchedule = (scheduleId: string) => {
    const weekly = schedules.data?.find((s) => s.id === scheduleId)?.weeklyHours
    setDraft({ ...draft, scheduleId, monthlyHours: weekly ? String(weekly * 5) : draft.monthlyHours })
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={current ? `Cargo ${current.name}` : 'Novo cargo'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          {current ? <PositionActions position={current} onDone={() => { onSaved(); onClose() }} /> : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar cargo</button> : null}
        </>
      )}
    >
      <div className="space-y-4">
        <fieldset disabled={!canWrite} className="space-y-3">
          <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
          <Field label="Nome" required error={field('name')}>{(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}</Field>
          <Field label="CBO" error={field('cbo')} dica={DICAS.cbo}>{(p) => <CboInput {...p} value={draft.cbo} onChange={(cbo) => setDraft({ ...draft, cbo })} />}</Field>
          <Field label="Centro de trabalho padrão" error={field('defaultWorkCenterId')}>
            {(p) => (
              <select {...p} value={draft.centerId} onChange={(e) => setDraft({ ...draft, centerId: e.target.value })} className={inputClass}>
                <option value="">Nenhum</option>
                {(centers.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            )}
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Jornada" error={field('workScheduleId')}>
              {(p) => (
                <select {...p} value={draft.scheduleId} onChange={(e) => pickSchedule(e.target.value)} className={inputClass}>
                  <option value="">Nenhuma</option>
                  {(schedules.data ?? []).filter((s) => s.isActive || s.id === draft.scheduleId).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </select>
              )}
            </Field>
            <Field label="Horas mensais" error={field('monthlyHours')} dica={DICAS.horas}>{(p) => <DecimalInput {...p} scale={2} suffix="h" value={draft.monthlyHours} onChange={(monthlyHours) => setDraft({ ...draft, monthlyHours })} />}</Field>
          </div>
        </fieldset>
        {current && canHire && current.isActive ? (
          <p className="text-sm">
            {current.activeEmployees === 1 ? '1 funcionário ativo neste cargo.' : `${current.activeEmployees} funcionários ativos neste cargo.`}{' '}
            <Link to="/app/rh/funcionarios/novo" search={{ cargo: current.id } as never} className="font-semibold text-primary">Cadastrar funcionário neste cargo</Link>
          </p>
        ) : null}
        {current ? <PositionCost position={current} onSaved={keep} /> : null}
        <PositionSalary position={current} chargesPct={chargesPct} onSaved={keep} />
        {current && canHistory ? (
          <details className="rounded-md border border-border bg-card px-3 py-2 text-sm">
            <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
            <div className="mt-3"><RecordHistory entity="JobPosition" id={current.id} /></div>
          </details>
        ) : null}
      </div>
    </SidePanel>
  )
}
