import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { createReceivable, createReceivableInstallments, listFinanceCustomers, previewReceivableSplit, useListAccounts, useListCostCenters, useListPaymentTerms } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { ToggleGroup } from '@/components/form/choice'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { type ProjectCost, ProjectCostFields } from '../payables/ProjectCostFields'
import { today } from './receivables'

type Customer = ComboOption & { termId: string | null }

const searchCustomers = async (term: string): Promise<Customer[]> =>
  (await listFinanceCustomers({ q: term })).map((c) => ({ id: c.id, label: c.name, hint: c.document ?? undefined, termId: c.defaultPaymentTermId }))

interface Draft {
  customer: Customer | null
  description: string
  accountId: string
  costCenterId: string
  amount: string | null
  issueDate: string
  dueDate: string
  mode: 'single' | 'split'
  termId: string
  baseDate: string
  projectCost: ProjectCost
}

/** Novo título a receber (FIN-01 CA 1 a 5): página em seções; parcelar pela condição do cliente, com a prévia do servidor. */
export function NewReceivablePage() {
  const accounts = useListAccounts()
  const centers = useListCostCenters()
  const terms = useListPaymentTerms({ pageSize: 100 })
  if (accounts.isPending || centers.isPending || terms.isPending) return <Skeleton className="h-96 max-w-3xl" />
  if (accounts.isError || centers.isError || terms.isError) return <ErrorState onRetry={() => { void accounts.refetch(); void centers.refetch(); void terms.refetch() }} />
  const revenue = accounts.data.filter((a) => a.type === 'Revenue' && a.acceptsEntries && a.isActive)
  return (
    <ReceivableForm
      accounts={revenue.map((a) => ({ id: a.id, label: `${a.code} ${a.name}`, code: a.code }))}
      centers={centers.data.filter((c) => c.isActive).map((c) => ({ id: c.id, label: c.name }))}
      terms={terms.data.items.filter((t) => t.isActive).map((t) => ({ id: t.id, label: t.name }))}
    />
  )
}

interface FormProps {
  accounts: { id: string; label: string; code: string }[]
  centers: { id: string; label: string }[]
  terms: { id: string; label: string }[]
}

