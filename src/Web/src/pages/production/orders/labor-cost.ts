import { useGetOrderLaborCosts } from '@/api/generated/bemaqui'
import type { LaborCostMissingView, LaborCostOrigin, LaborEntryCostView } from '@/api/generated/model'
import { formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'

export const LABOR_COST_DICAS = {
  custoHora: 'Custo do cargo por hora, com encargos; o salário de cada pessoa não aparece.',
  implantacao: 'Custo calculado na implantação, com o custo-hora do cargo daquele dia.',
  semCusto: 'O cargo do funcionário estava sem custo-hora no fechamento: as horas contam, mas o custo ficou zero até recalcular.',
  material: 'Material baixado para a OS pela requisição, pelo custo médio do momento da baixa, menos as devoluções (pelo custo da baixa original) e os estornos.',
}

export const COST_ORIGIN: Record<LaborCostOrigin, string> = {
  Closing: 'Fechamento',
  Correction: 'Correção',
  Recalculation: 'Recálculo',
  Backfill: 'Implantação',
}

/** Custo da OS (PRD-04 CA 14): só com projects.view_cost (sem a permissão, nem consulta: o DTO sem custo não tem o campo). */
export function useOrderLaborCosts(orderId: string) {
  const canSee = useCan('projects.view_cost')
  const query = useGetOrderLaborCosts(orderId, { query: { enabled: canSee } })
  return { canSee, query }
}

/** Marcas do custo do apontamento: "Sem custo-hora" (alerta) e "Calculado na implantação" (padrão 11). */
export function laborCostMarks(c: Pick<LaborEntryCostView, 'costMissing' | 'costBackfilled' | 'pending'>) {
  const marks: string[] = []
  if (!c.pending && c.costMissing) marks.push('Sem custo-hora')
  if (!c.pending && c.costBackfilled) marks.push('Calculado na implantação')
  return marks
}

/** Texto do impacto do recálculo (CA 6): "{n} apontamentos · R$ 0,00 → R$ {total}" (o total só com projects.view_cost). */
export function recalcImpact(m: Pick<LaborCostMissingView, 'recalculableCount' | 'newTotal'>) {
  const n = `${m.recalculableCount} ${m.recalculableCount === 1 ? 'apontamento' : 'apontamentos'}`
  return m.newTotal === null || m.newTotal === undefined ? n : `${n} · ${formatMoney(0)} → ${formatMoney(m.newTotal)}`
}