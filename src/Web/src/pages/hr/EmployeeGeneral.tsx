import { useState } from 'react'
import { updateEmployee, useListSchedules, useListUnions } from '@/api/generated/bemaqui'
import type { EmployeeDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { notifySuccess } from '@/components/ui/messages'
import { EMPLOYEE_DICAS } from './employee'
import { EmployeeUser } from './EmployeeUser'

/** Dados gerais (CA 7 e 10): só campos não sensíveis; o cargo muda pela ação "Trocar cargo". */
export function EmployeeGeneral({ employee, canEdit, onSaved, linking, onLinkingChange }: { employee: EmployeeDetail; canEdit: boolean; onSaved: (e: EmployeeDetail) => void; linking?: boolean; onLinkingChange?: (o: boolean) => void }) {
  const schedules = useListSchedules()
  const unions = useListUnions()
  const initial = { name: employee.name, registration: employee.registration ?? '', workScheduleId: employee.workScheduleId ?? '', unionId: employee.unionId ?? '', admissionDate: employee.admissionDate }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const set = (patch: Partial<typeof draft>) => { setError(null); setDraft((d) => ({ ...d, ...patch })) }
  const field = (k: string) => error?.fields[k]?.[0]

  const save = async () => {
    try {
      const saved = await updateEmployee(employee.id, { name: draft.name.trim(), registration: draft.registration.trim() || null, workScheduleId: draft.workScheduleId || null, unionId: draft.unionId || null, admissionDate: draft.admissionDate })
      notifySuccess('Dados gerais salvos')
      onSaved(saved)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar os dados gerais.', fields: {} })
      return false
    }
  }

  return (
    <div className="max-w-3xl space-y-4">
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="space-y-4">
        <UnsavedChangesGuard when={dirty} onSave={save} />
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <fieldset disabled={!canEdit} className="space-y-4">
          <FormSection title="Identificação">
            <Field label="Nome" required error={field('name')}>
              {(p) => <input {...p} value={draft.name} maxLength={150} autoComplete="off" onChange={(e) => set({ name: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Matrícula" error={field('registration')} dica={EMPLOYEE_DICAS.matricula}>
              {(p) => <input {...p} value={draft.registration} maxLength={20} autoComplete="off" onChange={(e) => set({ registration: e.target.value })} className={inputClass + ' tabular-nums'} />}
            </Field>
          </FormSection>
          <FormSection title="Contrato" description={`Cargo: ${employee.positionName}${employee.workCenterName ? ` · Centro: ${employee.workCenterName}` : ''}. Para mudar o cargo, use "Trocar cargo".`}>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Admissão" required error={field('admissionDate')}>{(p) => <DateInput {...p} value={draft.admissionDate} onChange={(admissionDate) => set({ admissionDate })} />}</Field>
              <Field label="Jornada" error={field('workScheduleId')}>
                {(p) => (
                  <select {...p} value={draft.workScheduleId} onChange={(e) => set({ workScheduleId: e.target.value })} className={inputClass}>
                    <option value="">Sem jornada</option>
                    {(schedules.data ?? []).filter((s) => s.isActive || s.id === draft.workScheduleId).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                  </select>
                )}
              </Field>
              <Field label="Sindicato" error={field('unionId')}>
                {(p) => (
                  <select {...p} value={draft.unionId} onChange={(e) => set({ unionId: e.target.value })} className={inputClass}>
                    <option value="">Sem sindicato</option>
                    {(unions.data ?? []).filter((u) => u.isActive || u.id === draft.unionId).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
                  </select>
                )}
              </Field>
            </div>
          </FormSection>
          {canEdit ? <div className="flex justify-end"><button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar dados gerais</button></div> : null}
        </fieldset>
      </form>
      <EmployeeUser employee={employee} linking={linking} onLinkingChange={onLinkingChange} />
    </div>
  )
}
