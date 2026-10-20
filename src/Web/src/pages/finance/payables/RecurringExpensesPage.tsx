import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getListRecurringExpensesQueryKey, useListRecurringExpenses } from '@/api/generated/bemaqui'
import type { RecurringExpenseDetail } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { RecurringPanel } from './RecurringPanel'

/** Financeiro → Recorrentes (FIN-02 CA 16 a 20): despesas fixas que viram títulos a pagar todo mês. */
export function RecurringExpensesPage() {
  const query = useListRecurringExpenses()
  const client = useQueryClient()
  const [editing, setEditing] = useState<{ expense: RecurringExpenseDetail | null } | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListRecurringExpensesQueryKey() })
  const newButton = <Can perm="finance.create"><button type="button" onClick={() => setEditing({ expense: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova despesa recorrente</button></Can>
  let body
  if (query.isPending) body = <Skeleton className="h-48" />
  else if (query.isError) body = <ErrorState onRetry={() => query.refetch()} />
  else if (query.data.length === 0) body = <EmptyState title="Cadastre as despesas fixas do mês." description="O sistema lança o título de cada mês alguns dias antes do vencimento." action={newButton} />
  else {
    body = (
      <div className="overflow-x-auto rounded-md border border-border bg-card">
        <table className="w-full text-sm" aria-label="Despesas recorrentes">
          <thead className="border-b border-border text-xs text-muted-foreground">
            <tr><th className="px-3 py-2 text-left">Descrição</th><th className="hidden px-3 py-2 text-left sm:table-cell">Fornecedor</th><th className="px-3 py-2 text-right">Valor</th><th className="px-3 py-2 text-left">Próximo vencimento</th><th className="px-3 py-2 text-left">Situação</th></tr>
          </thead>
          <tbody>
            {query.data.map((r) => (
              <tr key={r.id} className="border-b border-border last:border-0">
                <td className="px-3 py-2"><button type="button" onClick={() => setEditing({ expense: r })} className="font-semibold text-primary hover:underline">{r.description}</button></td>
                <td className="hidden px-3 py-2 sm:table-cell">{r.partyName}</td>
                <td className="px-3 py-2 text-right"><Money value={r.amount} /></td>
                <td className="px-3 py-2 tabular-nums">{r.nextDueDates[0] ? formatDate(r.nextDueDates[0]) : '—'}</td>
                <td className="px-3 py-2"><StatusChip tone={r.isActive ? 'ok' : 'neutral'}>{r.isActive ? 'Ativa' : 'Encerrada'}</StatusChip></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }

  return (
    <>
      <PageHeading title="Financeiro: Recorrentes">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Despesas fixas, como aluguel e internet, lançadas em A pagar todo mês.</p>
          {newButton}
        </div>
      </PageHeading>
      <FinanceTabs />
      {body}
      {editing ? <RecurringPanel expense={editing.expense} onClose={() => setEditing(null)} onSaved={refresh} /> : null}
    </>
  )
}
