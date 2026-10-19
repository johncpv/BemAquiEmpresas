import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getListAccountsQueryKey, useListAccounts } from '@/api/generated/bemaqui'
import type { AccountDetail } from '@/api/generated/model'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { TreeTable } from '@/components/ui/tree-table'
import { Dica } from '@/help/Dica'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { AccountPanel } from './AccountPanel'
import { ACCOUNT_TYPES, ANALYTIC_DICA, DRE_GROUPS, TO_CLASSIFY_DICA, toTree } from './chart'
import { FinanceTabs } from './FinanceTabs'

/** Financeiro → Plano de contas (CAD-05a CA 4 a 14): árvore com busca e o filtro "Inativas" na URL; edição no painel lateral. */
export function ChartOfAccountsPage() {
  const query = useListAccounts()
  const client = useQueryClient()
  const navigate = useNavigate()
  const { inativas } = useSearch({ strict: false }) as { inativas?: string }
  const showInactive = inativas === 'true'
  const [editing, setEditing] = useState<{ account: AccountDetail | null; parent: AccountDetail | null } | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListAccountsQueryKey() })
  const newRoot = <Can perm="finance.create"><button type="button" onClick={() => setEditing({ account: null, parent: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova conta contábil</button></Can>

  return (
    <>
      <PageHeading title="Financeiro: Plano de contas">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Contas para classificar receitas e despesas.</p>
          {newRoot}
        </div>
      </PageHeading>
      <FinanceTabs />
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2 text-sm">
        <span className="inline-flex items-center gap-1 text-muted-foreground">Analítica e sintética <Dica nome="Analítica e sintética" texto={ANALYTIC_DICA} /></span>
        <label className="flex items-center gap-2">
          <input type="checkbox" className="size-4" checked={showInactive} onChange={(e) => void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, inativas: e.target.checked ? 'true' : undefined }) } as never)} />
          Mostrar inativas
        </label>
      </div>
      {query.isPending ? <Skeleton className="h-96" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : query.data.length === 0 ? (
        <EmptyState title="Cadastre as contas para classificar receitas e despesas." action={newRoot} />
      ) : (
        <TreeTable label="Plano de contas" nodes={toTree(query.data, showInactive)}
          renderLabel={(n) => (
            <span className="inline-flex flex-wrap items-center gap-1.5">
              <button type="button" onClick={() => setEditing({ account: n.item, parent: null })} className="text-left font-semibold text-primary hover:underline">
                <span className="font-mono tabular-nums">{n.item.code}</span> {n.item.name}
              </button>
              {n.item.code.endsWith('.9') ? <Dica nome={n.item.name} texto={TO_CLASSIFY_DICA} /> : null}
              {n.item.code.split('.').length < 3 && n.item.isActive ? (
                <Can perm="finance.create">
                  <button type="button" onClick={() => setEditing({ account: null, parent: n.item })} className="text-xs font-semibold text-primary">Nova conta filha<span className="sr-only"> de {n.item.code}</span></button>
                </Can>
              ) : null}
            </span>
          )}
          columns={[
            { header: 'Tipo', cell: (n) => ACCOUNT_TYPES[n.item.type] },
            { header: 'Grupo da DRE', cell: (n) => DRE_GROUPS[n.item.dreGroup] },
            { header: 'Lançamentos', cell: (n) => <Tag>{n.item.acceptsEntries ? 'Analítica' : 'Sintética'}</Tag> },
            { header: 'Situação', cell: (n) => <StatusChip tone={n.item.isActive ? 'ok' : 'neutral'}>{n.item.isActive ? 'Ativa' : 'Inativa'}</StatusChip> },
          ]} />
      )}
      {editing ? <AccountPanel account={editing.account} parent={editing.parent} all={query.data ?? []} onClose={() => setEditing(null)} onSaved={() => { refresh(); setEditing(null) }} /> : null}
    </>
  )
}
