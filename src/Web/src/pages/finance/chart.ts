import type { AccountType, DreGroup } from '@/api/generated/model'
import type { TreeNode } from '@/components/ui/tree-table'

export const ACCOUNT_TYPES: Record<AccountType, string> = { Revenue: 'Receita', Expense: 'Despesa', Asset: 'Ativo', Liability: 'Passivo' }

export const DRE_GROUPS: Record<DreGroup, string> = {
  GrossRevenue: 'Receita bruta', Deductions: 'Deduções', Costs: 'Custos', OperatingExpenses: 'Despesas operacionais',
  FinancialResult: 'Resultado financeiro', OtherResults: 'Outros resultados', OutsideDre: 'Fora da DRE',
}

export const ANALYTIC_DICA = 'Analítica recebe lançamentos. Sintética agrupa outras contas e só soma: ela vira sintética quando ganha a primeira conta filha.'
export const TO_CLASSIFY_DICA = 'Use só enquanto não souber a conta.'

/** Ordem por segmento numérico, igual à do servidor. */
export const sortKey = (code: string) => code.split('.').map((s) => s.padStart(2, '0')).join('.')

/** Lista plana → árvore da TreeTable; os inativos ficam de fora sem o filtro "Inativas". */
export function toTree<T extends { id: string; code: string; name: string; parentId?: string | null; isActive: boolean }>(all: T[], showInactive: boolean) {
  type Node = TreeNode & { item: T; children?: Node[] }
  const visible = all.filter((x) => showInactive || x.isActive)
  const ids = new Set(visible.map((x) => x.id))
  const build = (parent: string | null): Node[] =>
    visible.filter((x) => (x.parentId && ids.has(x.parentId) ? x.parentId : null) === parent)
      .sort((a, b) => sortKey(a.code).localeCompare(sortKey(b.code)))
      .map((x) => ({ id: x.id, label: `${x.code} ${x.name}`, item: x, children: build(x.id) }))
  return build(null)
}
