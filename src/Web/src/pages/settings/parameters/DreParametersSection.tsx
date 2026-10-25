import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { confirmDreParameter, getGetDreParametersQueryKey, updateDreParameters, useGetDreParameters } from '@/api/generated/bemaqui'
import type { DreParameterItem, DreParametersInput, DreParametersView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { DRE_FIELDS, dreChanged, PENDING_LABEL } from './dre-parameters'

/**
 * Financeiro: contas do DRE (FIN-08 CA 8 e 16, ADR 0042). As contas de juros, multa e descontos das baixas e a regra do sinal da
 * D4 vêm preenchidas com o valor proposto e o selo "A confirmar com a contadora" até alguém com settings.edit marcar como
 * conferido; trocar o valor volta o selo. Aparece com finance.view, salva com settings.edit, com o histórico de cada valor.
 */
export function DreParametersSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('finance.view')
  const query = useGetDreParameters({ query: { enabled: canSee } })
  if (!canSee) return null
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError || !query.data?.items) return null
  return <DreForm key={JSON.stringify(query.data.items.map((i) => [i.accountId, i.enabled, i.reviewState]))} view={query.data} canEdit={canEdit} />
}

function Review({ item, canEdit, onConfirm }: { item: DreParameterItem; canEdit: boolean; onConfirm: () => void }) {
  if (item.reviewState === 'Confirmed') {
    return <StatusChip tone="ok">Conferido{item.reviewedByName ? ` por ${item.reviewedByName}` : ''}{item.reviewedAt ? ` em ${formatDateTime(item.reviewedAt)}` : ''}</StatusChip>
  }

  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      <StatusChip tone="warn">{PENDING_LABEL}</StatusChip>
      {canEdit ? <button type="button" onClick={onConfirm} className="min-h-11 rounded-md border border-border px-2 py-1 text-xs font-semibold sm:min-h-0">Marcar como conferido</button> : null}
    </span>
  )
}

function DreForm({ view, canEdit }: { view: DreParametersView; canEdit: boolean }) {
  const client = useQueryClient()
  const initial = Object.fromEntries(view.items.map((i) => [i.key, i.enabled ?? i.accountId ?? '']))
  const [draft, setDraft] = useState<Record<string, string | boolean>>(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const saved = (next: DreParametersView, message: string) => {
    client.setQueryData(getGetDreParametersQueryKey(), next)
    void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/FinanceParameter/') || String(q.queryKey[0]).startsWith('/api/v1/finance/dre') })
    notifySuccess(message)
    setError(null)
  }
  const fail = (e: unknown, fallback: string) => setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: fallback, fields: {} })
  const save = async () => {
    const input: Record<string, string | boolean | null> = { interestIncomeAccountId: null, interestExpenseAccountId: null, discountGrantedAccountId: null, discountReceivedAccountId: null, downPaymentAsAdvance: null }
    for (const f of DRE_FIELDS) {
      if (draft[f.key] !== initial[f.key]) input[f.field] = draft[f.key]
    }
    try {
      saved(await updateDreParameters(input as unknown as DreParametersInput), 'Contas do DRE salvas')
    } catch (e) {
      fail(e, 'Não foi possível salvar as contas do DRE.')
    }
  }
  const confirm = async (key: string) => {
    try {
      saved(await confirmDreParameter({ key }), 'Valor conferido')
    } catch (e) {
      fail(e, 'Não foi possível marcar como conferido.')
    }
  }

  return (
    <FormSection title="Financeiro: contas do DRE" description={`Onde entram juros, multas e descontos das baixas e como o sinal do contrato entra na receita. ${view.pendingCount > 0 ? `${view.pendingCount} ${view.pendingCount === 1 ? 'valor a confirmar' : 'valores a confirmar'} com a contadora.` : 'Tudo conferido com a contadora.'}`}>
      <div className="space-y-3 sm:col-span-2">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        {view.items.map((item) => {
          const field = DRE_FIELDS.find((f) => f.key === item.key)
          const fieldError = field ? error?.fields[field.field]?.[0] : undefined
          const id = `dre-${item.key}`
          return (
            <div key={item.key} role="group" aria-label={item.label} className="space-y-2 rounded-md border border-border p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <label htmlFor={id} className="inline-flex items-center text-sm font-semibold">{item.label}<Dica nome={item.label} texto={item.description} /></label>
                <Review item={item} canEdit={canEdit} onConfirm={() => void confirm(item.key)} />
              </div>
              {item.enabled != null ? (
                <label className="flex items-start gap-2 text-sm">
                  <input id={id} type="checkbox" disabled={!canEdit} checked={draft[item.key] === true} onChange={(e) => setDraft((d) => ({ ...d, [item.key]: e.target.checked }))} className="mt-0.5 size-4 accent-primary" />
                  <span>Ligado (proposta da revisão D4, padrão)</span>
                </label>
              ) : (
                <select id={id} disabled={!canEdit} value={String(draft[item.key] ?? '')} onChange={(e) => setDraft((d) => ({ ...d, [item.key]: e.target.value }))} className={inputClass}
                  aria-invalid={fieldError ? true : undefined} aria-describedby={fieldError ? `${id}-error` : undefined}>
                  {item.options.some((o) => o.id === draft[item.key]) ? null : <option value={String(draft[item.key] ?? '')}>{item.accountCode ? `${item.accountCode} ${item.accountName}` : 'Escolha a conta'}</option>}
                  {item.options.map((o) => <option key={o.id} value={o.id}>{o.code} {o.name}</option>)}
                </select>
              )}
              {fieldError ? <p id={`${id}-error`} role="alert" className="text-xs font-medium text-crit-ink">{fieldError}</p> : null}
              {item.parameterId ? (
                <details className="text-sm">
                  <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Histórico de alterações</summary>
                  <div className="mt-2"><RecordHistory entity="FinanceParameter" id={item.parameterId} /></div>
                </details>
              ) : null}
            </div>
          )
        })}
        {canEdit ? (
          <button type="button" disabled={!dreChanged(initial, draft)} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar contas do DRE</button>
        ) : null}
      </div>
    </FormSection>
  )
}
