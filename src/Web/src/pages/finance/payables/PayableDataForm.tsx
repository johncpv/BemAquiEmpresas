import { useState } from 'react'
import { updatePayable, useListAccounts, useListCostCenters } from '@/api/generated/bemaqui'
import type { PayableDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { notifySuccess } from '@/components/ui/messages'
import { toNumber } from '../receivables/receivables'
import { type RetentionDraft, suggestCostType } from './payables'
import { type ProjectCost, ProjectCostFields } from './ProjectCostFields'
import { RetentionFields } from './RetentionFields'

const str = (n: number) => (n ? String(n) : null)

/** Aba Dados (FIN-02 CA 4, 11 e 13): em OP, só descrição e vencimento; título de outra origem não troca o valor. */
export function PayableDataForm({ payable, canEdit, onSaved }: { payable: PayableDetail; canEdit: boolean; onSaved: (p: PayableDetail) => void }) {
  const accounts = useListAccounts()
  const centers = useListCostCenters()
  const inOrder = payable.status === 'AwaitingApproval' || payable.status === 'Approved'
  const lockedAmount = inOrder || !['Manual', 'Recurring', 'Opening'].includes(payable.origin)
  const initial = {
    description: payable.description, accountId: payable.accountId, costCenterId: payable.costCenterId ?? '', amount: String(payable.amount) as string | null, dueDate: payable.dueDate ?? '',
    retentions: { iss: str(payable.retentionIss), inss: str(payable.retentionInss), ir: str(payable.retentionIr), pcc: str(payable.retentionPcc) } as RetentionDraft,
    projectCost: { project: payable.projectId ? { id: payable.projectId, label: payable.projectNumber ?? 'Projeto' } : null, costType: payable.costType ?? '' } as ProjectCost,
  }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const set = (patch: Partial<typeof draft>) => { setError(null); setDraft((d) => ({ ...d, ...patch })) }
  const errors = Object.fromEntries(Object.entries(error?.fields ?? {}).map(([k, v]) => [k, v[0]]))

  const save = async () => {
    const r = draft.retentions
    try {
      const saved = await updatePayable(payable.id, {
        description: draft.description.trim(), accountId: draft.accountId, costCenterId: draft.costCenterId || null, amount: toNumber(draft.amount), dueDate: draft.dueDate,
        retentionIss: toNumber(r.iss), retentionInss: toNumber(r.inss), retentionIr: toNumber(r.ir), retentionPcc: toNumber(r.pcc), version: payable.version,
        projectId: draft.projectCost.project?.id ?? null, costType: draft.projectCost.project ? draft.projectCost.costType || null : null,
      })
      notifySuccess('Título a pagar salvo')
      onSaved(saved)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o título.', fields: {} })
      return false
    }
  }

  const suggested = suggestCostType((accounts.data ?? []).find((a) => a.id === draft.accountId)?.code)
  const options = (accounts.data ?? []).filter((a) => a.type !== 'Revenue' && a.acceptsEntries && (a.isActive || a.id === draft.accountId))
  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
      <UnsavedChangesGuard when={dirty && canEdit} onSave={save} />
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      <fieldset disabled={!canEdit} className="space-y-4">
        <FormSection title="Dados do título">
          <Field label="Descrição" required error={errors.description}>{(p) => <input {...p} value={draft.description} maxLength={200} onChange={(e) => set({ description: e.target.value })} className={inputClass} />}</Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Conta" required error={errors.accountId}>
              {(p) => <select {...p} value={draft.accountId} disabled={inOrder} onChange={(e) => { const code = (accounts.data ?? []).find((a) => a.id === e.target.value)?.code; set({ accountId: e.target.value, projectCost: draft.projectCost.project && !draft.projectCost.costType ? { ...draft.projectCost, costType: suggestCostType(code) ?? '' } : draft.projectCost }) }} className={inputClass}>{options.map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}</select>}
            </Field>
            <Field label="Centro de custo" error={errors.costCenterId}>
              {(p) => <select {...p} value={draft.costCenterId} disabled={inOrder} onChange={(e) => set({ costCenterId: e.target.value })} className={inputClass}><option value="">Sem centro de custo</option>{(centers.data ?? []).filter((c) => c.isActive || c.id === draft.costCenterId).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select>}
            </Field>
            <Field label="Valor" required error={errors.amount} hint={inOrder ? 'Em ordem de pagamento: o valor não muda.' : lockedAmount ? 'Gerado por outro módulo: ajuste na origem.' : undefined}>
              {(p) => <MoneyInput {...p} value={draft.amount} disabled={lockedAmount} onChange={(amount) => set({ amount })} />}
            </Field>
            <Field label="Vencimento" required error={errors.dueDate}>{(p) => <DateInput {...p} value={draft.dueDate} onChange={(dueDate) => set({ dueDate })} />}</Field>
          </div>
        </FormSection>
        <FormSection title="Projeto">
          <ProjectCostFields value={draft.projectCost} onChange={(projectCost) => set({ projectCost })} suggested={suggested} errors={errors} />
        </FormSection>
        <FormSection title="Retenções">
          <RetentionFields value={draft.retentions} onChange={(retentions) => set({ retentions })} amount={toNumber(draft.amount)} errors={errors} disabled={inOrder} />
        </FormSection>
        {canEdit ? <div className="flex justify-end"><button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar título a pagar</button></div> : null}
      </fieldset>
    </form>
  )
}
