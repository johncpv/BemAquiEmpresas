import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { AlertTriangle } from 'lucide-react'
import { useState } from 'react'
import { addPayrollCharge, deletePayrollCharge, getListPayrollChargesSensitiveQueryKey, searchPayrollPayees, updatePayrollCharge, useListPayrollChargesSensitive } from '@/api/generated/bemaqui'
import type { PayrollChargeDetail, PayrollChargeKind, PayrollChargesView, PayrollRunSummary } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { CHARGE_LABELS } from './payroll'

const KINDS: PayrollChargeKind[] = ['Fgts', 'Inss', 'Irrf', 'Union', 'Other']

/** Nome sugerido do órgão no atalho "Cadastrar órgão" (R-PRP-1). */
const SUGGESTED: Record<string, string> = { Fgts: 'Caixa Econômica Federal', Inss: 'Receita Federal do Brasil', Irrf: 'Receita Federal do Brasil', Union: 'Sindicato', Other: '' }

const searchPayees = async (term: string): Promise<ComboOption[]> =>
  (await searchPayrollPayees({ q: term })).map((p) => ({ id: p.id, label: p.name, hint: p.document ?? undefined }))

interface Draft {
  id: string | null
  version: number | null
  kind: PayrollChargeKind
  description: string
  amount: string | null
  dueDate: string
  payee: ComboOption | null
}

/**
 * Aba Encargos da empresa (RH-02a CA 7): uma linha por guia que a contadora calculou, com o vencimento e o órgão vindos do padrão de
 * Configurações → Parâmetros → RH; a soma do FGTS dos holerites aparece ao lado da guia do FGTS, com o aviso âmbar quando difere. A
 * RH-02c gera um título a pagar por guia.
 */
export function ChargesTab({ run, editable }: { run: PayrollRunSummary; editable: boolean }) {
  const query = useListPayrollChargesSensitive(run.id)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar os encargos." onRetry={() => query.refetch()} />
  return <Charges run={run} view={query.data} editable={editable && query.data.editable} />
}

