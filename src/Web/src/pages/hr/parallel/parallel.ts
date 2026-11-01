/** Conferência paralela da folha (FLH-06): rótulos, chips, a diferença em % e as dicas da tela. */
import type { StatusTone } from '@/components/ui/status-chip'

export const STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Open: { label: 'Em conferência', tone: 'info' },
  Accepted: { label: 'Aceita', tone: 'ok' },
  Rejected: { label: 'Reprovada', tone: 'crit' },
}

export const TYPE_LABELS: Record<string, string> = { Monthly: 'Mensal', Vacation: 'Férias', Thirteenth: '13º salário', Termination: 'Rescisão' }

export const CLASSIFICATIONS = [
  { value: 'SystemError', label: 'Erro do sistema' },
  { value: 'AccountantError', label: 'Erro da contadora' },
  { value: 'RuleDifference', label: 'Regra diferente: ajustar parâmetro' },
  { value: 'Rounding', label: 'Arredondamento' },
] as const

export type Classification = (typeof CLASSIFICATIONS)[number]['value']

const pct = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 4 })

/** Diferença % sobre o valor da contadora (CA 1): "—" sem ele; o sinal sempre aparece quando há diferença. */
export function formatDiffPct(value: number | null | undefined) {
  if (value === null || value === undefined) return '—'
  if (value === 0) return '0%'
  return `${value > 0 ? '+' : ''}${pct.format(value)}%`
}

/** "{n} de {exigidas} competências seguidas aceitas" (CA 5). */
export function streakLabel(count: number, required: number) {
  return `${count} de ${required} ${required === 1 ? 'competência seguida aceita' : 'competências seguidas aceitas'}`
}

/** "2026-10-01" → "2026-10" (a competência na URL e na comparação). */
export const monthOf = (period: string) => period.slice(0, 7)

/** CRC aceito pelo servidor: de 4 a 30 caracteres com pelo menos 3 dígitos (ex.: SP-123456/O). */
export const validCrc = (crc: string) => {
  const text = crc.trim()
  return text.length >= 4 && text.length <= 30 && (text.match(/\d/g)?.length ?? 0) >= 3
}

interface RowLike {
  equal: boolean
  difference: number
}

/** "Só diferenças" e a ordem da tela: as diferentes primeiro, das maiores para as menores (Propositivo). */
export function visibleRows<T extends RowLike>(rows: readonly T[], onlyDifferences: boolean) {
  const list = onlyDifferences ? rows.filter((r) => !r.equal) : [...rows]
  return list.sort((a, b) => Number(a.equal) - Number(b.equal) || Math.abs(b.difference) - Math.abs(a.difference))
}

export const PARALLEL_DICAS = {
  virada: 'A partir da competência seguinte, a folha calculada pelo sistema passa a ser a oficial: ela fecha, gera os títulos a pagar e o caixa. A folha lançada continua disponível, e o Proprietário pode voltar para ela com o motivo.',
  diferencaPct: 'A diferença em relação ao valor da contadora. Aparece "—" quando a contadora não lançou nada naquela rubrica.',
  termo: 'O documento que a contadora assina dizendo que conferiu a folha calculada da competência e que não há diferença. O sistema gera o termo com os totais; imprima, peça a assinatura e anexe o termo assinado para aceitar.',
  protegidos: 'Os valores da folha por funcionário são dados pessoais protegidos pela LGPD. Só quem tem a permissão "Ver dados sensíveis" do RH vê a comparação.',
}
