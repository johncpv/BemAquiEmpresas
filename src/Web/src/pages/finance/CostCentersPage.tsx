import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { getListCostCentersQueryKey, useListCostCenters } from '@/api/generated/bemaqui'
import type { CostCenterDetail } from '@/api/generated/model'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { TreeTable } from '@/components/ui/tree-table'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { toTree } from './chart'
import { CostCenterPanel } from './CostCenterPanel'
import { FinanceTabs } from './FinanceTabs'

/** Financeiro → Centros de custo (CAD-05a CA 16): a mesma árvore do plano de contas. */
export function CostCentersPage() {
  const query = useListCostCenters()
  const client = useQueryClient()
  const navigate = useNavigate()
  const { inativos } = useSearch({ strict: false }) as { inativos?: string }
  const showInactive = inativos === 'true'
  const [editing, setEditing] = useState<{ center: CostCenterDetail | null; parent: CostCenterDetail | null } | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListCostCentersQueryKey() })
  const newRoot = <Can perm="finance.create"><button type="button" onClick={() => setEditing({ center: null, parent: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo centro de custo</button></Can>

  return (
    <>
      <PageHeading title="Financeiro: Centros de custo">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">As áreas da empresa, para ver quanto cada uma custa.</p>
          {newRoot}
        </div>
      </PageHeading>
      <FinanceTabs />
      <label className="mb-3 flex items-center justify-end gap-2 text-sm">
        <input type="checkbox" className="size-4" checked={showInactive} onChange={(e) => void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, inativos: e.target.checked ? 'true' : undefined }) } as never)} />
        Mostrar inativos
      </label>
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : query.data.length === 0 ? (
        <EmptyState title="Cadastre os centros de custo da empresa." description="Por exemplo: Administrativo, Comercial, Produção e Instalação." action={newRoot} />
      ) : (
        <TreeTable label="Centros de custo" nodes={toTree(query.data, showInactive)}
          renderLabel={(n) => (
            <span className="inline-flex flex-wrap items-center gap-1.5">
              <button type="button" onClick={() => setEditing({ center: n.item, parent: null })} className="text-left font-semibold text-primary hover:underline">
                <span className="font-mono tabular-nums">{n.item.code}</span> {n.item.name}
              </button>
              {n.item.code.split('.').length < 3 && n.item.isActive ? (
                <Can perm="finance.create">
                  <button type="button" onClick={() => setEditing({ center: null, parent: n.item })} className="text-xs font-semibold text-primary">Novo centro filho<span className="sr-only"> de {n.item.code}</span></button>
                </Can>
              ) : null}
            </span>
          )}
          columns={[{ header: 'Situação', cell: (n) => <StatusChip tone={n.item.isActive ? 'ok' : 'neutral'}>{n.item.isActive ? 'Ativo' : 'Inativo'}</StatusChip> }]} />
      )}
      {editing ? <CostCenterPanel center={editing.center} parent={editing.parent} all={query.data ?? []} onClose={() => setEditing(null)} onSaved={() => { refresh(); setEditing(null) }} /> : null}
    </>
  )
}
