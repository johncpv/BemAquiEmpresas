import { type ColumnDef, flexRender, getCoreRowModel, type RowData, useReactTable } from '@tanstack/react-table'
import { ArrowDown, ArrowUp, ChevronDown, ChevronRight, Search } from 'lucide-react'
import { useNavigate } from '@tanstack/react-router'
import { Fragment, type ReactNode, useEffect, useState } from 'react'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { cn } from '@/lib/utils'
import { useTableFilters, useTableSearch } from './table-search'

declare module '@tanstack/react-table' {
  interface ColumnMeta<TData extends RowData, TValue> {
    /** Some a 400px (FND-09c CA 7). */
    secondary?: boolean
    /** Campo enviado em sort; sem ele, a coluna não ordena. */
    sortField?: string
    numeric?: boolean
    /** Só para o TypeScript usar os parâmetros genéricos. */
    _types?: [TData, TValue]
  }
}

/** Mesmo formato do PagedList do backend (FND-07). */
export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface TableQuery<T> {
  data?: Page<T>
  isPending: boolean
  isError: boolean
  error?: unknown
  refetch: () => unknown
}

interface DataTableProps<T> {
  // any: o valor de cada coluna tem tipo próprio (padrão do TanStack Table).
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  columns: ColumnDef<T, any>[]
  query: TableQuery<T>
  empty: { title: string; description?: string; action?: ReactNode }
  label: string
  searchPlaceholder?: string
  getRowId?: (row: T) => string
  /** Filtros na URL (FND-09d CA 19); o valor vazio é "Todos". */
  filters?: TableFilter[]
  /** Liga o filtro "Mostrar inativos" (?inativos=true). */
  inactiveFilter?: boolean
  /** A primeira coluna vira link para o detalhe (Enter abre). */
  rowHref?: (row: T) => string
  /** Sem busca de texto quando o servidor não filtra por texto (ex.: execuções da importação). */
  searchable?: boolean
  /** Seleção por checkbox (FND-09e CA 10), pelos ids de getRowId; `rowLabel` dá o nome acessível de cada linha. */
  selectable?: { selected: string[]; onChange: (ids: string[]) => void; rowLabel: (row: T) => string }
  /** Rodapé de totais do filtro inteiro (vindo do servidor), por id de coluna; a primeira célula diz "Total do filtro". */
  totals?: Record<string, ReactNode>
  /** Linha expansível por teclado (aria-expanded), com o conteúdo abaixo dela. */
  expandable?: { render: (row: T) => ReactNode; label: (row: T) => string }
  /** Classe extra da linha (ex.: faixa lateral vermelha do vencido, padrão 6 da Fase 2). */
  rowClassName?: (row: T) => string | undefined
}

export interface TableFilter {
  key: string
  label: string
  options: { value: string; label: string }[]
}


const cellClass = (secondary?: boolean, numeric?: boolean) =>
  cn('px-3 py-2 text-left', secondary && 'hidden sm:table-cell', numeric && 'text-right tabular-nums')

/**
 * Tabela padrão (FND-09c CA 4 a 7): paginação, ordenação e busca no servidor, refletidas na URL; estados de carregando,
 * vazio, vazio por filtro e erro; colunas secundárias somem a 400px.
 */
