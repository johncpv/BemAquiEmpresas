import { getDownloadCashSessionReportUrl } from '@/api/generated/bemaqui'
import type { CashSessionStatus } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatMoney } from '@/lib/format'

/** Situações da sessão de caixa (CX-02), na ordem do filtro. */
export const CASH_STATUSES: CashSessionStatus[] = ['PendingReview', 'Counted', 'Counting', 'Open', 'Closed']

export const STATUS_LABEL: Record<CashSessionStatus, string> = {
  Open: 'Aberto',
  Counting: 'Em contagem',
  Counted: 'Contado',
  PendingReview: 'Em conferência',
  Closed: 'Fechado',
}

export const STATUS_TONE: Record<CashSessionStatus, StatusTone> = {
  Open: 'info',
  Counting: 'info',
  Counted: 'info',
  PendingReview: 'warn',
  Closed: 'ok',
}

export const EMPTY = 'Nenhum caixa fechado no período.'
export const BLIND_DICA = 'Conferência cega: o operador conta o dinheiro e os comprovantes sem ver quanto deveria haver. Só quem aprova vendas vê o esperado e a diferença.'
export const BREAK_DICA = 'Quebra de caixa é a diferença entre o contado e o esperado de cada forma: sobra (positiva) ou falta (negativa). Acima da tolerância da loja, o fechamento vai para conferência.'

/** "R$ 3,00 (sobra)", "R$ 2,00 (falta)" ou "R$ 0,00". */
export function differenceText(value: number): string {
  if (value > 0) return `${formatMoney(value)} (sobra)`
  if (value < 0) return `${formatMoney(-value)} (falta)`
  return formatMoney(0)
}

/** PDF do fechamento (CA 9): a via do operador ou a do gerente (essa, só para quem aprova vendas). */
export const reportUrl = (id: string, copy: 'operator' | 'manager') => getDownloadCashSessionReportUrl(id, { copy })
