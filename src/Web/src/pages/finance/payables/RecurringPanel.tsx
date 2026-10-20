import { useState } from 'react'
import { createRecurringExpense, deleteRecurringExpense, listFinanceSuppliers, updateRecurringExpense, useListAccounts, useListCostCenters } from '@/api/generated/bemaqui'
import type { RecurringExpenseDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { today, toNumber } from '../receivables/receivables'
import { emptyMethod, PAYABLE_DICAS, toDetails } from './payables'
import { PaymentMethodFields } from './PaymentMethodFields'

const searchSuppliers = async (term: string): Promise<ComboOption[]> => (await listFinanceSuppliers({ q: term })).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined }))
const firstOfMonth = (iso: string) => iso.slice(0, 8) + '01'

const toDraft = (e: RecurringExpenseDetail | null) => ({
  description: e?.description ?? '', party: e ? ({ id: e.partyId, label: e.partyName } as ComboOption) : null, accountId: e?.accountId ?? '', costCenterId: e?.costCenterId ?? '',
  amount: e ? String(e.amount) : (null as string | null), dueDay: e ? String(e.dueDay) : '10', start: e?.start ?? firstOfMonth(today()), end: e?.end ?? '', leadDays: String(e?.leadDays ?? 10),
})

/** Painel da despesa recorrente (FIN-02 CA 16 a 20): mudanças valem para as competências ainda não geradas. */
export function RecurringPanel({ expense, onClose, onSaved }: { expense: RecurringExpenseDetail | null; onClose: () => void; onSaved: () => void }) {
  const accounts = useListAccounts()
  const centers = useListCostCenters()
  const idempotency = useIdempotencyKey()
  const canWrite = useCan(expense ? 'finance.edit' : 'finance.create')
  const [current, setCurrent] = useState(expense)
  const [initial, setInitial] = useState(() => toDraft(expense))
  const [draft, setDraft] = useState(initial)
  const [method, setMethod] = useState(emptyMethod())
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const errors = Object.fromEntries(Object.entries(error?.fields ?? {}).map(([k, v]) => [k, v[0]]))
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial) || method.kind !== ''
  const set = (patch: Partial<typeof draft>) => { setError(null); setDraft((d) => ({ ...d, ...patch })) }

  const save = async () => {
    if (!draft.party) { setError({ message: '', fields: { partyId: ['Escolha o fornecedor.'] } }); return false }
    const body = {
      description: draft.description.trim(), partyId: draft.party.id, accountId: draft.accountId, costCenterId: draft.costCenterId || null, amount: toNumber(draft.amount), dueDay: Number(draft.dueDay || 0),
      start: draft.start, end: draft.end || null, leadDays: Number(draft.leadDays || 0), paymentDetails: toDetails(method),
    }
    try {
      const saved = current ? await updateRecurringExpense(current.id, body) : await createRecurringExpense(body, idempotency.options)
      notifySuccess('Despesa recorrente salva')
      setCurrent(saved)
      setInitial(toDraft(saved))
      setDraft(toDraft(saved))
      setMethod(emptyMethod())
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a despesa.', fields: {} })
      return false
    }
  }

  const expenseAccounts = (accounts.data ?? []).filter((a) => a.type !== 'Revenue' && a.acceptsEntries && (a.isActive || a.id === draft.accountId))
  const activeCenters = (centers.data ?? []).filter((c) => c.isActive)
  return <RecurringForm {...{ current, canWrite, draft, set, method, setMethod, errors, error, expenseAccounts, activeCenters, save, onClose, onSaved, dirty }} />
}

type Draft = ReturnType<typeof toDraft>

interface FormProps {
  current: RecurringExpenseDetail | null
  canWrite: boolean
  draft: Draft
  set: (patch: Partial<Draft>) => void
  method: ReturnType<typeof emptyMethod>
  setMethod: (m: ReturnType<typeof emptyMethod>) => void
  errors: Record<string, string>
  error: { message: string; fields: Record<string, string[]> } | null
  expenseAccounts: { id: string; code: string; name: string }[]
  activeCenters: { id: string; name: string }[]
  save: () => Promise<boolean>
  onClose: () => void
  onSaved: () => void
  dirty: boolean
}

