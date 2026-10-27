import { useState } from 'react'
import { addEmployeeDependent, deleteEmployeeDependent, updateEmployeeDependent, useListEmployeeDependentsSensitive } from '@/api/generated/bemaqui'
import type { EmployeeDependentDetail, EmployeeDetail } from '@/api/generated/model'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { CpfInput, DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { todayIso } from './employee'
import { COMPLEMENT_DICAS, type DependentDraft, dependentErrors, emptyDependentDraft, KINSHIP_LABELS } from './employee-complement'
import { EmployeeProtected } from './EmployeeProtected'

const button = 'min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/** Aba Dependentes (CAD-04c2 CA 1 a 3): só por /dependents/sensitive, em cartões; CPF mascarado e só no dependente de IR. */
export function EmployeeDependents({ employee, canEdit, onFirstSaved }: { employee: EmployeeDetail; canEdit: boolean; onFirstSaved: () => void }) {
  const canSee = useCan('hr.view_sensitive')
  const query = useListEmployeeDependentsSensitive(employee.id, { query: { enabled: canSee, gcTime: 0, staleTime: 0 } })
  const [editing, setEditing] = useState<EmployeeDependentDetail | 'new' | null>(null)
  if (!canSee) return <EmployeeProtected />
  if (query.isPending) return <div className="max-w-3xl space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-20" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar os dependentes." onRetry={() => query.refetch()} />
  const list = query.data
  const add = canEdit ? <button type="button" onClick={() => setEditing('new')} className={primary}>Novo dependente</button> : undefined
  const saved = async () => { const first = list.length === 0; await query.refetch(); if (first) onFirstSaved() }

  return (
    <div className="max-w-3xl space-y-3">
      {editing ? <DependentDialog employee={employee} dependent={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} onSaved={saved} /> : null}
      {list.length === 0 ? (
        <EmptyState title="Nenhum dependente cadastrado." description="Cadastre os filhos e demais dependentes para o IR e o salário-família." action={add} />
      ) : (
        <>
          {add ? <div className="flex justify-end">{add}</div> : null}
          <ul aria-label="Dependentes" className="grid gap-2 sm:grid-cols-2">
            {list.map((d) => (
              <li key={d.id} className="space-y-2 rounded-md border border-border bg-card p-3 text-sm">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <p className="font-semibold">{d.name}</p>
                    <p className="text-muted-foreground">{KINSHIP_LABELS[d.kinship]} · nascido em <span className="tabular-nums">{formatDate(d.birthDate)}</span></p>
                  </div>
                  <div className="flex flex-wrap gap-1">
                    {d.irDependent ? <Tag>Dependente de IR</Tag> : null}
                    {d.familyAllowance ? <Tag>Salário-família</Tag> : null}
                  </div>
                </div>
                {d.cpfMasked ? <p className="tabular-nums text-muted-foreground">CPF {d.cpfMasked}</p> : null}
                {canEdit ? (
                  <div className="flex flex-wrap gap-2">
                    <button type="button" onClick={() => setEditing(d)} className={button}>Editar</button>
                    <ConfirmDialog verb="Excluir" done="Dependente excluído" destructive title={`Excluir ${d.name}?`} description="O dependente sai do IR e do salário-família. O histórico de alterações continua registrado."
                      onConfirm={async () => { await deleteEmployeeDependent(employee.id, d.id); await query.refetch() }}
                      trigger={<button type="button" className={button + ' text-crit-ink'}>Excluir</button>} />
                  </div>
                ) : null}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}

/** Novo ou editar dependente (CA 1 e 2): o CPF só aparece com "Dependente de IR"; na edição, vazio mantém o gravado. */
function DependentDialog({ employee, dependent, onClose, onSaved }: { employee: EmployeeDetail; dependent?: EmployeeDependentDetail; onClose: () => void; onSaved: () => Promise<void> }) {
  const [draft, setDraft] = useState<DependentDraft>(() => dependent
    ? { name: dependent.name, birthDate: dependent.birthDate, kinship: dependent.kinship, irDependent: dependent.irDependent, familyAllowance: dependent.familyAllowance, cpf: '' }
    : emptyDependentDraft())
  const [errors, setErrors] = useState<Record<string, string>>({})
  const set = (patch: Partial<DependentDraft>) => { setErrors({}); setDraft((d) => ({ ...d, ...patch })) }
  const keepsCpf = !!dependent?.cpfMasked && draft.irDependent
  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb="Salvar dependente" done="Dependente salvo"
      title={dependent ? `Editar ${dependent.name}` : `Novo dependente de ${employee.name}`} description="Dado pessoal protegido pela LGPD; pode ser de menor."
      onFieldErrors={(e) => { setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))); return true }}
      onConfirm={async () => {
        const found = dependentErrors(draft, todayIso(), keepsCpf)
        if (Object.keys(found).length > 0) { setErrors(found); throw new DialogMessage('Confira os campos destacados.') }
        const body = { name: draft.name.trim(), birthDate: draft.birthDate, kinship: draft.kinship || null, irDependent: draft.irDependent, familyAllowance: draft.familyAllowance, cpf: draft.irDependent && draft.cpf ? draft.cpf : null }
        if (dependent) await updateEmployeeDependent(employee.id, dependent.id, { ...body, version: dependent.version })
        else await addEmployeeDependent(employee.id, body)
        await onSaved()
      }}>
      <Field label="Nome" required error={errors.name}>{(p) => <input {...p} value={draft.name} maxLength={150} autoComplete="off" onChange={(e) => set({ name: e.target.value })} className={inputClass} />}</Field>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Data de nascimento" required error={errors.birthDate}>{(p) => <DateInput {...p} value={draft.birthDate} onChange={(birthDate) => set({ birthDate })} />}</Field>
        <Field label="Parentesco" required error={errors.kinship}>
          {(p) => (
            <select {...p} value={draft.kinship} onChange={(e) => set({ kinship: e.target.value as DependentDraft['kinship'] })} className={inputClass}>
              <option value="">Escolha</option>
              {Object.entries(KINSHIP_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          )}
        </Field>
      </div>
      <Check label="Dependente de IR" dica={COMPLEMENT_DICAS.ir} checked={draft.irDependent} onChange={(irDependent) => set({ irDependent, cpf: irDependent ? draft.cpf : '' })} />
      {draft.irDependent ? (
        <Field label="CPF do dependente" required={!keepsCpf} error={errors.cpf} hint={keepsCpf ? `Gravado: ${dependent?.cpfMasked}. Deixe em branco para manter.` : undefined}>
          {(p) => <CpfInput {...p} value={draft.cpf} onChange={(cpf) => set({ cpf })} />}
        </Field>
      ) : null}
      <Check label="Salário-família" dica={COMPLEMENT_DICAS.salarioFamilia} checked={draft.familyAllowance} onChange={(familyAllowance) => set({ familyAllowance })} />
    </ConfirmDialog>
  )
}

function Check({ label, dica, checked, onChange }: { label: string; dica: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="flex items-center gap-1 text-sm">
      <label className="flex min-h-11 items-center gap-2">
        <input type="checkbox" className="size-4" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        {label}
      </label>
      <Dica nome={label} texto={dica} />
    </div>
  )
}

