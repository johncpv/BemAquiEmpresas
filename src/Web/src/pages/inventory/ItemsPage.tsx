import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { Barcode, Layers } from 'lucide-react'
import { getExportItemsUrl, useListItemCategories, useListItems } from '@/api/generated/bemaqui'
import type { ItemListItem, ItemType } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ExportMenu } from '@/components/ui/export-menu'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { formatNcm, TYPE_LABELS, TYPE_OPTIONS } from './item-labels'

const quantity = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 4 })

/** Estoque → Itens (CAD-02a CA 19): lista server-side com busca por código ou nome e filtros por tipo, categoria e situação na URL. */
export function ItemsPage() {
  const [search] = useTableSearch()
  const { tipo, categoria, inativos } = useTableFilters(['tipo', 'categoria'])
  const categories = useListItemCategories()
  const type = TYPE_OPTIONS.some((o) => o.value === tipo) ? (tipo as ItemType) : undefined
  const filters = { q: search.q, type, categoryId: categoria || undefined, includeInactive: inativos === 'true', sort: search.sort }
  const query = useListItems({ ...filters, page: search.page, pageSize: search.pageSize })

  const columns: ColumnDef<ItemListItem>[] = [
    { id: 'code', header: 'Código', meta: { sortField: 'code' }, cell: ({ row: { original: i } }) => <span className="font-mono">{i.code}</span> },
    { id: 'name', header: 'Nome', meta: { sortField: 'name' }, cell: ({ row: { original: i } }) => i.name },
    { id: 'type', header: 'Tipo', cell: ({ row: { original: i } }) => TYPE_LABELS[i.type] },
    { id: 'category', header: 'Categoria', meta: { secondary: true }, cell: ({ row: { original: i } }) => i.categoryName ?? '—' },
    { id: 'unit', header: 'Unidade', cell: ({ row: { original: i } }) => i.unitCode },
    { id: 'ncm', header: 'NCM', meta: { secondary: true }, cell: ({ row: { original: i } }) => <span className="tabular-nums">{formatNcm(i.ncm) || '—'}</span> },
    {
      id: 'tracking', header: 'Lote/série', meta: { secondary: true }, cell: ({ row: { original: i } }) => (
        <span className="flex gap-2 text-xs">
          {i.tracksLot ? <span className="inline-flex items-center gap-1"><Layers aria-hidden="true" className="size-3.5" />Lote</span> : null}
          {i.tracksSerial ? <span className="inline-flex items-center gap-1"><Barcode aria-hidden="true" className="size-3.5" />Série</span> : null}
          {!i.tracksLot && !i.tracksSerial ? '—' : null}
        </span>
      ),
    },
    { id: 'min', header: 'Mínimo', meta: { numeric: true, secondary: true }, cell: ({ row: { original: i } }) => <span className="tabular-nums">{quantity.format(i.minStock)}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: i } }) => <StatusChip tone={i.isActive ? 'ok' : 'neutral'}>{i.isActive ? 'Ativo' : 'Inativo'}</StatusChip> },
  ]

  return (
    <>
      <PageHeading title="Estoque: Itens" description="Matérias-primas, componentes, produtos e serviços."
        actions={
          <>
            <Can perm="inventory.create">
              <Link to="/app/estoque/itens/novo" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo item</Link>
            </Can>
          </>
        } />
      <DataTable
        label="Itens"
        columns={columns}
        query={query}
        getRowId={(i) => i.id}
        rowHref={(i) => `/app/estoque/itens/${i.id}`}
        searchPlaceholder="Buscar pelo código ou nome"
        toolbar={<ExportMenu formats={['xlsx']} fileBase="itens" empty={query.data?.total === 0} url={(format) => getExportItemsUrl({ ...filters, format })} />}
        filters={[
          { key: 'tipo', label: 'Tipo', options: TYPE_OPTIONS },
          { key: 'categoria', label: 'Categoria', options: (categories.data ?? []).map((c) => ({ value: c.id, label: c.name })) },
        ]}
        inactiveFilter
        empty={{
          title: 'Cadastre o primeiro item: matéria-prima, componente ou produto acabado.',
          description: 'Use o mesmo código do sistema antigo. O tipo sugere o lote, a série e a garantia.',
          action: <Can perm="inventory.create"><Link to="/app/estoque/itens/novo" className="text-sm font-semibold text-primary">Novo item</Link></Can>,
        }}
      />
    </>
  )
}
