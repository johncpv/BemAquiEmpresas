import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { createPayable, createPayableInstallments, listFinanceSuppliers, previewReceivableSplit, useListAccounts, useListCostCenters, useListPaymentTerms } from '@/api/generated/bemaqui'
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
import { today, toNumber } from '../receivables/receivables'
import { emptyMethod, type MethodDraft, PAYABLE_DICAS, suggestCostType, toDetails, type RetentionDraft } from './payables'
import { type ProjectCost, ProjectCostFields } from './ProjectCostFields'
import { PaymentMethodFields } from './PaymentMethodFields'
import { RetentionFields } from './RetentionFields'

type Supplier = ComboOption & { termId: string | null }

const searchSuppliers = async (term: string): Promise<Supplier[]> =>
  (await listFinanceSuppliers({ q: term })).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined, termId: s.defaultPaymentTermId }))

/** Novo título a pagar (FIN-02 CA 1 a 6): seções Fornecedor e conta, Valor e vencimento, Retenções e Forma de pagamento. */
export function NewPayablePage() {
  const accounts = useListAccounts()
  const centers = useListCostCenters()
  const terms = useListPaymentTerms({ pageSize: 100 })
  if (accounts.isPending || centers.isPending || terms.isPending) return <Skeleton className="h-96 max-w-3xl" />
  if (accounts.isError || centers.isError || terms.isError) return <ErrorState onRetry={() => { void accounts.refetch(); void centers.refetch(); void terms.refetch() }} />
  const expense = accounts.data.filter((a) => a.type !== 'Revenue' && a.acceptsEntries && a.isActive)
  return (
    <PayableForm
      accounts={expense.map((a) => ({ id: a.id, label: `${a.code} ${a.name}` }))}
      centers={centers.data.filter((c) => c.isActive).map((c) => ({ id: c.id, label: c.name }))}
      terms={terms.data.items.filter((t) => t.isActive).map((t) => ({ id: t.id, label: t.name }))}
    />
  )
}

interface Option { id: string; label: string }

interface Draft {
  supplier: Supplier | null
  description: string
  accountId: string
  costCenterId: string
  amount: string | null
  issueDate: string
  dueDate: string
  mode: 'single' | 'split'
  termId: string
  baseDate: string
  retentions: RetentionDraft
  method: MethodDraft
  projectCost: ProjectCost
}

