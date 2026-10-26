import type { DreRow } from '@/api/generated/model'
import type { TreeNode } from '@/components/ui/tree-table'

/** Textos das dicas do DRE (FIN-08): os termos que confundem. */
export const DRE_DICAS = {
  competencia: 'Cada documento entra no mês a que pertence, não no mês em que o dinheiro entrou ou saiu: a receita na data da venda (da nota, quando houver), a despesa na data do documento ou na competência da conta recorrente.',
  provisorio: 'Até a emissão de notas no sistema, a receita é a data de emissão dos títulos. Com as notas, passa a ser a data da nota.',
  adiantamentos: 'O sinal do contrato ainda não é receita: fica aqui até a nota do projeto. Antes das notas no sistema, entra na receita na expedição do projeto ou junto com o marco seguinte. É saldo no fim do mês e não muda o resultado.',
  receitaLiquida: 'Receita bruta menos as deduções (impostos sobre vendas).',
  lucroBruto: 'Receita líquida menos os custos de produzir e instalar. O % é a margem bruta.',
  resultadoFinanceiro: 'Juros, multas, descontos, tarifas e rendimentos. Positivo quando a empresa ganhou mais do que pagou.',
  aClassificar: 'Valores lançados sem a conta certa. Reclassifique o título para a conta do plano, para o DRE mostrar onde o dinheiro foi.',
  custos: 'A compra entra aqui no mês da nota do fornecedor. No resultado por projeto, o material entra quando é baixado para a OS; por isso os dois não fecham.',
  delta: 'Último mês do período menos o mês anterior, em valor e em percentual.',
} as const

export const PERIODS = [3, 6, 12, 24] as const

export const KIND_LABELS: Record<string, string> = {
  Receivable: 'Título a receber',
  DownPayment: 'Sinal reconhecido',
  Payable: 'Título a pagar',
  BankEntry: 'Lançamento bancário',
  ReceivableSettlement: 'Baixa a receber',
  PayableSettlement: 'Pagamento',
  Payroll: 'Folha',
  Invoice: 'Nota fiscal',
}

export interface DreNode extends TreeNode {
  row: DreRow
  children?: DreNode[]
}

/** Árvore da tabela pelas chaves do servidor: grupos, subtotais e a linha de memória na raiz; contas por baixo do pai. */
export function toTree(rows: DreRow[]): DreNode[] {
  const nodes = new Map<string, DreNode>(rows.map((r) => [r.key, { id: r.key, label: r.code ? `${r.code} ${r.label}` : r.label, row: r }]))
  const roots: DreNode[] = []
  for (const r of rows) {
    const node = nodes.get(r.key)!
    const parent = r.parentKey ? nodes.get(r.parentKey) : undefined
    if (parent) (parent.children ??= []).push(node)
    else roots.push(node)
  }
  return roots
}

const MONTHS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']

/** "2026-03-01" → "mar/26". */
export function monthLabel(iso: string): string {
  const [y, m] = iso.split('-').map(Number)
  return `${MONTHS[m - 1]}/${String(y).slice(2)}`
}

/** "2026-03-01" → "março de 2026" (leitores de tela e títulos do painel). */
export function monthLong(iso: string): string {
  const [y, m] = iso.split('-').map(Number)
  return new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, 15)))
}

/** Primeiro dia do mês em ISO, contando <paramref name="offset"/> meses a partir de <paramref name="iso"/>. */
export function addMonths(iso: string, offset: number): string {
  const [y, m] = iso.split('-').map(Number)
  const d = new Date(Date.UTC(y, m - 1 + offset, 1))
  return d.toISOString().slice(0, 10)
}

/** Primeiro dia do mês corrente (America/Sao_Paulo). */
export function currentMonth(now = new Date()): string {
  const [y, m] = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit' }).format(now).split('-')
  return `${y}-${m}-01`
}

/** Tom crítico só em resultado negativo (padrão 3): Lucro bruto, Resultado operacional e Resultado do período. */
export const isCritical = (row: DreRow, value: number | null | undefined) => row.criticalWhenNegative && value != null && value < 0

/** Célula com drill-down: conta ou grupo, nos meses e no Total (CA 13). */
export const isDrillable = (row: DreRow) => row.kind === 'Account' || row.kind === 'Group'

const pct = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })

export function formatPercent(value: number | null | undefined): string {
  return value == null ? '—' : `${pct.format(value)}%`
}

/** Δ com sinal explícito: "+R$ 1.300,00" ou "−R$ 2.500,00". */
export function formatSigned(value: number | null | undefined, money: (v: number) => string): string {
  if (value == null) return '—'
  if (value === 0) return money(0)
  return `${value > 0 ? '+' : '−'}${money(Math.abs(value))}`
}

/** Δ e % do mês escolhido contra o anterior (visão de 400 px, um mês por vez). */
export function monthDelta(values: number[], index: number): { delta: number | null; percent: number | null } {
  if (index <= 0) return { delta: null, percent: null }
  const delta = values[index] - values[index - 1]
  const prev = values[index - 1]
  return { delta, percent: prev === 0 ? null : Math.round((delta / Math.abs(prev)) * 10000) / 100 }
}

/** % da receita líquida do mês escolhido. */
export function monthPercent(value: number, netRevenue: number): number | null {
  return netRevenue === 0 ? null : Math.round((value / netRevenue) * 10000) / 100
}

/** "Mudou desde a sua última consulta em {data}: {n} documentos em {meses}." */
export function changesText(since: string, count: number, months: string[], formatSince: (v: string) => string): string {
  const list = months.map(monthLabel)
  const meses = list.length <= 1 ? list.join('') : `${list.slice(0, -1).join(', ')} e ${list[list.length - 1]}`
  return `Mudou desde a sua última consulta em ${formatSince(since)}: ${count} ${count === 1 ? 'documento' : 'documentos'} em ${meses}.`
}