function Charges({ run, view, editable }: { run: PayrollRunSummary; view: PayrollChargesView; editable: boolean }) {
  const client = useQueryClient()
  const canParties = useCan('parties.create')
  const fresh = (kind: PayrollChargeKind): Draft => {
    const d = view.defaults.find((x) => x.kind === kind)
    return { id: null, version: null, kind, description: '', amount: null, dueDate: d?.dueDate ?? '', payee: d?.payeePartyId ? { id: d.payeePartyId, label: d.payeeName ?? '' } : null }
  }
  const [draft, setDraft] = useState<Draft | null>(null)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const set = (patch: Partial<Draft>) => setDraft((d) => (d ? { ...d, ...patch } : d))
  const saved = (next: PayrollChargesView, message: string) => {
    client.setQueryData(getListPayrollChargesSensitiveQueryKey(run.id), next)
    notifySuccess(message)
    setDraft(null)
    setError(null)
  }
  const save = async () => {
    if (!draft) return
    const input = { kind: draft.kind, description: draft.description.trim() || null, amount: Number(draft.amount ?? 0), dueDate: draft.dueDate || null, payeePartyId: draft.payee?.id ?? null, version: draft.version }
    try {
      saved(draft.id ? await updatePayrollCharge(run.id, draft.id, input) : await addPayrollCharge(run.id, input), 'Guia salva')
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a guia.', fields: {} })
    }
  }
  const edit = (c: PayrollChargeDetail) => {
    setError(null)
    setDraft({ id: c.id, version: c.version, kind: c.kind, description: c.description, amount: c.amount.toFixed(2), dueDate: c.dueDate, payee: c.payeePartyId ? { id: c.payeePartyId, label: c.payeeName ?? '' } : null })
  }

  const newButton = editable ? <button type="button" onClick={() => { setError(null); setDraft(fresh('Fgts')) }} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Adicionar guia</button> : null
  return (
    <div className="max-w-3xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">As guias que a contadora calculou para a competência. Cada uma vira um título a pagar no fechamento da folha.</p>
        {draft ? null : newButton}
      </div>
      {view.fgtsMismatch ? (
        <p role="status" className="flex items-start gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <AlertTriangle aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          A guia do FGTS (<Money value={view.fgtsCharges ?? 0} />) difere da soma do FGTS dos holerites (<Money value={view.payslipsFgts} />). Confira com a contadora.
        </p>
      ) : null}
      {view.items.length === 0 && !draft ? (
        <EmptyState title="Nenhuma guia lançada." description={`Informe as guias da competência (FGTS, INSS, IRRF e as outras). A soma do FGTS dos holerites é ${new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(view.payslipsFgts)}.`} action={newButton} />
      ) : (
        <ul aria-label="Guias" className="divide-y divide-border rounded-md border border-border">
          {view.items.map((c) => (
            <li key={c.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
              <span className="space-y-0.5">
                <span className="block font-semibold">{CHARGE_LABELS[c.kind]} · {c.description}</span>
                <span className="block text-muted-foreground">Vence em {formatDate(c.dueDate)} · {c.payeeName ?? 'Sem órgão'}{c.kind === 'Fgts' ? <> · soma dos holerites <Money value={view.payslipsFgts} /></> : null}</span>
              </span>
              <span className="flex items-center gap-2">
                <Money value={c.amount} className="font-semibold" />
                {editable ? (
                  <>
                    <button type="button" onClick={() => edit(c)} className="min-h-11 rounded-md border border-border px-2 py-1 text-xs font-semibold sm:min-h-0">Editar</button>
                    <ConfirmDialog verb="Remover" done="Guia removida" destructive title={`Remover a guia ${CHARGE_LABELS[c.kind]}?`} onConfirm={async () => client.setQueryData(getListPayrollChargesSensitiveQueryKey(run.id), await deletePayrollCharge(run.id, c.id))}
                      trigger={<button type="button" className="min-h-11 rounded-md border border-border px-2 py-1 text-xs font-semibold text-crit-ink sm:min-h-0">Remover</button>} />
                  </>
                ) : null}
              </span>
            </li>
          ))}
        </ul>
      )}
      {draft ? (
        <FormSection title={draft.id ? 'Editar guia' : 'Nova guia'}>
          <div className="space-y-3 sm:col-span-2">
            <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Guia" required error={error?.fields.kind?.[0]}>
                {(p) => (
                  <select {...p} value={draft.kind} onChange={(e) => {
                    const kind = e.target.value as PayrollChargeKind
                    const d = fresh(kind)
                    set({ kind, dueDate: draft.id ? draft.dueDate : d.dueDate, payee: draft.id ? draft.payee : d.payee })
                  }} className={inputClass}>
                    {KINDS.map((k) => <option key={k} value={k}>{CHARGE_LABELS[k]}</option>)}
                  </select>
                )}
              </Field>
              <Field label="Valor" required error={error?.fields.amount?.[0]} hint={draft.kind === 'Fgts' ? `Soma do FGTS dos holerites: ${new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(view.payslipsFgts)}` : undefined}>
                {(p) => <MoneyInput {...p} value={draft.amount} onChange={(amount) => set({ amount })} />}
              </Field>
              <Field label="Descrição" required={draft.kind === 'Other'} error={error?.fields.description?.[0]} hint="Vazio: o nome da guia e a competência.">
                {(p) => <input {...p} value={draft.description} maxLength={100} onChange={(e) => set({ description: e.target.value })} className={inputClass} />}
              </Field>
              <Field label="Vencimento" required error={error?.fields.dueDate?.[0]} hint={view.defaults.find((d) => d.kind === draft.kind)?.dueDate ? 'Pelo padrão de Configurações → Parâmetros → RH (o dia não útil antecipa).' : 'Informado na folha.'}>
                {(p) => <DateInput {...p} value={draft.dueDate} onChange={(dueDate) => set({ dueDate })} />}
              </Field>
            </div>
            <Field label="Órgão favorecido" error={error?.fields.payeePartyId?.[0]} hint="O título a pagar sai no nome dele.">
              {(p) => <Combobox {...p} queryKey={['payroll-payees']} search={searchPayees} value={draft.payee?.id ?? null} selected={draft.payee} placeholder="Busque pelo nome ou CNPJ" onChange={(payee) => set({ payee })} />}
            </Field>
            {canParties && !draft.payee ? (
              <p className="text-sm">
                <Link to="/app/cadastros/terceiros/novo" search={{ papel: 'Supplier', nome: SUGGESTED[draft.kind] || undefined } as never} target="_blank" className="font-semibold text-primary underline">Cadastrar órgão</Link>
                {SUGGESTED[draft.kind] ? <span className="text-muted-foreground"> (já com o nome {SUGGESTED[draft.kind]})</span> : null}
              </p>
            ) : null}
            <div className="flex flex-wrap justify-end gap-2">
              <button type="button" onClick={() => setDraft(null)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
              <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar guia</button>
            </div>
          </div>
        </FormSection>
      ) : null}
      {view.items.some((c) => !c.payeePartyId) ? <StatusChip tone="warn">Guia sem órgão: escolha antes do fechamento</StatusChip> : null}
    </div>
  )
}