function PayableForm({ accounts, centers, terms }: { accounts: Option[]; centers: Option[]; terms: Option[] }) {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  // Pré-preenchido pelo próximo passo do pagamento (recolhimento de retenções, FIN-03a CA 19).
  const prefill = useSearch({ strict: false }) as { descricao?: string; valor?: string; vencimento?: string }
  const [initial] = useState<Draft>(() => ({
    supplier: null, description: prefill.descricao ?? '', accountId: '', costCenterId: '', amount: prefill.valor ?? null, issueDate: today(), dueDate: prefill.vencimento ?? '', mode: 'single', termId: '', baseDate: today(),
    retentions: { iss: null, inss: null, ir: null, pcc: null }, method: emptyMethod(), projectCost: { project: null, costType: '' },
  }))
  const [draft, setDraft] = useState(initial)
  const [submitted, setSubmitted] = useState(false)
  const [server, setServer] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const set = (patch: Partial<Draft>) => { setServer(null); setDraft((d) => ({ ...d, ...patch })) }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const amount = toNumber(draft.amount)
  const split = draft.mode === 'split'
  const preview = useQuery({
    queryKey: ['payable-split-preview', draft.termId, draft.amount, draft.baseDate],
    queryFn: () => previewReceivableSplit({ paymentTermId: draft.termId, amount, baseDate: draft.baseDate }),
    enabled: split && !!draft.termId && amount > 0 && !!draft.baseDate,
  })

  const local: Partial<Record<string, string>> = {}
  if (!draft.supplier) local.partyId = 'Escolha o fornecedor.'
  if (!draft.description.trim()) local.description = 'Informe a descrição.'
  if (!draft.accountId) local.accountId = 'Escolha a conta.'
  if (!(amount > 0)) local.amount = 'Informe um valor maior que zero.'
  if (!split && !draft.dueDate) local.dueDate = 'Informe o vencimento.'
  if (split && !draft.termId) local.paymentTermId = 'Escolha a condição de pagamento.'
  if (draft.projectCost.project && !draft.projectCost.costType) local.costType = 'Informe a natureza do custo do projeto.'
  const errors: Partial<Record<string, string>> = { ...(submitted ? local : {}), ...Object.fromEntries(Object.entries(server?.fields ?? {}).map(([k, v]) => [k, v[0]])) }

  const save = async () => {
    setSubmitted(true)
    if (Object.keys(local).length || !draft.supplier) return false
    const r = draft.retentions
    const common = {
      partyId: draft.supplier.id, description: draft.description.trim(), accountId: draft.accountId, costCenterId: draft.costCenterId || null, amount, issueDate: draft.issueDate,
      retentionIss: toNumber(r.iss), retentionInss: toNumber(r.inss), retentionIr: toNumber(r.ir), retentionPcc: toNumber(r.pcc), paymentDetails: toDetails(draft.method),
      projectId: draft.projectCost.project?.id ?? null, costType: draft.projectCost.project ? draft.projectCost.costType || null : null,
    }
    try {
      const saved = split
        ? await createPayableInstallments({ ...common, paymentTermId: draft.termId, baseDate: draft.baseDate }, idempotency.options)
        : await createPayable({ ...common, dueDate: draft.dueDate }, idempotency.options)
      notifySuccess(saved.items.length > 1 ? `${saved.items.length} títulos a pagar salvos` : 'Título a pagar salvo')
      idempotency.renew()
      await navigate({ to: '/app/financeiro/pagar/$id', params: { id: saved.items[0].id }, search: { novo: '1' }, ignoreBlocker: true } as never)
      return true
    } catch (e) {
      setServer(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o título.', fields: {} })
      return false
    }
  }

  const codeOf = (id: string) => accounts.find((a) => a.id === id)?.label.split(' ')[0]
  const suggested = suggestCostType(codeOf(draft.accountId))
  const chooseAccount = (accountId: string) => set({ accountId, projectCost: draft.projectCost.project && !draft.projectCost.costType ? { ...draft.projectCost, costType: suggestCostType(codeOf(accountId)) ?? '' } : draft.projectCost })

  const chooseSupplier = (option: ComboOption | null) => {
    const supplier = option as Supplier | null
    set({ supplier, termId: draft.termId || supplier?.termId || '' })
  }

  return (
    <>
      <PageHeading title="Novo título a pagar">
        <p className="mt-2 text-muted-foreground">O pagamento sai depois, pela ordem de pagamento aprovada.</p>
      </PageHeading>
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
        <UnsavedChangesGuard when={dirty} onSave={save} />
        <FormError message={server && Object.keys(server.fields).length === 0 ? server.message : undefined} />
        <FormSection title="Fornecedor e conta">
          <Field label="Fornecedor" required error={errors.partyId} hint="Fornecedores e instaladores ativos.">
            {(p) => <Combobox {...p} queryKey={['finance-suppliers']} search={searchSuppliers} value={draft.supplier?.id ?? null} selected={draft.supplier} placeholder="Busque pelo nome ou CNPJ" onChange={chooseSupplier} />}
          </Field>
          <Field label="Descrição" required error={errors.description}>{(p) => <input {...p} value={draft.description} maxLength={200} onChange={(e) => set({ description: e.target.value })} className={inputClass} />}</Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Conta" required error={errors.accountId} hint="Despesa, investimento ou financiamento.">
              {(p) => <select {...p} value={draft.accountId} onChange={(e) => chooseAccount(e.target.value)} className={inputClass}><option value="">Escolha</option>{accounts.map((a) => <option key={a.id} value={a.id}>{a.label}</option>)}</select>}
            </Field>
            <Field label="Centro de custo" error={errors.costCenterId}>
              {(p) => <select {...p} value={draft.costCenterId} onChange={(e) => set({ costCenterId: e.target.value })} className={inputClass}><option value="">Sem centro de custo</option>{centers.map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}</select>}
            </Field>
          </div>
        </FormSection>
        <FormSection title="Projeto">
          <ProjectCostFields value={draft.projectCost} onChange={(projectCost) => set({ projectCost })} suggested={suggested} errors={errors} />
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
              <Field label="Condição de pagamento" required error={errors.paymentTermId}>
                {(p) => <select {...p} value={draft.termId} onChange={(e) => set({ termId: e.target.value })} className={inputClass}><option value="">Escolha</option>{terms.map((t) => <option key={t.id} value={t.id}>{t.label}</option>)}</select>}
              </Field>
              <Field label="Data base" required>{(p) => <DateInput {...p} value={draft.baseDate} onChange={(baseDate) => set({ baseDate })} />}</Field>
            </div>
          ) : (
            <Field label="Vencimento" required error={errors.dueDate}>{(p) => <DateInput {...p} value={draft.dueDate} onChange={(dueDate) => set({ dueDate })} />}</Field>
          )}
          {split && preview.data ? (
            <table className="w-full text-sm" aria-label="Parcelas que serão lançadas">
              <thead className="text-xs text-muted-foreground"><tr><th className="py-1 text-left">Parcela</th><th className="py-1 text-left">Vencimento</th><th className="py-1 text-right">Valor</th></tr></thead>
              <tbody>{preview.data.map((p) => <tr key={p.number} className="border-t border-border"><td className="py-1">{p.number}/{preview.data.length}</td><td className="py-1 tabular-nums">{formatDate(p.dueDate)}</td><td className="py-1 text-right"><Money value={p.amount} /></td></tr>)}</tbody>
            </table>
          ) : null}
        </FormSection>
        <FormSection title="Retenções" description={PAYABLE_DICAS.retencoes}>
          <RetentionFields value={draft.retentions} onChange={(retentions) => set({ retentions })} amount={amount} errors={errors} />
        </FormSection>
        <FormSection title="Forma de pagamento">
          <PaymentMethodFields value={draft.method} onChange={(method) => set({ method })} errors={errors} />
        </FormSection>
        <div className="flex justify-end gap-2">
          <Link to="/app/financeiro/pagar" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Link>
          <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{split ? 'Salvar títulos a pagar' : 'Salvar título a pagar'}</button>
        </div>
      </form>
    </>
  )
}
