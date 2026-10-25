import type { CostType, ProjectCostEntryKind, ProjectMarginFigures } from '@/api/generated/model'
import { formatMoney } from '@/lib/format'

/** Naturezas de custo do projeto, na ordem da tela (DSH-02 CA 1). */
export const COST_TYPES: { type: CostType; label: string }[] = [
  { type: 'Material', label: 'Material' },
  { type: 'Labor', label: 'Mão de obra' },
  { type: 'Installation', label: 'Instalação' },
  { type: 'Freight', label: 'Frete' },
  { type: 'Tax', label: 'Impostos' },
  { type: 'Other', label: 'Outros' },
]

export const costLabel = (type: string | null | undefined) => COST_TYPES.find((c) => c.type === type)?.label ?? type ?? ''

/** Tipo de origem de um lançamento do drill-down (CA 13 e 15). */
export const ENTRY_KIND: Record<ProjectCostEntryKind, string> = {
  StockIssue: 'Baixa para OS',
  StockReturn: 'Devolução',
  Labor: 'Apontamento',
  LaborCorrection: 'Correção de apontamento',
  Payable: 'Título a pagar',
  PurchasePayable: 'Título de compra',
  StockIssuedOffset: 'Baixas para as OS',
  InvoiceTax: 'Imposto da nota',
  InvoiceTaxReversal: 'Estorno de imposto',
  Contract: 'Orçamento do contrato',
  Amendment: 'Aditivo',
  Project: 'Projeto',
}

export const COLUMN_LABEL = { budgeted: 'Orçado', realized: 'Realizado', committed: 'Comprometido' } as const
export type CostColumn = keyof typeof COLUMN_LABEL

/** Percentual com 1 casa na tela (o cálculo tem 4, CA 6); null → "—". */
export const pct1 = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${v.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`)

/** Pontos percentuais com sinal ("−3,6 pp"). */
export const pp = (v: number | null | undefined) => {
  if (v === null || v === undefined) return '—'
  const abs = Math.abs(v).toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
  return `${v < 0 ? '−' : v > 0 ? '+' : ''}${abs} pp`
}

/** Chip da margem (CA 11): "Prejuízo projetado" (crítico) quando a projetada é negativa; "Margem {x} pp abaixo" (alerta) quando cai. */
export function marginChip(f: Pick<ProjectMarginFigures, 'projectedMarginPct' | 'marginDeviationPp'>): { tone: 'crit' | 'warn'; text: string } | null {
  if (typeof f.projectedMarginPct === 'number' && f.projectedMarginPct < 0) return { tone: 'crit', text: 'Prejuízo projetado' }
  if (typeof f.marginDeviationPp === 'number' && f.marginDeviationPp < 0) {
    return { tone: 'warn', text: `Margem ${Math.abs(f.marginDeviationPp).toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} pp abaixo` }
  }
  return null
}

/** Linha de contexto do Kpi de margem ("−3,6 pp abaixo dos 23,0% orçados"). */
export function marginContext(f: Pick<ProjectMarginFigures, 'budgetedMarginPct' | 'marginDeviationPp'>) {
  if (typeof f.marginDeviationPp !== 'number' || typeof f.budgetedMarginPct !== 'number') return 'Sem receita contratada.'
  if (f.marginDeviationPp === 0) return `Igual aos ${pct1(f.budgetedMarginPct)} orçados.`
  return `${pp(f.marginDeviationPp)} ${f.marginDeviationPp < 0 ? 'abaixo' : 'acima'} dos ${pct1(f.budgetedMarginPct)} orçados`
}

/** Resumo textual do gráfico (CA 17): a natureza que mais desviou acima do orçado. */
export function worstSummary(f: Pick<ProjectMarginFigures, 'lines'>) {
  const worst = [...f.lines].filter((l) => l.deviation > 0).sort((a, b) => b.deviation - a.deviation)[0]
  return worst ? `A natureza que mais desviou foi ${costLabel(worst.costType)}: ${formatMoney(worst.deviation)} acima do orçado.` : 'Nenhuma natureza está projetada acima do orçado.'
}

/** Formato curto do rótulo direto da coluna (R$ 12 mil). */
export const shortMoney = (v: number) => (Math.abs(v) >= 1000 ? `R$ ${(v / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} mil` : formatMoney(v, false))

export const MARGIN_DICAS = {
  realizado: 'Custo já gasto: material baixado para as OS (menos devoluções), horas apontadas, títulos a pagar confirmados com o projeto e o imposto das notas autorizadas.',
  comprometido: 'Já contratado, mas ainda não gasto: título previsto e material comprado para o projeto que ainda não foi baixado para a OS (títulos de compra − baixas, nunca abaixo de zero).',
  projetado: 'Realizado mais o maior entre o que ainda falta do orçado e o comprometido. Em impostos, o orçado até a primeira nota; depois, o imposto das notas mais o orçado da parte ainda não faturada.',
  desvio: 'Projetado menos orçado. Positivo quer dizer que o custo vai passar do orçado.',
  margemProjetada: 'Receita contratada (contrato mais aditivos aprovados) menos o custo projetado, sobre a receita. No total, somamos as receitas e os custos de todos os projetos (média ponderada pela receita).',
  impostoOrcado: 'Ainda não há nota fiscal do projeto: o imposto projetado é o do orçamento aprovado. Com a primeira nota, a parte faturada passa a usar o imposto da nota.',
  pp: 'Pontos percentuais: a diferença entre duas margens. Margem de 20% contra 23% orçados é 3 pp abaixo.',
  comoCalculamos: 'Somamos o orçado, o realizado e o projetado de cada projeto filtrado. As margens do total são ponderadas pela receita: (Σ receita − Σ projetado) ÷ Σ receita.',
}