function ReceivableForm({ accounts, centers, terms }: FormProps) {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const [initial] = useState<Draft>(() => ({
    customer: null, description: '', accountId: accounts.find((a) => a.code === '1.1')?.id ?? accounts[0]?.id ?? '', costCenterId: '', amount: null,
    issueDate: today(), dueDate: '', mode: 'single', termId: '', baseDate: today(), projectCost: { project: null, costType: '' },
  }))
  const [draft, setDraft] = useState(initial)
  const [submitted, setSubmitted] = useState(false)
  const [server, setServer] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const set = (patch: Partial<Draft>) => { setServer(null); setDraft((d) => ({ ...d, ...patch })) }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const amount = Number(draft.amount ?? 0)
  const split = draft.mode === 'split'
  const preview = useQuery({
    queryKey: ['receivable-split-preview', draft.termId, draft.amount, draft.baseDate],
    queryFn: () => previewReceivableSplit({ paymentTermId: draft.termId, amount, baseDate: draft.baseDate }),
    enabled: split && !!draft.termId && amount > 0 && !!draft.baseDate,
  })

  const local: Partial<Record<string, string>> = {}
  if (!draft.customer) local.partyId = 'Escolha o cliente.'
  if (!draft.description.trim()) local.description = 'Informe a descrição.'
  if (!draft.accountId) local.accountId = 'Escolha a conta de receita.'
  if (!(amount > 0)) local.amount = 'Informe um valor maior que zero.'
  if (!split && !draft.dueDate) local.dueDate = 'Informe o vencimento.'
  if (split && !draft.termId) local.paymentTermId = 'Escolha a condição de pagamento.'
  const errors: Partial<Record<string, string>> = { ...(submitted ? local : {}), ...Object.fromEntries(Object.entries(server?.fields ?? {}).map(([k, v]) => [k, v[0]])) }

  const save = async () => {
    setSubmitted(true)
    if (Object.keys(local).length || !draft.customer) return false
    const common = { partyId: draft.customer.id, description: draft.description.trim(), accountId: draft.accountId, costCenterId: draft.costCenterId || null, projectId: draft.projectCost.project?.id ?? null, amount, issueDate: draft.issueDate }
    try {
      const saved = split
        ? await createReceivableInstallments({ ...common, paymentTermId: draft.termId, baseDate: draft.baseDate }, idempotency.options)
        : await createReceivable({ ...common, dueDate: draft.dueDate }, idempotency.options)
      notifySuccess(saved.items.length > 1 ? `${saved.items.length} títulos a receber salvos` : 'Título a receber salvo')
      idempotency.renew()
      await navigate({ to: '/app/financeiro/receber/$id', params: { id: saved.items[0].id }, search: { novo: '1' }, ignoreBlocker: true } as never)
      return true
    } catch (e) {
      setServer(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o título.', fields: {} })
      return false
    }
  }

  const chooseCustomer = (option: ComboOption | null) => {
    const customer = option as Customer | null
    set({ customer, termId: draft.termId || customer?.termId || '' })
  }

  return (
    <>
      <PageHeading title="Novo título a receber">
        <p className="mt-2 text-muted-foreground">Um título só, ou parcelado pela condição de pagamento do cliente.</p>
      </PageHeading>
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
        <UnsavedChangesGuard when={dirty} onSave={save} />
        <FormError message={server && Object.keys(server.fields).length === 0 ? server.message : undefined} />
        <FormSection title="Cliente e conta">
          <Field label="Cliente" required error={errors.partyId}>
            {(p) => <Combobox {...p} queryKey={['finance-customers']} search={searchCustomers} value={draft.customer?.id ?? null} selected={draft.customer} placeholder="Busque pelo nome ou CNPJ" onChange={chooseCustomer} />}
          </Field>
          <Field label="Descrição" required error={errors.description} hint="Aparece no título e nas parcelas.">
            {(p) => <input {...p} value={draft.description} maxLength={200} onChange={(e) => set({ description: e.target.value })} className={inputClass} />}
          </Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Conta de receita" required error={errors.accountId}>
              {(p) => (
                <select {...p} value={draft.accountId} onChange={(e) => set({ accountId: e.target.value })} className={inputClass}>
                  <option value="">Escolha</option>
                  {accounts.map((a) => <option key={a.id} value={a.id}>{a.label}</option>)}
                </select>
              )}
            </Field>
            <Field label="Centro de custo" error={errors.costCenterId}>
              {(p) => (
                <select {...p} value={draft.costCenterId} onChange={(e) => set({ costCenterId: e.target.value })} className={inputClass}>
                  <option value="">Sem centro de custo</option>
                  {centers.map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}
                </select>
              )}
            </Field>
          </div>
          <ProjectCostFields value={draft.projectCost} onChange={(projectCost) => set({ projectCost })} withCostType={false} errors={errors} />
        </FormSection>
        <FormSection title="Valor e vencimento">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Valor" required error={errors.amount}>{(p) => <MoneyInput {...p} value={draft.amount} onChange={(v) => set({ amount: v })} />}</Field>
            <Field label="Emissão" required error={errors.issueDate}>{(p) => <DateInput {...p} value={draft.issueDate} onChange={(issueDate) => set({ issueDate })} />}</Field>
          </div>
          <div className="space-y-1">
            <span className="block text-sm font-medium">Forma</span>
            <ToggleGroup label="Forma" value={draft.mode} onChange={(mode) => set({ mode })} options={[{ value: 'single', label: 'Título único' }, { value: 'split', label: 'Parcelar pela condição' }]} />
          </div>
          {split ? (
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Condição de pagamento" required error={errors.paymentTermId} hint={draft.customer?.termId && draft.termId === draft.customer.termId ? 'A condição padrão do cliente.' : undefined}>
                {(p) => (
                  <select {...p} value={draft.termId} onChange={(e) => set({ termId: e.target.value })} className={inputClass}>
                    <option value="">Escolha</option>
                    {terms.map((t) => <option key={t.id} value={t.id}>{t.label}</option>)}
                  </select>
                )}
              </Field>
              <Field label="Data base" required error={errors.baseDate} hint="Os vencimentos contam a partir dela.">{(p) => <DateInput {...p} value={draft.baseDate} onChange={(baseDate) => set({ baseDate })} />}</Field>
            </div>
          ) : (
            <Field label="Vencimento" required error={errors.dueDate}>{(p) => <DateInput {...p} value={draft.dueDate} onChange={(dueDate) => set({ dueDate })} />}</Field>
          )}
          {split && preview.data ? (
            <table className="w-full text-sm" aria-label="Parcelas que serão lançadas">
              <thead className="text-xs text-muted-foreground"><tr><th className="py-1 text-left">Parcela</th><th className="py-1 text-left">Vencimento</th><th className="py-1 text-right">Valor</th></tr></thead>
              <tbody>
                {preview.data.map((p) => (
                  <tr key={p.number} className="border-t border-border">
                    <td className="py-1">{p.number}/{preview.data.length}</td>
                    <td className="py-1 tabular-nums">{formatDate(p.dueDate)}</td>
                    <td className="py-1 text-right"><Money value={p.amount} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
        </FormSection>
        <div className="flex justify-end gap-2">
          <Link to="/app/financeiro/receber" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Link>
          <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{split ? 'Salvar títulos a receber' : 'Salvar título a receber'}</button>
        </div>
      </form>
    </>
  )
}
