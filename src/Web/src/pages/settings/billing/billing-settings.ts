import type { OverdueFineFrom } from '@/api/generated/model'

export const FINE_KEY = 'finance.late_fine_pct'
export const INTEREST_KEY = 'finance.late_interest_month_pct'
export const OVERDUE_KEY = 'finance.overdue_fine_from'

export const OVERDUE_OPTIONS: { value: OverdueFineFrom; label: string }[] = [
  { value: 'OriginalDueDate', label: 'Vencimento original do título' },
  { value: 'SlipDueDate', label: 'Novo vencimento do boleto' },
]

/** Aviso do topo (CA 2): "{n} valores a confirmar". */
export const pendingText = (n: number) => (n === 1 ? '1 valor a confirmar' : `${n} valores a confirmar`)

export const BILLING_DICAS = {
  multa: 'Percentual cobrado uma vez quando o boleto é pago depois do vencimento. Pelo Código de Defesa do Consumidor, até 2%.',
  juros: 'Juros cobrados por mês de atraso, proporcionais aos dias. O usual é 1% ao mês.',
  seuNumero: 'Número do boleto criado pelo sistema para o banco identificar a cobrança. É curto e não revela o número do título.',
  nossoNumero: 'Número que o banco dá ao boleto. Aparece no extrato e no comprovante do cliente.',
  pix: 'Código que o cliente cola no aplicativo do banco para pagar na hora pelo Pix, sem digitar a linha digitável.',
}
