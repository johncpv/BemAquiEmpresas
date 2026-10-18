import { useMemo } from 'react'
import type { Page, TableQuery } from './DataTable'
import { useTableSearch } from './table-search'

/**
 * Lista pequena que a API devolve inteira (ex.: usuários do tenant): página, ordenação e busca no cliente, com o
 * mesmo contrato do DataTable (URL compartilhável).
 */
export function useClientPage<T>(query: { data?: T[]; isPending: boolean; isError: boolean; error?: unknown; refetch: () => unknown }, text: (row: T) => string, sortValue: (row: T, field: string) => string | number): TableQuery<T> {
  const [search] = useTableSearch()
  return useMemo(() => {
    if (!query.data) return { isPending: query.isPending, isError: query.isError, error: query.error, refetch: query.refetch }
    let rows = query.data.filter((r) => !search.q || text(r).toLowerCase().includes(search.q.toLowerCase()))
    if (search.sort) {
      const desc = search.sort.startsWith('-')
      const field = search.sort.replace(/^-/, '')
      rows = [...rows].sort((a, b) => {
        const x = sortValue(a, field)
        const y = sortValue(b, field)
        return (x < y ? -1 : x > y ? 1 : 0) * (desc ? -1 : 1)
      })
    }

    const data: Page<T> = { items: rows.slice((search.page - 1) * search.pageSize, search.page * search.pageSize), page: search.page, pageSize: search.pageSize, total: rows.length }
    return { data, isPending: false, isError: false, refetch: query.refetch }
  }, [query.data, query.isPending, query.isError, query.error, query.refetch, search, text, sortValue])
}
