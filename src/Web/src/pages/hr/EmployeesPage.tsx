import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListEmployees, useListPositions, useListPositionWorkCenters } from '@/api/generated/bemaqui'
import type { EmployeeDetail, EmployeeStatus } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { STATUS_LABELS, STATUS_OPTIONS } from './employee'
import { HrTabs } from './HrTabs'

/** RH → Funcionários (CAD-04b): lista sem dado pessoal (CA 8), com filtros por situação, cargo e centro na URL. */
export function EmployeesPage() {
  const [search] = useTableSearch()
  const { situacao, cargo, centro } = useTableFilters(['situacao', 'cargo', 'centro'])
  const positions = useListPositions({ includeInactive: true, pageSize: 100 })
  const centers = useListPositionWorkCenters()
  const status = STATUS_OPTIONS.some((o) => o.value === situacao) ? (situacao as EmployeeStatus) : undefined
  const query = useListEmployees({ q: search.q, status, positionId: cargo || undefined, workCenterId: centro || undefined, page: search.page, pageSize: search.pageSize, sort: search.sort })

  const columns: ColumnDef<EmployeeDetail>[] = [
    { id: 'name', header: 'Nome', meta: { sortField: 'name' }, cell: ({ row: { original: e } }) => e.name },
    { id: 'registration', header: 'Matrícula', meta: { secondary: true }, cell: ({ row: { original: e } }) => <span className="tabular-nums">{e.registration ?? '—'}</span> },
    { id: 'position', header: 'Cargo', cell: ({ row: { original: e } }) => e.positionName },
    { id: 'center', header: 'Centro', meta: { secondary: true }, cell: ({ row: { original: e } }) => e.workCenterName ?? '—' },
    { id: 'admission', header: 'Admissão', meta: { sortField: 'admissionDate', secondary: true }, cell: ({ row: { original: e } }) => <span className="tabular-nums">{formatDate(e.admissionDate)}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: e } }) => <StatusChip tone={e.status === 'Active' ? 'ok' : 'neutral'}>{STATUS_LABELS[e.status]}</StatusChip> },
  ]

  return (
    <>
      <PageHeading title="RH: Funcionários">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Quem trabalha na empresa e em qual cargo.</p>
          <Can perm="hr.view_sensitive">
            <Can perm="hr.create">
              <Link to="/app/rh/funcionarios/novo" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo funcionário</Link>
            </Can>
          </Can>
        </div>
      </PageHeading>
      <HrTabs />
      <DataTable
        label="Funcionários"
        columns={columns}
        query={query}
        getRowId={(e) => e.id}
        rowHref={(e) => `/app/rh/funcionarios/${e.id}`}
        searchPlaceholder="Buscar pelo nome ou matrícula"
        filters={[
          { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS },
          { key: 'cargo', label: 'Cargo', options: (positions.data?.items ?? []).map((p) => ({ value: p.id, label: p.name })) },
          { key: 'centro', label: 'Centro', options: (centers.data ?? []).map((c) => ({ value: c.id, label: c.name })) },
        ]}
        empty={{
          title: 'Cadastre os funcionários para apontar horas e preparar a folha.',
          description: 'Comece pelos cargos: o cargo sugere a jornada e o salário.',
          action: <Can perm="hr.view_sensitive"><Can perm="hr.create"><Link to="/app/rh/funcionarios/novo" className="text-sm font-semibold text-primary">Novo funcionário</Link></Can></Can>,
        }}
      />
    </>
  )
}
