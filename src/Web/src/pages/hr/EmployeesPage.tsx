import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { getExportEmployeesUrl, useListEmployees, useListPositions, useListPositionWorkCenters } from '@/api/generated/bemaqui'
import type { EmployeeDetail, EmployeeStatus } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ExportMenu } from '@/components/ui/export-menu'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { EMPLOYEE_EXPORT_NOTE, STATUS_LABELS, STATUS_OPTIONS, STATUS_TONES } from './employee'

/** RH → Funcionários (CAD-04b): lista sem dado pessoal (CA 8), com filtros por situação, cargo e centro na URL. */
export function EmployeesPage() {
  const [search] = useTableSearch()
  const { situacao, cargo, centro } = useTableFilters(['situacao', 'cargo', 'centro'])
  const positions = useListPositions({ includeInactive: true, pageSize: 100 })
  const centers = useListPositionWorkCenters()
  const status = STATUS_OPTIONS.some((o) => o.value === situacao) ? (situacao as EmployeeStatus) : undefined
  const filters = { q: search.q, status, positionId: cargo || undefined, workCenterId: centro || undefined, sort: search.sort }
  const query = useListEmployees({ ...filters, page: search.page, pageSize: search.pageSize })
  const canSensitive = useCan('hr.view_sensitive')

  const columns: ColumnDef<EmployeeDetail>[] = [
    { id: 'name', header: 'Nome', meta: { sortField: 'name' }, cell: ({ row: { original: e } }) => e.name },
    { id: 'registration', header: 'Matrícula', meta: { secondary: true }, cell: ({ row: { original: e } }) => <span className="tabular-nums">{e.registration ?? '—'}</span> },
    { id: 'position', header: 'Cargo', cell: ({ row: { original: e } }) => e.positionName },
    { id: 'center', header: 'Centro', meta: { secondary: true }, cell: ({ row: { original: e } }) => e.workCenterName ?? '—' },
    { id: 'admission', header: 'Admissão', meta: { sortField: 'admissionDate', secondary: true }, cell: ({ row: { original: e } }) => <span className="tabular-nums">{formatDate(e.admissionDate)}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: e } }) => <StatusChip tone={STATUS_TONES[e.status]}>{STATUS_LABELS[e.status]}</StatusChip> },
  ]

  return (
    <>
      <PageHeading title="RH: Funcionários" description="Quem trabalha na empresa e em qual cargo." actions={
        <Can perm="hr.view_sensitive">
          <Can perm="hr.create">
            <Link to="/app/rh/funcionarios/novo" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo funcionário</Link>
          </Can>
        </Can>
      } />
      <DataTable
        label="Funcionários"
        columns={columns}
        query={query}
        getRowId={(e) => e.id}
        rowHref={(e) => `/app/rh/funcionarios/${e.id}`}
        searchPlaceholder="Buscar pelo nome ou matrícula"
        toolbar={<ExportMenu fileBase="funcionarios" formats={['xlsx']} empty={query.data?.total === 0} note={canSensitive ? undefined : EMPLOYEE_EXPORT_NOTE}
          url={(format) => getExportEmployeesUrl({ ...filters, format })} />}
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
