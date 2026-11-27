import type { StatusTone } from '@/components/ui/status-chip'

/** Situação do pedido (VND-02a) com o rótulo e o tom do chip (texto sempre junto da cor). */
export const ORDER_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Draft: { label: 'Rascunho', tone: 'info' },
  Open: { label: 'Aberto', tone: 'warn' },
  AwaitingPayment: { label: 'Aguardando pagamento', tone: 'warn' },
  Paid: { label: 'Pago', tone: 'ok' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

export function statusChip(status: string): { label: string; tone: StatusTone } {
  return ORDER_STATUS[status] ?? { label: status, tone: 'neutral' }
}

/** Grupos da lista pelo próximo passo, na ordem da tela; "Para separar", "Para emitir nota" e "Para enviar" chegam com a VND-02b. */
export const NEXT_STEPS = [
  { step: 'Confirm', label: 'Para confirmar', action: 'Confirmar pedido' },
  { step: 'Charge', label: 'Para cobrar', action: 'Gerar cobrança' },
  { step: 'AwaitPayment', label: 'Aguardando pagamento', action: 'Registrar pagamento' },
  { step: 'Pick', label: 'Pagos', action: 'Ver venda' },
] as const

export type NextStep = (typeof NEXT_STEPS)[number]['step']

export function isNextStep(value: unknown): value is NextStep {
  return NEXT_STEPS.some((s) => s.step === value)
}

export function nextStepLabel(step: string): string | undefined {
  return NEXT_STEPS.find((s) => s.step === step)?.action
}

export const ORIGIN_LABEL: Record<string, string> = {
  Backoffice: 'Balcão/telefone',
  Conecta: 'Conecta',
  Marketplace: 'Marketplace',
  Api: 'Outro sistema',
  Storefront: 'Loja virtual',
}

/** Faixa de problema com o texto e a ação (tela do pedido). */
export const PROBLEMS: Record<string, { label: string; tone: StatusTone; text: string }> = {
  DiscountNeedsApproval: { label: 'Desconto para aprovar', tone: 'warn', text: 'O desconto passou do limite. Só quem aprova em Vendas pode confirmar este pedido.' },
  ReservationShort: { label: 'Reserva com falta', tone: 'crit', text: 'Parte do estoque reservado não está mais no local. Reponha o item ou ajuste a quantidade.' },
  PaymentInReview: { label: 'Pagamento em conferência', tone: 'warn', text: 'O banco recebeu um valor diferente. O Financeiro confere o pagamento antes de o pedido ficar pago.' },
  PaidAfterCancel: { label: 'Pago depois de cancelado', tone: 'crit', text: 'O cliente pagou o boleto depois do cancelamento. Reabra o pedido ou registre a devolução do valor.' },
}

export const ORDER_DICAS = {
  reservado: 'Reservado é o estoque separado para este pedido: o balcão, a loja virtual e os outros canais já não contam com ele. A reserva sai se o pedido for cancelado ou o prazo de pagamento vencer.',
  conecta: 'O pedido chegou por um aplicativo conectado. Ele não reserva estoque nem gera cobrança sozinho: confira os itens com o cliente e confirme aqui.',
  pagoDepois: 'O banco avisou o pagamento de um pedido que já estava cancelado. Nada é estornado sozinho: escolha reabrir o pedido (o estoque é reservado de novo) ou registrar que o valor foi devolvido.',
  prazo: 'Sem cobrança, a reserva vale pelo prazo de Configurações → Parâmetros → Pedidos de venda (48 h para pessoa, 2 h para aplicativo). Com boleto, até o dia útil seguinte ao vencimento.',
} as const

/** Rótulo da linha do tempo, em linguagem simples. */
export const TIMELINE_ACTION: Record<string, string> = {
  create: 'Pedido criado',
  edit: 'Pedido alterado',
  confirm: 'Pedido confirmado e estoque reservado',
  charge: 'Cobrança gerada',
  charge_lapsed: 'Cobrança sem pagamento: pedido voltou a aberto',
  pay: 'Pagamento registrado: virou venda',
  cancel: 'Pedido cancelado',
  cancel_paid: 'Pedido cancelado e venda estornada',
  expire: 'Cancelado pelo prazo de pagamento',
  paid_after_cancel: 'Pago depois de cancelado',
  reopen: 'Pedido reaberto: virou venda',
  refund: 'Devolução do valor registrada',
}

export function timelineLabel(action: string): string {
  return TIMELINE_ACTION[action] ?? action
}

/** Linha do pedido em edição no formulário. */
export interface DraftLine {
  productId: string
  description: string
  unitCode: string
  price: number
  available: number
  quantity: number
  discount: number
}

/** Totais calculados na tela como prévia (o servidor recalcula e é a fonte). */
export function draftTotals(lines: readonly DraftLine[], orderDiscount = 0) {
  const round = (n: number) => Math.round(n * 100) / 100
  const subtotal = round(lines.reduce((sum, l) => sum + l.price * l.quantity, 0))
  const discounts = round(lines.reduce((sum, l) => sum + l.discount, 0) + orderDiscount)
  return { subtotal, discounts, total: round(subtotal - discounts) }
}

/** Linha que pede mais do que há disponível no local (aviso antes de confirmar). */
export function shortLines(lines: readonly DraftLine[]): DraftLine[] {
  return lines.filter((l) => l.quantity > l.available)
}

/** "Parado" há N dias (filtro da lista). */
export function staleDays(updatedAt: string, now: Date = new Date()): number {
  return Math.floor((now.getTime() - new Date(updatedAt).getTime()) / 86_400_000)
}
