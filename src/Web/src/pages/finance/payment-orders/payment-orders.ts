import type { PaymentDetailsInput } from '@/api/generated/model'
import type { Tone } from '../receivables/receivables'

export const ORDER_STATUS: Record<string, { label: string; tone: Tone }> = {
  Pending: { label: 'Aguardando aprovação', tone: 'warn' },
  Approved: { label: 'Aprovada', tone: 'ok' },
  Rejected: { label: 'Reprovada', tone: 'crit' },
  Paid: { label: 'Paga', tone: 'ok' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
  Reversed: { label: 'Estornada', tone: 'neutral' },
}

export const ORDER_SITUATIONS = [
  { value: 'aguardando', label: 'Aguardando aprovação' },
  { value: 'aprovada', label: 'Aprovadas' },
  { value: 'paga', label: 'Pagas' },
  { value: 'reprovada', label: 'Reprovadas' },
  { value: 'cancelada', label: 'Canceladas ou estornadas' },
]

export const ORDER_DICAS = {
  alcada: 'Quem pode aprovar a ordem. Nesta versão, quem tem a permissão de aprovar no Financeiro aprova qualquer valor.',
  parcial: 'Pague menos que o saldo do título. O que faltar continua em aberto e entra noutra ordem.',
  acrescimos: 'Juros e multa que você já espera pagar. Entram no total aprovado.',
  aprovado: 'Valor a pagar mais os acréscimos previstos. O pagamento não passa disso sem nova aprovação.',
  propria: 'O próprio solicitante aprovou, porque é o único Proprietário ou porque o parâmetro permite.',
}

/** Para onde vai o dinheiro, numa linha (retrato já mascarado pela API). */
export function destination(p: PaymentDetailsInput) {
  if (p.kind === 'Pix') return `Pix ${p.pixKey ?? ''} · ${p.holderName ?? ''}`
  if (p.kind === 'Boleto') return `Boleto ${p.barcode ?? ''}`
  if (p.kind === 'Ted') return `TED banco ${p.bankCode ?? ''} ag. ${p.agency ?? ''} conta ${p.account ?? ''} · ${p.holderName ?? ''}`
  return p.description ?? 'Outra forma'
}

/** Total que sai da conta por item: principal + juros + multa − desconto, em centavos. */
export function itemTotal(amount: number, interest: number, fine: number, discount: number) {
  return Math.round((amount + interest + fine - discount) * 100) / 100
}
