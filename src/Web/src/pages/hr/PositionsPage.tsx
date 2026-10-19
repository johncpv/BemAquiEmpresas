import { useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { getGetPositionsSummaryQueryKey, getListPositionsQueryKey, useGetPositionsSummary, useListPositions } from '@/api/generated/bemaqui'
import type { PositionDetail } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from './HrTabs'
import { PositionPanel } from './PositionPanel'

const cbo = (c: string | null) => (c ? `${c.slice(0, 4)}-${c.slice(4)}` : '—')

/** RH → Cargos (CAD-04a CA 1 a 10): custo-hora de cada cargo, sem salário na lista; edição no painel lateral. */
export function PositionsPage() {
  const [search] = useTableSearch()
  const { inativos } = useTableFilters([])
  const client = useQueryClient()
  const query = useListPositions({ q: search.q, includeInactive: inativos === 'true', page: search.page, pageSize: search.pageSize, sort: search.sort })
  const summary = useGetPositionsSummary()
  const [editing, setEditing] = useState<{ position: PositionDetail | null } | null>(null)
  const refresh = () => {
    void client.invalidateQueries({ queryKey: getListPositionsQueryKey() })
    void client.invalidateQueries({ queryKey: getGetPositionsSummaryQueryKey() })
  }
  const open = () => setEditing({ position: null })

  const columns: ColumnDef<PositionDetail>[] = [
    { id: 'name', header: 'Nome', meta: { sortField: 'name' }, cell: ({ row: { original: p } }) => <button type="button" onClick={() => setEditing({ position: p })} className="font-semibold text-primary hover:underline">{p.name}</button> },
    { id: 'cbo', header: 'CBO', meta: { secondary: true }, cell: ({ row: { original: p } }) => <span className="tabular-nums">{cbo(p.cbo)}</span> },
    { id: 'center', header: 'Centro padrão', meta: { secondary: true }, cell: ({ row: { original: p } }) => p.defaultWorkCenterName ?? '—' },
    {
      id: 'cost', header: 'Custo-hora', meta: { numeric: true, sortField: 'hourlyCost' }, cell: ({ row: { original: p } }) => (
        <span className="inline-flex items-center gap-1.5"><Money value={p.hourlyCost} />{p.hourlyCostManual ? <StatusChip tone="warn">Ajustado</StatusChip> : null}</span>
      ),
    },
    { id: 'status', header: 'Situação', cell: ({ row: { original: p } }) => <StatusChip tone={p.isActive ? 'ok' : 'neutral'}>{p.isActive ? 'Ativo' : 'Inativo'}</StatusChip> },
  ]

  return (
    <>
      <PageHeading title="RH: Cargos">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Cargos da empresa e o custo de cada hora trabalhada.</p>
          <Can perm="hr.create"><button type="button" onClick={open} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo cargo</button></Can>
        </div>
      </PageHeading>
      <HrTabs />
      <DataTable label="Cargos" columns={columns} query={query} getRowId={(p) => p.id} inactiveFilter searchPlaceholder="Buscar pelo nome"
        empty={{
          title: 'Cadastre os cargos da empresa para calcular o custo da hora trabalhada.',
          action: <Can perm="hr.create"><button type="button" onClick={open} className="text-sm font-semibold text-primary">Novo cargo</button></Can>,
        }} />
      {editing ? <PositionPanel position={editing.position} chargesPct={summary.data?.chargesPct ?? 0} onClose={() => setEditing(null)} onSaved={refresh} /> : null}
    </>
  )
}
