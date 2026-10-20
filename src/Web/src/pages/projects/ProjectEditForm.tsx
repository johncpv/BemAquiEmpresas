import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { listQuoteCustomerAddresses, updateProject, useListUsers } from '@/api/generated/bemaqui'
import type { ProjectDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { notifySuccess } from '@/components/ui/messages'

/** Editar nome, responsável e obra (COM-05 CA 4); data prometida e valor só por aditivo. */
export function ProjectEditForm({ project, onSaved }: { project: ProjectDetail; onSaved: () => Promise<void> }) {
  const usersList = useListUsers()
  const addresses = useQuery({ queryKey: ['project-addresses', project.partyId], queryFn: () => listQuoteCustomerAddresses(project.partyId) })
  const initial = { name: project.name, managerUserId: project.managerUserId ?? '', siteAddressId: project.siteAddressId ?? '' }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const save = async () => {
    try {
      await updateProject(project.id, { name: draft.name.trim(), managerUserId: draft.managerUserId || null, siteAddressId: draft.siteAddressId || null, version: project.version })
      notifySuccess('Projeto salvo')
      await onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o projeto.', fields: {} })
      return false
    }
  }

  const users = (usersList.data ?? []).filter((u) => u.status !== 'Inactive' || u.id === draft.managerUserId)
  const current = users.some((u) => u.id === draft.managerUserId) || !project.managerUserId ? null : { id: project.managerUserId, name: project.managerName ?? 'Responsável atual' }
  const sites = (addresses.data ?? []).filter((a) => a.kind === 'Site')
  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
      <UnsavedChangesGuard when={dirty} onSave={save} />
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      <FormSection title="Dados do projeto">
        <Field label="Nome" required error={error?.fields.name?.[0]}>{(p) => <input {...p} value={draft.name} maxLength={200} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}</Field>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Responsável" error={error?.fields.managerUserId?.[0]}>
            {(p) => (
              <select {...p} value={draft.managerUserId} onChange={(e) => setDraft({ ...draft, managerUserId: e.target.value })} className={inputClass}>
                <option value="">Sem responsável</option>
                {current ? <option value={current.id}>{current.name}</option> : null}
                {users.map((u) => <option key={u.id} value={u.id}>{u.name || u.email}</option>)}
              </select>
            )}
          </Field>
          <Field label="Endereço da obra" error={error?.fields.siteAddressId?.[0]}>
            {(p) => (
              <select {...p} value={draft.siteAddressId} onChange={(e) => setDraft({ ...draft, siteAddressId: e.target.value })} className={inputClass}>
                <option value="">Sem endereço de obra</option>
                {sites.map((a) => <option key={a.id} value={a.id}>{a.street}, {a.number} — {a.city}/{a.state}</option>)}
              </select>
            )}
          </Field>
        </div>
      </FormSection>
      <div className="flex justify-end"><button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar projeto</button></div>
    </form>
  )
}
