import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { getExportInstallationOrdersUrl, useListInstallations } from '@/api/generated/bemaqui'
import type { InstallationSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ExportMenu } from '@/components/ui/export-menu'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { INSTALLATION_SITUATIONS, INSTALLATION_STATUS } from './installations'

/** Fábrica → Instalações (COM-06): OS de instalação com projeto, instalador, previsão, etapa e situação. */
export function InstallationsPage() {
  const [search] = useTableSearch()
  const { situacao } = useTableFilters(['situacao'])
  const query = useListInstallations({ situation: situacao || undefined, page: search.page, pageSize: search.pageSize })
  const columns: ColumnDef<InstallationSummary>[] = [
    { id: 'number', header: 'OS', cell: ({ row: { original: o } }) => <Link to={'/app/producao/instalacoes/$id' as never} params={{ id: o.id } as never} className="font-semibold text-primary hover:underline">{o.number}</Link> },
    { id: 'project', header: 'Projeto', cell: ({ row: { original: o } }) => <span>{o.projectNumber}<span className="block text-xs text-muted-foreground">{o.projectName}</span></span> },
    { id: 'installer', header: 'Instalador', meta: { secondary: true }, cell: ({ row: { original: o } }) => o.installerName },
    { id: 'planned', header: 'Previsão', cell: ({ row: { original: o } }) => <span className="tabular-nums">{formatDate(o.plannedStart)} a {formatDate(o.plannedEnd)}</span> },
    { id: 'steps', header: 'Etapas', meta: { numeric: true, secondary: true }, cell: ({ row: { original: o } }) => <span className="tabular-nums">{o.stepsDone}/{o.stepsTotal}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: o } }) => <StatusChip tone={INSTALLATION_STATUS[o.status]?.tone ?? 'neutral'}>{INSTALLATION_STATUS[o.status]?.label ?? o.status}</StatusChip> },
  ]
  const goProjects = <Link to={'/app/projetos' as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver projetos</Link>
  return (
    <>
      <PageHeading title="Produção: Instalações" description="A instalação é feita pelo instalador terceirizado; o aceite técnico libera a parcela do cliente." />
      <DataTable label="OS de instalação" columns={columns} query={query} getRowId={(o) => o.id}
        toolbar={<ExportMenu formats={['xlsx']} fileBase="os-de-instalacao" empty={query.data?.total === 0} url={(format) => getExportInstallationOrdersUrl({ situation: situacao || undefined, format })} />}
        filters={[{ key: 'situacao', label: 'Situação', options: INSTALLATION_SITUATIONS }]}
        empty={{ title: 'Crie a OS de instalação a partir do projeto.', action: goProjects }} />
    </>
  )
}
