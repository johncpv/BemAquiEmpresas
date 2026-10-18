import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListTenants } from '@/api/generated/bemaqui'
import type { TenantRow } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { ProvisionDialog } from './ProvisionDialog'
import { TENANT_STATUS } from './status'

const columns: ColumnDef<TenantRow>[] = [
  { id: 'nome', header: 'Empresa', meta: { sortField: 'nome' }, cell: ({ row: { original: t } }) => <Link to="/plataforma/tenants/$key" params={{ key: t.key }} className="font-semibold text-primary">{t.tradeName || t.legalName}</Link> },
  { accessorKey: 'document', header: 'CNPJ', meta: { secondary: true }, cell: (c) => <span className="tabular-nums">{c.getValue<string | null>() ?? '—'}</span> },
  { accessorKey: 'primaryHost', header: 'Endereço', meta: { secondary: true } },
  { accessorKey: 'plan', header: 'Plano', meta: { secondary: true } },
  { accessorKey: 'status', header: 'Situação', cell: (c) => { const [tone, label] = TENANT_STATUS[c.getValue<keyof typeof TENANT_STATUS>()] ?? TENANT_STATUS.Active; return <StatusChip tone={tone}>{label}</StatusChip> } },
  { accessorKey: 'schemaVersion', header: 'Versão do banco', meta: { secondary: true }, cell: (c) => <code className="text-xs">{c.getValue<string | null>()?.slice(0, 14) ?? '—'}</code> },
  { accessorKey: 'createdAt', header: 'Criado em', meta: { sortField: 'criacao', secondary: true }, cell: (c) => <span className="tabular-nums">{formatDateTime(c.getValue<string>())}</span> },
]

/** Tenants (FND-03b CA 1 e 9): lista paginada no servidor com busca por nome, CNPJ ou endereço. */
export function TenantsPage() {
  const [search] = useTableSearch()
  const query = useListTenants({ page: search.page, pageSize: search.pageSize, sort: search.sort, q: search.q })
  return (
    <>
      <PageHeading title="Tenants">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">As empresas que usam o BemAqui Empresas.</p>
          <ProvisionDialog />
        </div>
      </PageHeading>
      <DataTable label="Tenants" columns={columns} query={query} getRowId={(t) => t.id} searchPlaceholder="Buscar por nome, CNPJ ou endereço" empty={{ title: 'Nenhum tenant ainda.', action: <ProvisionDialog /> }} />
    </>
  )
}