export function DataTable<T>({ columns, query, empty, label, searchPlaceholder = 'Buscar', getRowId, filters = [], inactiveFilter, rowHref, searchable = true, selectable, totals, expandable, rowClassName }: DataTableProps<T>) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set())
  const [search, setSearch] = useTableSearch()
  const navigate = useNavigate()
  const values = useTableFilters(filters.map((f) => f.key))
  const setFilter = (key: string, value: string | boolean | undefined) => setSearch({ [key]: value || undefined, page: 1 } as never)
  const filtered = !!search.q || Object.values(values).some(Boolean)
  const [text, setText] = useState(search.q ?? '')

  useEffect(() => {
    const t = setTimeout(() => {
      if ((search.q ?? '') !== text) setSearch({ q: text || undefined, page: 1 })
    }, 300)
    return () => clearTimeout(t)
  }, [text, search.q, setSearch])

  const data = query.data
  // O TanStack Table v8 não é compatível com o React Compiler (não usado aqui); a tabela é recriada a cada render.
  // oxlint-disable-next-line react/incompatible-library
  const table = useReactTable({ data: data?.items ?? [], columns, getRowId, manualPagination: true, manualSorting: true, rowCount: data?.total ?? 0, getCoreRowModel: getCoreRowModel() })
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  const pageIds = table.getRowModel().rows.map((r) => r.id)
  const extraColumns = (expandable ? 1 : 0) + (selectable ? 1 : 0)

  const toggleSort = (field: string) =>
    setSearch({ sort: search.sort === field ? '-' + field : search.sort === '-' + field ? undefined : field, page: 1 })
  const clearFilters = () => {
    setText('')
    setSearch({ q: undefined, page: 1, ...Object.fromEntries(Object.keys(values).map((k) => [k, undefined])) } as never)
  }

  let body: ReactNode
  if (query.isPending) {
    body = (
      <div aria-label="Carregando" className="space-y-2 p-3">
        {Array.from({ length: 5 }, (_, i) => <Skeleton key={i} className="h-8" />)}
      </div>
    )
  } else if (query.isError) {
    body = <ErrorState message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => query.refetch()} className="m-3" />
  } else if (!data || data.items.length === 0) {
    body = filtered ? (
      <EmptyState title="Nenhum resultado para os filtros." className="m-3" action={<button type="button" onClick={clearFilters} className="text-sm font-semibold text-primary">Limpar filtros</button>} />
    ) : (
      <EmptyState {...empty} className="m-3" />
    )
  } else {
    body = (
      <div className="overflow-x-auto">
        <table className="w-full border-collapse text-sm" aria-label={label}>
          <thead className="border-b border-border text-xs text-muted-foreground">
            {table.getHeaderGroups().map((group) => (
              <tr key={group.id}>
                {expandable ? <th scope="col" className="w-8 px-2"><span className="sr-only">Detalhes</span></th> : null}
                {selectable ? (
                  <th scope="col" className="w-8 px-3">
                    <input type="checkbox" aria-label="Selecionar todos desta página" className="size-4 accent-primary" checked={pageIds.length > 0 && pageIds.every((id) => selectable.selected.includes(id))}
                      onChange={(e) => selectable.onChange(e.target.checked ? [...new Set([...selectable.selected, ...pageIds])] : selectable.selected.filter((id) => !pageIds.includes(id)))} />
                  </th>
                ) : null}
                {group.headers.map((header) => {
                  const meta = header.column.columnDef.meta
                  const field = meta?.sortField
                  const state = field ? (search.sort === field ? 'ascending' : search.sort === '-' + field ? 'descending' : undefined) : undefined
                  const title = flexRender(header.column.columnDef.header, header.getContext())
                  return (
                    <th key={header.id} scope="col" aria-sort={state} className={cn(cellClass(meta?.secondary, meta?.numeric), 'font-semibold')}>
                      {field ? (
                        <button type="button" onClick={() => toggleSort(field)} className="inline-flex items-center gap-1">
                          {title}
                          {state === 'ascending' ? <ArrowUp aria-hidden="true" className="size-3" /> : null}
                          {state === 'descending' ? <ArrowDown aria-hidden="true" className="size-3" /> : null}
                        </button>
                      ) : (
                        title
                      )}
                    </th>
                  )
                })}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.map((row) => (
              <Fragment key={row.id}>
              <tr className={cn('border-b border-border last:border-0', rowClassName?.(row.original))}>
                {expandable ? (
                  <td className="px-2">
                    <button type="button" aria-expanded={expanded.has(row.id)} aria-controls={`${row.id}-detalhe`} aria-label={`Detalhes de ${expandable.label(row.original)}`} onClick={() => setExpanded((s) => { const n = new Set(s); if (n.has(row.id)) n.delete(row.id); else n.add(row.id); return n })} className="grid size-7 place-items-center rounded-sm">
                      {expanded.has(row.id) ? <ChevronDown aria-hidden="true" className="size-4" /> : <ChevronRight aria-hidden="true" className="size-4" />}
                    </button>
                  </td>
                ) : null}
                {selectable ? (
                  <td className="px-3">
                    <input type="checkbox" aria-label={`Selecionar ${selectable.rowLabel(row.original)}`} className="size-4 accent-primary" checked={selectable.selected.includes(row.id)}
                      onChange={(e) => selectable.onChange(e.target.checked ? [...selectable.selected, row.id] : selectable.selected.filter((id) => id !== row.id))} />
                  </td>
                ) : null}
                {row.getVisibleCells().map((c, i) => {
                  const content = flexRender(c.column.columnDef.cell, c.getContext())
                  const href = i === 0 && rowHref ? rowHref(row.original) : undefined
                  return (
                    <td key={c.id} className={cellClass(c.column.columnDef.meta?.secondary, c.column.columnDef.meta?.numeric)}>
                      {href ? (
                        <a href={href} onClick={(e) => { if (e.metaKey || e.ctrlKey || e.shiftKey) return; e.preventDefault(); void navigate({ to: href } as never) }} className="font-semibold text-primary hover:underline">
                          {content}
                        </a>
                      ) : content}
                    </td>
                  )
                })}
              </tr>
              {expandable && expanded.has(row.id) ? (
                <tr id={`${row.id}-detalhe`} className="border-b border-border bg-muted/40">
                  <td colSpan={extraColumns + row.getVisibleCells().length} className="px-3 py-2">{expandable.render(row.original)}</td>
                </tr>
              ) : null}
              </Fragment>
            ))}
          </tbody>
          {totals ? (
            <tfoot className="border-t-2 border-border font-semibold">
              <tr>
                {extraColumns > 0 ? <td colSpan={extraColumns} /> : null}
                {table.getVisibleLeafColumns().map((col, i) => (
                  <td key={col.id} className={cellClass(col.columnDef.meta?.secondary, col.columnDef.meta?.numeric)}>{totals[col.id] ?? (i === 0 ? 'Total do filtro' : null)}</td>
                ))}
              </tr>
            </tfoot>
          ) : null}
        </table>
      </div>
    )
  }

  return (
    <div className="rounded-md border border-border bg-card">
      {searchable ? (
        <div className="flex items-center gap-2 border-b border-border p-2">
          <Search aria-hidden="true" className="ml-1 size-4 text-muted-foreground" />
          <input type="search" value={text} onChange={(e) => setText(e.target.value)} placeholder={searchPlaceholder} aria-label={searchPlaceholder} className="w-full bg-transparent py-1 text-sm outline-none" />
        </div>
      ) : null}
      {filters.length > 0 || inactiveFilter ? (
        <div className="flex flex-wrap items-center gap-2 border-b border-border px-2 py-2">
          {filters.map((f) => (
            <label key={f.key} className="inline-flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
              {f.label}
              <select value={values[f.key] ?? ''} onChange={(e) => setFilter(f.key, e.target.value)} className="rounded-md border border-input bg-card px-2 py-1 text-sm text-foreground">
                <option value="">Todos</option>
                {f.options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </label>
          ))}
          {inactiveFilter ? (
            <label className="inline-flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
              <input type="checkbox" checked={values.inativos === 'true'} onChange={(e) => setFilter('inativos', e.target.checked || undefined)} className="size-4 accent-primary" />
              Mostrar inativos
            </label>
          ) : null}
        </div>
      ) : null}
      {selectable ? (
        <p aria-live="polite" className={cn('px-3 py-2 text-sm', selectable.selected.length === 0 && 'sr-only')}>
          {selectable.selected.length === 1 ? '1 selecionado' : `${selectable.selected.length} selecionados`}
        </p>
      ) : null}
      {body}
      {data && data.total > 0 ? (
        <div className="flex flex-wrap items-center justify-between gap-2 border-t border-border px-3 py-2 text-xs text-muted-foreground">
          <span className="tabular-nums">
            Página {data.page} de {pages} · {data.total.toLocaleString('pt-BR')} {data.total === 1 ? 'registro' : 'registros'}
          </span>
          <span className="flex gap-1">
            <button type="button" disabled={data.page <= 1} onClick={() => setSearch({ page: data.page - 1 })} className="rounded-sm border border-border px-2 py-1 disabled:opacity-50">Anterior</button>
            <button type="button" disabled={data.page >= pages} onClick={() => setSearch({ page: data.page + 1 })} className="rounded-sm border border-border px-2 py-1 disabled:opacity-50">Próxima</button>
          </span>
        </div>
      ) : null}
    </div>
  )
}
