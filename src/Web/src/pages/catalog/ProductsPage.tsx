import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { getExportProductsUrl, useListProductCategories, useListProducts } from '@/api/generated/bemaqui'
import type { ProductListItem } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ExportMenu } from '@/components/ui/export-menu'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { DICAS, formatPercent, marginTone, PRODUCTS } from './catalog'

/**
 * Catálogo → Produtos (CAT-01): busca por nome, PLU e código de barras (também das variações), filtros por categoria e "sem preço" na URL e
 * exportação com os mesmos filtros. O pai de grade aparece uma vez, com a quantidade de variações. Custo e margem só com
 * catalog.view_cost (sem ela, a API nem manda os campos).
 */
export function ProductsPage() {
  const [search] = useTableSearch()
  const { categoria, semPreco, inativos } = useTableFilters(['categoria', 'semPreco'])
  const categories = useListProductCategories({ includeInactive: true })
  const seeCost = useCan('catalog.view_cost')
  const filters = { q: search.q, categoryId: categoria || undefined, withoutPrice: semPreco === 'sim' || undefined, includeInactive: inativos === 'true' || undefined, sort: search.sort }
  const query = useListProducts({ ...filters, page: search.page, pageSize: search.pageSize })

  const columns: ColumnDef<ProductListItem>[] = [
    {
      id: 'name', header: 'Produto', meta: { sortField: 'name' },
      cell: ({ row: { original: p } }) => (
        <span className="flex flex-wrap items-center gap-2">
          <Link to="/app/catalogo/produtos/$id" params={{ id: p.id }} className="font-semibold text-primary hover:underline">{p.saleName}</Link>
          {p.variantCount > 0 ? <StatusChip tone="info">{p.variantCount === 1 ? '1 variação' : `${p.variantCount} variações`}</StatusChip> : null}
        </span>
      ),
    },
    { id: 'category', header: 'Categoria', meta: { secondary: true }, cell: ({ row: { original: p } }) => p.categoryName ?? '—' },
    {
      id: 'code', header: 'Código', meta: { secondary: true },
      cell: ({ row: { original: p } }) => <span className="tabular-nums">{p.firstGtin ?? (p.plu ? `PLU ${p.plu}` : '—')}{p.barcodeCount > 1 ? ` e mais ${p.barcodeCount - 1}` : ''}</span>,
    },
    {
      id: 'price', header: 'Preço', meta: { numeric: true },
      cell: ({ row: { original: p } }) => (p.price == null ? <StatusChip tone="warn">Sem preço</StatusChip> : <span className="tabular-nums">{formatMoney(p.price)}</span>),
    },
    ...(seeCost ? [{
      id: 'margin', header: () => <span className="inline-flex items-center">Margem<Dica nome="Margem" texto={DICAS.margem} /></span>, meta: { secondary: true, numeric: true },
      cell: ({ row: { original: p } }) => (p.cost?.marginPercent == null ? '—' : <StatusChip tone={marginTone(p.cost.marginPercent)}>{formatPercent(p.cost.marginPercent)}</StatusChip>),
    } as ColumnDef<ProductListItem>] : []),
    { id: 'status', header: 'Situação', cell: ({ row: { original: p } }) => <StatusChip tone={p.isActive ? 'ok' : 'neutral'}>{p.isActive ? 'Ativo' : 'Inativo'}</StatusChip> },
  ]

  const newLink = (primary: boolean) => (
    <Can perm="catalog.create">
      <Link to="/app/catalogo/produtos/novo" className={primary ? 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'inline-flex min-h-11 items-center text-sm font-semibold text-primary'}>Novo produto</Link>
    </Can>
  )
  return (
    <>
      <PageHeading title="Catálogo: Produtos" description="O que a loja vende: código de barras, preço e categoria de cada produto."
        actions={newLink(true)} />
      <DataTable
        label="Produtos"
        columns={columns}
        query={query}
        getRowId={(p) => p.id}
        rowHref={(p) => `${PRODUCTS}/${p.id}`}
        inactiveFilter
        searchPlaceholder="Buscar pelo nome, PLU ou código de barras"
        toolbar={<ExportMenu formats={['xlsx', 'pdf']} fileBase="produtos" empty={query.data?.total === 0} url={(format) => getExportProductsUrl({ ...filters, format })} />}
        filters={[
          { key: 'categoria', label: 'Categoria', options: (categories.data ?? []).map((c) => ({ value: c.id, label: c.isActive ? c.name : `${c.name} (inativa)` })) },
          { key: 'semPreco', label: 'Preço', options: [{ value: 'sim', label: 'Só os sem preço' }] },
        ]}
        empty={{
          title: 'Nenhum produto ainda.',
          description: 'Cadastre o que você vende: o nome, o código de barras e o preço. O item do estoque nasce junto, sem digitar duas vezes.',
          action: newLink(false),
        }}
      />
    </>
  )
}
