import { useNavigate, useSearch } from '@tanstack/react-router'
import type { ProjectResultLine } from '@/api/generated/model'
import type { TreeNode } from '@/components/ui/tree-table'
import { addMonths, currentMonth } from '@/pages/finance/dre/dre'

const KEYS = ['situacao', 'cliente', 'clienteNome', 'de', 'ate'] as const
export type ResultFilters = Partial<Record<(typeof KEYS)[number], string>>

/** Filtros na URL (situação, cliente e período, padrão os 12 meses até o mês corrente). */
export function useResultFilters(): [ResultFilters, (patch: ResultFilters) => void, { from: string; to: string }] {
  const raw = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const filters = Object.fromEntries(KEYS.map((k) => [k, typeof raw[k] === 'string' ? (raw[k] as string) : undefined])) as ResultFilters
  const to = filters.ate ?? currentMonth().slice(0, 7)
  const from = filters.de ?? addMonths(`${to}-01`, -11).slice(0, 7)
  const set = (patch: ResultFilters) => void navigate({
    to: '.', replace: true,
    search: (prev: Record<string, unknown>) => {
      const next: Record<string, unknown> = { ...prev, ...patch }
      delete next.page
      for (const k of Object.keys(next)) if (next[k] === undefined || next[k] === '') delete next[k]
      return next
    },
  } as never)
  return [filters, set, { from, to }]
}

export interface LineNode extends TreeNode {
  line: ProjectResultLine
  children?: LineNode[]
}

/** Receita; Custos diretos com as naturezas abaixo; = Resultado (CA 7). */
export function toTree(lines: readonly ProjectResultLine[]): LineNode[] {
  const node = (l: ProjectResultLine, children?: LineNode[]): LineNode => ({ id: l.key, label: l.label, line: l, children })
  const revenue = lines.find((l) => l.key === 'revenue')
  const costs = lines.find((l) => l.key === 'costs')
  const result = lines.find((l) => l.key === 'result')
  const natures = lines.filter((l) => l.kind === 'Cost').map((l) => node(l))
  return [...(revenue ? [node(revenue)] : []), ...(costs ? [node(costs, natures)] : []), ...(result ? [node(result)] : [])]
}
