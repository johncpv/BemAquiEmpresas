import { useState } from 'react'
import { updateReceivable, useListAccounts, useListCostCenters } from '@/api/generated/bemaqui'
import type { ReceivableDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { notifySuccess } from '@/components/ui/messages'
import { type ProjectCost, ProjectCostFields } from '../payables/ProjectCostFields'

/** Aba Dados (CA 19): descrição, conta, centro e valor (valor só em título manual sem baixa). */
export function ReceivableDataForm({ receivable, canEdit, onSaved }: { receivable: ReceivableDetail; canEdit: boolean; onSaved: (r: ReceivableDetail) => void }) {
  const accounts = useListAccounts()
  const centers = useListCostCenters()
  const initial = { description: receivable.description, accountId: receivable.accountId, costCenterId: receivable.costCenterId ?? '', amount: String(receivable.amount) as string | null,
    projectCost: { project: receivable.projectId ? { id: receivable.projectId, label: receivable.projectNumber ?? 'Projeto' } : null, costType: '' } as ProjectCost }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const amountLocked = receivable.origin === 'Contract' || receivable.paidAmount > 0
  const set = (patch: Partial<typeof draft>) => { setError(null); setDraft((d) => ({ ...d, ...patch })) }
  const field = (k: string) => error?.fields[k]?.[0]
  const amountHint = receivable.origin === 'Contract' ? 'Título do contrato: o valor muda pelo contrato ou pelo aditivo.' : receivable.paidAmount > 0 ? 'Com baixa, o valor não muda.' : undefined

  const save = async () => {
    try {
      const saved = await updateReceivable(receivable.id, { description: draft.description.trim(), accountId: draft.accountId, costCenterId: draft.costCenterId || null, projectId: draft.projectCost.project?.id ?? null, amount: Number(draft.amount), version: receivable.version })
      notifySuccess('Título a receber salvo')
      onSaved(saved)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o título.', fields: {} })
      return false
    }
  }

  const revenue = (accounts.data ?? []).filter((a) => a.type === 'Revenue' && a.acceptsEntries && (a.isActive || a.id === draft.accountId))
  const centerOptions = (centers.data ?? []).filter((c) => c.isActive || c.id === draft.costCenterId)
  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
      <UnsavedChangesGuard when={dirty && canEdit} onSave={save} />
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      <fieldset disabled={!canEdit} className="space-y-4">
        <FormSection title="Dados do título">
          <Field label="Descrição" required error={field('description')}>{(p) => <input {...p} value={draft.description} maxLength={200} onChange={(e) => set({ description: e.target.value })} className={inputClass} />}</Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Conta de receita" required error={field('accountId')}>
              {(p) => <select {...p} value={draft.accountId} onChange={(e) => set({ accountId: e.target.value })} className={inputClass}>{revenue.map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}</select>}
            </Field>
            <Field label="Centro de custo" error={field('costCenterId')}>
              {(p) => <select {...p} value={draft.costCenterId} onChange={(e) => set({ costCenterId: e.target.value })} className={inputClass}><option value="">Sem centro de custo</option>{centerOptions.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select>}
            </Field>
            <Field label="Valor" required error={field('amount')} hint={amountHint}>{(p) => <MoneyInput {...p} value={draft.amount} disabled={amountLocked} onChange={(amount) => set({ amount })} />}</Field>
          </div>
          <ProjectCostFields value={draft.projectCost} onChange={(projectCost) => set({ projectCost })} withCostType={false} errors={{ projectId: field('projectId') }} disabled={receivable.origin === 'Contract'} />
        </FormSection>
        {canEdit ? <div className="flex justify-end"><button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar título a receber</button></div> : null}
      </fieldset>
    </form>
  )
}
