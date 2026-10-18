import { useNavigate, useSearch } from '@tanstack/react-router'
import { useCallback } from 'react'

/** Mesmos limites do PageRequest do backend (FND-07): 20 por página, no máximo 100. */
export const DEFAULT_PAGE_SIZE = 20
export const MAX_PAGE_SIZE = 100

export interface TableSearch {
  page: number
  pageSize: number
  /** "campo" (crescente) ou "-campo" (decrescente), o formato do PageRequest.Sort. */
  sort?: string
  q?: string
}

const positive = (value: unknown, fallback: number, max = Number.MAX_SAFE_INTEGER) => {
  const n = Number(value)
  return Number.isInteger(n) && n > 0 ? Math.min(n, max) : fallback
}

export function parseTableSearch(search: Record<string, unknown>): TableSearch {
  return {
    page: positive(search.page, 1),
    pageSize: positive(search.pageSize, DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE),
    sort: typeof search.sort === 'string' && search.sort ? search.sort : undefined,
    q: typeof search.q === 'string' && search.q ? search.q : undefined,
  }
}

/** Estado da tabela na URL (FND-09c CA 4): filtro, ordenação e página viram link compartilhável. */
export function useTableSearch(): [TableSearch, (patch: Partial<TableSearch>) => void] {
  const raw = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const set = useCallback(
    (patch: Partial<TableSearch>) => {
      void navigate({
        to: '.',
        replace: true,
        search: (prev: Record<string, unknown>) => {
          const next = { ...prev, ...patch } as Record<string, unknown>
          for (const key of Object.keys(next)) if (next[key] === undefined || next[key] === '') delete next[key]
          if (next.page === 1) delete next.page
          if (next.pageSize === DEFAULT_PAGE_SIZE) delete next.pageSize
          return next
        },
      } as never)
    },
    [navigate],
  )
  return [parseTableSearch(raw), set]
}

/** Valores dos filtros da URL, para a consulta da tela (as mesmas chaves de `filters`, mais `inativos`). */
export function useTableFilters(keys: string[]): Record<string, string | undefined> {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  return Object.fromEntries([...keys, 'inativos'].map((k) => [k, ['string', 'number', 'boolean'].includes(typeof search[k]) ? String(search[k]) : undefined]))
}