function RecurringFooter({ current, canWrite, set, save, onClose, onSaved }: Pick<FormProps, 'current' | 'canWrite' | 'set' | 'save' | 'onClose' | 'onSaved'>) {
  return (
    <>
      {current && canWrite ? (
        <ConfirmDialog verb="Excluir" done="Despesa excluída" destructive title={`Excluir a despesa ${current.description}?`} description="Só dá para excluir despesa que ainda não gerou título."
          onConfirm={async () => { await deleteRecurringExpense(current.id); onSaved(); onClose() }}
          recovery={{ verb: 'Encerrar neste mês', when: (code) => code === 'recurring_has_payables', onSelect: async () => set({ end: firstOfMonth(today()) }) }}
          trigger={<button type="button" className="mr-auto rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir</button>} />
      ) : null}
      <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
      {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar despesa recorrente</button> : null}
    </>
  )
}

function RecurringForm(props: FormProps) {
  const { current, canWrite, draft, set, method, setMethod, errors, error, expenseAccounts, activeCenters, save, onClose, dirty } = props
  const digits = (v: string) => v.replace(/\D/g, '')
  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={current ? `Despesa ${current.description}` : 'Nova despesa recorrente'} dirty={dirty && canWrite} onSave={save} footer={<RecurringFooter {...props} />}>
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Descrição" required error={errors.description}>{(p) => <input {...p} value={draft.description} maxLength={150} onChange={(e) => set({ description: e.target.value })} className={inputClass} />}</Field>
        <Field label="Fornecedor" required error={errors.partyId}>{(p) => <Combobox {...p} queryKey={['finance-suppliers']} search={searchSuppliers} value={draft.party?.id ?? null} selected={draft.party} placeholder="Busque pelo nome ou CNPJ" onChange={(party) => set({ party })} />}</Field>
        <Field label="Conta" required error={errors.accountId}>{(p) => <select {...p} value={draft.accountId} onChange={(e) => set({ accountId: e.target.value })} className={inputClass}><option value="">Escolha</option>{expenseAccounts.map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}</select>}</Field>
        <Field label="Centro de custo" error={errors.costCenterId}>{(p) => <select {...p} value={draft.costCenterId} onChange={(e) => set({ costCenterId: e.target.value })} className={inputClass}><option value="">Sem centro de custo</option>{activeCenters.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select>}</Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Valor" required error={errors.amount}>{(p) => <MoneyInput {...p} value={draft.amount} onChange={(amount) => set({ amount })} />}</Field>
          <Field label="Dia do vencimento" required error={errors.dueDay} hint="No mês sem esse dia, o último.">{(p) => <input {...p} inputMode="numeric" maxLength={2} value={draft.dueDay} onChange={(e) => set({ dueDay: digits(e.target.value) })} className={inputClass + ' tabular-nums'} />}</Field>
          <Field label="Início" required error={errors.start}>{(p) => <DateInput {...p} value={draft.start} onChange={(start) => set({ start })} />}</Field>
          <Field label="Fim" error={errors.end} hint="Vazio: sem fim.">{(p) => <DateInput {...p} value={draft.end} onChange={(end) => set({ end })} />}</Field>
          <Field label="Antecedência (dias)" error={errors.leadDays} dica={PAYABLE_DICAS.antecedencia}>{(p) => <input {...p} inputMode="numeric" maxLength={2} value={draft.leadDays} onChange={(e) => set({ leadDays: digits(e.target.value) })} className={inputClass + ' tabular-nums'} />}</Field>
        </div>
        <PaymentMethodFields value={method} onChange={setMethod} errors={errors} />
        {current && current.nextDueDates.length > 0 ? <p className="text-sm text-muted-foreground">Próximos vencimentos: {current.nextDueDates.map(formatDate).join(', ')}.</p> : null}
      </fieldset>
    </SidePanel>
  )
}
