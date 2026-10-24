import type { PurchaseDecision, PurchaseLevelStatus, PurchaseOrderStatus } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Pedidos de compra no front (CMP-03a): rótulos, dicas e a conta do rodapé ao vivo; o servidor recalcula tudo ao salvar (CA 3). */
export const ORDER_STATUS: Record<PurchaseOrderStatus, { label: string; tone: StatusTone }> = {
  Draft: { label: 'Rascunho', tone: 'neutral' },
  PendingApproval: { label: 'Aguardando aprovação', tone: 'info' },
  Approved: { label: 'Aprovado', tone: 'ok' },
  Sent: { label: 'Enviado', tone: 'ok' },
  PartiallyReceived: { label: 'Parcialmente recebido', tone: 'info' },
  Received: { label: 'Recebido', tone: 'ok' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

export const LEVEL_STATUS: Record<PurchaseLevelStatus, { label: string; tone: StatusTone }> = {
  Pending: { label: 'Aguardando', tone: 'warn' },
  Approved: { label: 'Aprovado', tone: 'ok' },
  Rejected: { label: 'Reprovado', tone: 'crit' },
  Closed: { label: 'Encerrado', tone: 'neutral' },
}

export const DECISION: Record<PurchaseDecision, string> = {
  Submitted: 'Enviado para aprovação',
  Approved: 'Aprovado',
  Rejected: 'Reprovado',
  Changed: 'Alterado (voltou para a aprovação)',
  Cancelled: 'Cancelado',
}

export const ORDER_DICAS = {
  referencia: 'Valor de referência é o que decide a alçada: o maior entre o total do pedido e a soma dos pedidos do mesmo fornecedor nos últimos 30 dias (aprovados ou aguardando), incluindo este. Evita dividir uma compra grande em pedidos pequenos.',
  estimado: 'Estimado: o título previsto tem a data calculada pela previsão de entrega mais os dias da parcela. A data e o valor se confirmam no recebimento.',
  comprometido: 'Comprometido é o que já está pedido e aprovado para o projeto, mas ainda não virou custo realizado. O material conta como realizado só quando sai do estoque para a OS.',
  sugerido: 'Sugerido pelo último pedido do item: o fornecedor e o preço pago da última vez (ou o custo médio, sem pedido anterior). Confira antes de enviar.',
}

export const SHOW_OPTIONS = [
  { value: 'aguardando-voce', label: 'Aguardando você' },
  { value: 'abertos', label: 'Abertos' },
  { value: 'todos', label: 'Todos' },
]

export const STATUS_OPTIONS = (Object.keys(ORDER_STATUS) as PurchaseOrderStatus[]).map((value) => ({ value, label: ORDER_STATUS[value].label }))

const round2 = (value: number) => {
  const sign = value < 0 ? -1 : 1
  // toFixed(6) tira o erro de ponto flutuante (3,015 × 100 = 301,4999…) antes de arredondar, como o decimal do servidor.
  return (sign * Math.round(Number((Math.abs(value) * 100).toFixed(6)))) / 100
}

/** Total da linha: quantidade × preço, 2 casas, metade para longe do zero (a mesma regra do servidor). */
export const lineTotal = (quantity: number, unitPrice: number) => round2(quantity * unitPrice)

/** Rodapé ao vivo (CA 3): subtotal, IPI sobre o subtotal e total = subtotal + frete + IPI. */
export function orderTotals(lines: { quantity: number; unitPrice: number }[], freight: number, ipiPct: number) {
  const subtotal = round2(lines.reduce((sum, l) => sum + lineTotal(l.quantity, l.unitPrice), 0))
  const ipi = round2((subtotal * ipiPct) / 100)
  return { subtotal, ipi, total: round2(subtotal + freight + ipi) }
}

/** "Atrasado {n} d" (CA 20). */
export const lateLabel = (daysLate: number | null | undefined) => (daysLate && daysLate > 0 ? `Atrasado ${daysLate} d` : null)

/** Destino do pedido (R-CMP-6): o projeto ou "Estoque geral". */
export const orderDestination = (projectNumber: string | null | undefined, projectName?: string | null) =>
  projectNumber ? `${projectNumber}${projectName ? ` — ${projectName}` : ''}` : 'Estoque geral'

/**
 * Saldo projetado do caixa no dia de cada parcela (impacto do diálogo de aprovação): o saldo daquele dia no fluxo de caixa menos as
 * parcelas deste pedido até ele (o pedido ainda não está no caixa).
 */
export function projectedAfter<T extends { estimatedDueDate: string; amount: number }>(installments: T[], balanceOn: (date: string) => number | null) {
  let cumulative = 0
  return installments.map((i) => {
    cumulative = round2(cumulative + i.amount)
    const before = balanceOn(i.estimatedDueDate)
    return { ...i, before, after: before === null ? null : round2(before - cumulative) }
  })
}
