import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { createEmployee, getPositionSalary, useListPositions, useListSchedules } from '@/api/generated/bemaqui'
import type { PositionSalaryDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { CpfInput, DateInput, MoneyInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { notifySuccess } from '@/components/ui/messages'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { type EmployeeDraft, employeeDraftErrors, emptyEmployeeDraft, outsideRange, todayIso, withPosition } from './employee'

/** Novo funcionário (CAD-04b CA 1 a 5): página única com o obrigatório, em Identificação e Contrato. */
export function NewEmployeePage() {
  const { cargo } = useSearch({ strict: false }) as { cargo?: string }
  const canSensitive = useCan('hr.view_sensitive')
  const positions = useListPositions({ pageSize: 100 })
  const schedules = useListSchedules()
  if (!canSensitive) return <EmptyState title="Cadastrar funcionário exige ver dados sensíveis." description="CPF e salário são obrigatórios e protegidos. Peça ao administrador a permissão Ver dados sensíveis do RH." />
  if (positions.isPending || schedules.isPending) return <Skeleton className="h-96 max-w-3xl" />
  if (positions.isError || schedules.isError) return <ErrorState onRetry={() => { void positions.refetch(); void schedules.refetch() }} />
  const active = positions.data.items.filter((p) => p.isActive)
  if (active.length === 0) {
    return <EmptyState title="Cadastre um cargo antes do funcionário." description="O cargo sugere a jornada e o salário." action={<Link to="/app/rh/cargos" className="text-sm font-semibold text-primary">Ir para Cargos</Link>} />
  }

  return <NewEmployeeForm initialPositionId={active.some((p) => p.id === cargo) ? cargo : undefined} positions={active} schedules={schedules.data.filter((s) => s.isActive)} />
}

interface FormProps {
  /** ?cargo= vindo do painel do cargo: já escolhido, com as sugestões. */
  initialPositionId?: string
  positions: { id: string; name: string; workScheduleId: string | null }[]
  schedules: { id: string; name: string }[]
}

function NewEmployeeForm({ initialPositionId, positions, schedules }: FormProps) {
  const navigate = useNavigate()
  const [initial] = useState<EmployeeDraft>(() => emptyEmployeeDraft(todayIso()))
  const [draft, setDraft] = useState(initial)
  const [range, setRange] = useState<PositionSalaryDetail | null>(null)
  const [submitted, setSubmitted] = useState(false)
  const [server, setServer] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const local = employeeDraftErrors(draft)
  const errors: Partial<Record<string, string>> = { ...(submitted ? local : {}), ...Object.fromEntries(Object.entries(server?.fields ?? {}).map(([k, v]) => [k, v[0]])) }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const set = (patch: Partial<EmployeeDraft>) => { setServer(null); setDraft((d) => ({ ...d, ...patch })) }

  const choosePosition = async (id: string) => {
    const position = positions.find((p) => p.id === id)
    const salary = position ? await getPositionSalary(position.id).catch(() => null) : null
    setRange(salary)
    setServer(null)
    setDraft((d) => withPosition(d, position ? { id: position.id, workScheduleId: position.workScheduleId, referenceSalary: salary?.referenceSalary ?? null } : undefined))
  }

  useEffect(() => {
    if (initialPositionId) void choosePosition(initialPositionId)
    // Só na montagem: o cargo do link é a primeira escolha.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const save = async () => {
    setSubmitted(true)
    if (Object.keys(local).length) return false
    try {
      const saved = await createEmployee({
        name: draft.name.trim(), cpf: draft.cpf, birthDate: draft.birthDate, admissionDate: draft.admissionDate, positionId: draft.positionId,
        workScheduleId: draft.workScheduleId || null, baseSalary: Number(draft.baseSalary), registration: null,
      })
      notifySuccess('Funcionário salvo')
      await navigate({ to: '/app/rh/funcionarios/$id', params: { id: saved.employee.id }, search: { novo: '1' }, ignoreBlocker: true } as never)
      return true
    } catch (e) {
      setServer(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o funcionário.', fields: {} })
      return false
    }
  }

  return (
    <>
      <PageHeading title="Novo funcionário">
        <p className="mt-2 text-muted-foreground">Só o obrigatório. Os dados pessoais você completa depois, na aba própria.</p>
      </PageHeading>
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
        <UnsavedChangesGuard when={dirty} onSave={save} />
        <FormError message={server && Object.keys(server.fields).length === 0 ? server.message : undefined} />
        <FormSection title="Identificação">
          <Field label="Nome" required error={errors.name}>
            {(p) => <input {...p} value={draft.name} maxLength={150} autoComplete="off" onChange={(e) => set({ name: e.target.value })} className={inputClass} />}
          </Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="CPF" required error={errors.cpf}>{(p) => <CpfInput {...p} value={draft.cpf} onChange={(cpf) => set({ cpf })} />}</Field>
            <Field label="Data de nascimento" required error={errors.birthDate}>{(p) => <DateInput {...p} value={draft.birthDate} onChange={(birthDate) => set({ birthDate })} />}</Field>
          </div>
        </FormSection>
        <FormSection title="Contrato">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Admissão" required error={errors.admissionDate}>{(p) => <DateInput {...p} value={draft.admissionDate} onChange={(admissionDate) => set({ admissionDate })} />}</Field>
            <Field label="Cargo" required error={errors.positionId} hint="Sugere a jornada e o salário de referência.">
              {(p) => (
                <select {...p} value={draft.positionId} onChange={(e) => void choosePosition(e.target.value)} className={inputClass}>
                  <option value="">Escolha</option>
                  {positions.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
                </select>
              )}
            </Field>
            <Field label="Jornada" hint={draft.touched.schedule ? undefined : 'A do cargo, enquanto você não trocar.'}>
              {(p) => (
                <select {...p} value={draft.workScheduleId} onChange={(e) => set({ workScheduleId: e.target.value, touched: { ...draft.touched, schedule: true } })} className={inputClass}>
                  <option value="">Sem jornada</option>
                  {schedules.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </select>
              )}
            </Field>
            <Field label="Salário base" required error={errors.baseSalary} warning={outsideRange(draft.baseSalary, range)} hint={draft.touched.salary ? undefined : 'O de referência do cargo, enquanto você não trocar.'}>
              {(p) => <MoneyInput {...p} value={draft.baseSalary} onChange={(baseSalary) => set({ baseSalary, touched: { ...draft.touched, salary: true } })} />}
            </Field>
          </div>
        </FormSection>
        <div className="flex justify-end gap-2">
          <Link to="/app/rh/funcionarios" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Link>
          <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar funcionário</button>
        </div>
      </form>
    </>
  )
}
