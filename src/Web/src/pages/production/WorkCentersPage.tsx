import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { getGetSuggestedShiftsQueryKey, getListWorkCentersQueryKey, useGetSuggestedShifts, useListWorkCenters } from '@/api/generated/bemaqui'
import type { WorkCenterDetail } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { NextSteps } from '@/components/ui/next-steps'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from './ProductionTabs'
import { WorkCenterPanel } from './WorkCenterPanel'
import { formatHours, shiftsSummary } from './shifts'

/** Produção → Centros de trabalho (CAD-03 CA 12 a 14): horas por dia dos turnos; edição no painel lateral. */
export function WorkCentersPage() {
  const [search] = useTableSearch()
  const { inativos } = useTableFilters([])
  const client = useQueryClient()
  const navigate = useNavigate()
  const query = useListWorkCenters({ q: search.q, includeInactive: inativos === 'true', page: search.page, pageSize: search.pageSize })
  const suggested = useGetSuggestedShifts()
  const [editing, setEditing] = useState<{ center: WorkCenterDetail | null } | null>(null)
  const [steps, setSteps] = useState(false)

  const columns: ColumnDef<WorkCenterDetail>[] = [
    { id: 'name', header: 'Nome', cell: ({ row: { original: c } }) => <button type="button" onClick={() => setEditing({ center: c })} className="font-semibold text-primary hover:underline">{c.name}</button> },
    { id: 'line', header: 'Linha', meta: { secondary: true }, cell: ({ row: { original: c } }) => c.line ?? '—' },
    { id: 'hours', header: 'Horas/dia', meta: { numeric: true }, cell: ({ row: { original: c } }) => <span className="tabular-nums">{formatHours(c.hoursPerDay)} h</span> },
    { id: 'shifts', header: 'Turnos', meta: { secondary: true }, cell: ({ row: { original: c } }) => <span className="tabular-nums">{shiftsSummary(c.shifts)}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: c } }) => <StatusChip tone={c.isActive ? 'ok' : 'neutral'}>{c.isActive ? 'Ativo' : 'Inativo'}</StatusChip> },
  ]
  const open = () => setEditing({ center: null })

  return (
    <>
      <PageHeading title="Produção: Centros de trabalho">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Onde as etapas são feitas e quantas horas cada centro tem por dia.</p>
          <Can perm="production.create">
            <button type="button" onClick={open} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo centro de trabalho</button>
          </Can>
        </div>
      </PageHeading>
      <ProductionTabs />
      {steps ? <div className="mb-3"><NextSteps steps={[{ label: 'Cadastrar o calendário', onSelect: () => void navigate({ to: '/app/producao/calendario' }) }]} onDismiss={() => setSteps(false)} /></div> : null}
      <DataTable label="Centros de trabalho" columns={columns} query={query} getRowId={(c) => c.id} inactiveFilter searchPlaceholder="Buscar pelo nome ou linha"
        empty={{
          title: 'Cadastre os centros de trabalho da fábrica, como Corte e Montagem, com os turnos de cada um.',
          action: <Can perm="production.create"><button type="button" onClick={open} className="text-sm font-semibold text-primary">Novo centro de trabalho</button></Can>,
        }} />
      {editing && (editing.center || !suggested.isPending) ? (
        <WorkCenterPanel center={editing.center} suggested={suggested.data ?? []} onClose={() => setEditing(null)}
          onSaved={(created) => {
            void client.invalidateQueries({ queryKey: getListWorkCentersQueryKey() })
            void client.invalidateQueries({ queryKey: getGetSuggestedShiftsQueryKey() })
            if (created && (query.data?.total ?? 0) === 0) setSteps(true)
            setEditing(null)
          }} />
      ) : null}
    </>
  )
}
