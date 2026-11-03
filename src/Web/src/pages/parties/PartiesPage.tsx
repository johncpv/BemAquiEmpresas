import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { getExportPartiesUrl, useListParties } from '@/api/generated/bemaqui'
import type { PartyListItem, PartyRoleKind } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ExportMenu } from '@/components/ui/export-menu'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { ROLE_LABELS, ROLE_OPTIONS } from './labels'

/** Cadastros → Terceiros (CAD-01a CA 16 e 17): lista server-side com busca, filtro por papel e inativos na URL. */
export function PartiesPage() {
  const [search] = useTableSearch()
  const { papel, inativos } = useTableFilters(['papel'])
  const role = ROLE_OPTIONS.some((o) => o.value === papel) ? (papel as PartyRoleKind) : undefined
  const filters = { q: search.q, role, includeInactive: inativos === 'true', sort: search.sort }
  const query = useListParties({ ...filters, page: search.page, pageSize: search.pageSize })

  const columns: ColumnDef<PartyListItem>[] = [
    {
      id: 'name', header: 'Nome', meta: { sortField: 'name' }, cell: ({ row: { original: p } }) => (
        <span>
          <span className="block">{p.legalName}</span>
          {p.tradeName ? <span className="block text-xs font-normal text-muted-foreground">{p.tradeName}</span> : null}
        </span>
      ),
    },
    { id: 'document', header: 'Documento', meta: { secondary: true }, cell: ({ row: { original: p } }) => (p.document ? <span className="tabular-nums">{p.document}</span> : <Tag>Sem documento</Tag>) },
    { id: 'roles', header: 'Papéis', cell: ({ row: { original: p } }) => <span className="flex flex-wrap gap-1">{p.roles.map((r) => <Tag key={r}>{ROLE_LABELS[r]}</Tag>)}</span> },
    { id: 'city', header: 'Cidade/UF', meta: { secondary: true }, cell: ({ row: { original: p } }) => (p.city ? `${p.city}/${p.state}` : '—') },
    { id: 'status', header: 'Situação', cell: ({ row: { original: p } }) => <StatusChip tone={p.isActive ? 'ok' : 'neutral'}>{p.isActive ? 'Ativo' : 'Inativo'}</StatusChip> },
  ]

  return (
    <>
      <PageHeading title="Terceiros" description="Clientes, fornecedores, instaladores e transportadoras num cadastro só."
        actions={
          <Can perm="parties.create">
            <Link to="/app/cadastros/terceiros/novo" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo terceiro</Link>
          </Can>
        } />
      <DataTable
        label="Terceiros"
        columns={columns}
        query={query}
        getRowId={(p) => p.id}
        rowHref={(p) => `/app/cadastros/terceiros/${p.id}`}
        searchPlaceholder="Buscar por nome, CNPJ ou CPF completo"
        toolbar={<ExportMenu formats={['xlsx']} fileBase="terceiros" empty={query.data?.total === 0} url={(format) => getExportPartiesUrl({ ...filters, format })} />}
        filters={[{ key: 'papel', label: 'Papel', options: ROLE_OPTIONS }]}
        inactiveFilter
        empty={{
          title: 'Cadastre o primeiro cliente ou fornecedor.',
          description: 'Um mesmo CNPJ pode ser cliente, fornecedor, instalador e transportadora ao mesmo tempo.',
          action: <Can perm="parties.create"><Link to="/app/cadastros/terceiros/novo" className="text-sm font-semibold text-primary">Novo terceiro</Link></Can>,
        }}
      />
    </>
  )
}
