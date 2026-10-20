import { useState } from 'react'
import { lookupEmployeeCep, updateEmployeePersonal, useGetEmployeeSensitive } from '@/api/generated/bemaqui'
import type { EmployeeDetail, EmployeeSensitiveDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { CepInput, CpfInput, DateInput, PhoneInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { toastDone } from '@/components/ui/toast'
import { emptyOnly, useCepLookup } from '@/pages/parties/lookups'
import { useCan } from '@/pages/use-can'
import { EMPLOYEE_DICAS } from './employee'
import { EmployeeProtected } from './EmployeeProtected'

/** Dados pessoais (CA 6 e 9): lidos só de /sensitive, com a leitura registrada; gravados pelo comando próprio. */
export function EmployeePersonal({ employee, canEdit }: { employee: EmployeeDetail; canEdit: boolean }) {
  const canSee = useCan('hr.view_sensitive')
  const query = useGetEmployeeSensitive(employee.id, { query: { enabled: canSee, gcTime: 0, staleTime: 0 } })
  if (!canSee) return <EmployeeProtected />
  if (query.isPending) return <Skeleton className="h-96 max-w-3xl" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar os dados pessoais." onRetry={() => query.refetch()} />
  return <PersonalForm key={query.dataUpdatedAt} employee={employee} data={query.data} canEdit={canEdit} />
}

type Draft = Record<'cpf' | 'birthDate' | 'rg' | 'pis' | 'ctpsNumber' | 'ctpsSeries' | 'voterId' | 'zipCode' | 'street' | 'number' | 'complement' | 'district' | 'city' | 'state' | 'phone' | 'email', string>

const fromData = (d: EmployeeSensitiveDetail): Draft => ({
  cpf: d.cpf, birthDate: d.birthDate, rg: d.rg ?? '', pis: d.pis ?? '', ctpsNumber: d.ctpsNumber ?? '', ctpsSeries: d.ctpsSeries ?? '', voterId: d.voterId ?? '',
  zipCode: d.zipCode ?? '', street: d.street ?? '', number: d.number ?? '', complement: d.complement ?? '', district: d.district ?? '', city: d.city ?? '', state: d.state ?? '',
  phone: d.phone ?? '', email: d.email ?? '',
})

function PersonalForm({ employee, data, canEdit }: { employee: EmployeeDetail; data: EmployeeSensitiveDetail; canEdit: boolean }) {
  const [initial, setInitial] = useState(() => fromData(data))
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const set = (patch: Partial<Draft>) => { setError(null); setDraft((d) => ({ ...d, ...patch })) }
  const cep = useCepLookup((a) => setDraft((d) => ({ ...d, ...emptyOnly(d, { street: a.street, district: a.district, city: a.city, state: a.state }) })), lookupEmployeeCep)
  const field = (k: string) => error?.fields[k]?.[0]
  const text = (k: keyof Draft, label: string, extra: { max?: number; dica?: string; className?: string } = {}) => (
    <Field label={label} error={field(k)} dica={extra.dica}>
      {(p) => <input {...p} value={draft[k]} maxLength={extra.max ?? 100} autoComplete="off" onChange={(e) => set({ [k]: e.target.value })} className={inputClass + (extra.className ?? '')} />}
    </Field>
  )

  const save = async () => {
    const nil = (s: string) => s.trim() || null
    try {
      await updateEmployeePersonal(employee.id, {
        cpf: draft.cpf, birthDate: draft.birthDate, rg: nil(draft.rg), pis: nil(draft.pis.replace(/\D/g, '')), ctpsNumber: nil(draft.ctpsNumber), ctpsSeries: nil(draft.ctpsSeries), voterId: nil(draft.voterId),
        zipCode: nil(draft.zipCode), street: nil(draft.street), number: nil(draft.number), complement: nil(draft.complement), district: nil(draft.district), city: nil(draft.city), state: nil(draft.state.toUpperCase()),
        phone: nil(draft.phone), email: nil(draft.email),
      })
      toastDone('Dados pessoais salvos')
      setInitial(draft)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar os dados pessoais.', fields: {} })
      return false
    }
  }

  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
      <UnsavedChangesGuard when={dirty} onSave={save} />
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      <fieldset disabled={!canEdit} className="space-y-4">
        <FormSection title="Documentos" description="Dado pessoal protegido pela LGPD. Cada leitura desta aba fica registrada.">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="CPF" required error={field('cpf')}>{(p) => <CpfInput {...p} value={draft.cpf} onChange={(cpf) => set({ cpf })} />}</Field>
            <Field label="Data de nascimento" required error={field('birthDate')}>{(p) => <DateInput {...p} value={draft.birthDate} onChange={(birthDate) => set({ birthDate })} />}</Field>
            {text('rg', 'RG', { max: 20 })}
            {text('pis', 'PIS', { max: 14, dica: EMPLOYEE_DICAS.pis, className: ' tabular-nums' })}
            {text('ctpsNumber', 'CTPS: número', { max: 20, dica: EMPLOYEE_DICAS.ctps, className: ' tabular-nums' })}
            {text('ctpsSeries', 'CTPS: série', { max: 10, className: ' tabular-nums' })}
            {text('voterId', 'Título de eleitor', { max: 20, className: ' tabular-nums' })}
          </div>
        </FormSection>
        <FormSection title="Endereço">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="CEP" error={field('zipCode')} hint={cep.message} hintLive>
              {(p) => <CepInput {...p} value={draft.zipCode} onChange={(zipCode) => { set({ zipCode }); void cep.onCep(zipCode) }} />}
            </Field>
            {text('street', 'Logradouro', { max: 150 })}
            {text('number', 'Número', { max: 20 })}
            {text('complement', 'Complemento', { max: 100 })}
            {text('district', 'Bairro', { max: 100 })}
            {text('city', 'Cidade', { max: 100 })}
            {text('state', 'UF', { max: 2, className: ' uppercase' })}
          </div>
        </FormSection>
        <FormSection title="Contato pessoal">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Telefone" error={field('phone')}>{(p) => <PhoneInput {...p} value={draft.phone} onChange={(phone) => set({ phone })} />}</Field>
            {text('email', 'E-mail pessoal', { max: 150 })}
          </div>
        </FormSection>
        {canEdit ? <div className="flex justify-end"><button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar dados pessoais</button></div> : null}
      </fieldset>
    </form>
  )
}
