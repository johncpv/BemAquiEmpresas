import type { CostType, ProjectMarginLine } from '@/api/generated/model'
import { COST_TYPES } from './margins/margins'

/** Valores do diálogo "Editar custo orçado" (COM-09): string normalizada por natureza ("1234.56"), como o campo de dinheiro entrega. */
export type BudgetDraft = Record<CostType, string | null>

/** O orçado de hoje, natureza por natureza (as seis sempre aparecem; sem linha, zero). */
export function budgetDraft(lines: Pick<ProjectMarginLine, 'costType' | 'budgeted'>[]): BudgetDraft {
  return Object.fromEntries(COST_TYPES.map(({ type }) => [type, (lines.find((l) => l.costType === type)?.budgeted ?? 0).toFixed(2)])) as BudgetDraft
}

/** Só as naturezas que mudaram vão para a API (as demais ficam como estão); campo vazio conta como inválido, não como zero. */
export function budgetChanges(lines: Pick<ProjectMarginLine, 'costType' | 'budgeted'>[], draft: BudgetDraft) {
  const before = budgetDraft(lines)
  return COST_TYPES.filter(({ type }) => draft[type] !== null && Number(draft[type]) !== Number(before[type])).map(({ type }) => ({ costType: type, amount: Number(draft[type]) }))
}

export const budgetTotal = (draft: BudgetDraft) => COST_TYPES.reduce((sum, { type }) => sum + Number(draft[type] ?? 0), 0)

/** Margem orçada com o total informado; sem receita ou sem custo orçado, não existe (COM-09, mesma regra do "Sem orçamento de custo"). */
export const budgetedMarginPct = (revenue: number, total: number) => (revenue > 0 && total > 0 ? ((revenue - total) / revenue) * 100 : null)

/** Projeto encerrado ou cancelado não tem o custo orçado editado (o servidor recusa com 409). */
export const budgetLocked = (status: string | null | undefined) => status === 'Closed' || status === 'Cancelled'
