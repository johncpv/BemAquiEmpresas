import type { GoodsReceiptStatus } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { lineTotal } from '../orders/purchase-orders'

/** Recebimento no front (CMP-04): rótulos, dicas e as contas ao vivo do formulário; o servidor confere e calcula tudo de novo. */
export const RECEIPT_STATUS: Record<GoodsReceiptStatus, { label: string; tone: StatusTone }> = {
  Completed: { label: 'Registrado', tone: 'ok' },
  Reversed: { label: 'Estornado', tone: 'neutral' },
}

export const RECEIPT_DICAS = {
  custoEntrada: 'Custo de entrada é quanto cada unidade custou de verdade: o valor da linha (quantidade × preço do pedido) mais a parte do frete e do IPI dela, dividido pela quantidade. É ele que entra no custo médio do item.',
  rateio: 'Rateio do frete: o frete e o IPI deste recebimento são divididos entre os itens pelo valor de cada linha. A última linha fica com a diferença de centavos. O IPI entra no custo até a contadora decidir se ele é recuperável.',
  tolerancia: 'Tolerância é quanto o recebimento pode passar do pedido. Com 0% (o padrão), nada passa do saldo da linha nem do total aprovado; o excedente vira outro pedido. Quem muda é Configurações → Parâmetros.',
  encerrarSaldo: 'Encerrar saldo é dizer que o resto do pedido não vem mais. O pedido fica Recebido, o previsto que sobrou sai do fluxo de caixa e a quantidade que não chegou volta para as solicitações, para comprar de outro jeito.',
  ultimaCompra: 'Última compra é o preço e o fornecedor do último pedido aprovado deste item. O novo pedido e a cotação sugerem a partir dela.',
}

const round = (value: number, places: number) => {
  const factor = 10 ** places
  const sign = value < 0 ? -1 : 1
  return (sign * Math.round(Number((Math.abs(value) * factor).toFixed(6)))) / factor
}

/** Quantidade na unidade de estoque: na do fornecedor, multiplica pelo fator e arredonda para 4 casas (a mesma conta do servidor). */
export const toStock = (quantity: number, factor: number | null | undefined) => (factor ? round(quantity * factor, 4) : quantity)

/**
 * Frete ou IPI proporcional ao que está sendo recebido (CA 1): o valor do pedido × (itens deste recebimento ÷ subtotal do pedido), até o
 * que sobrou; o recebimento que completa o pedido leva o resto. É o máximo: no recebimento, frete e IPI só baixam.
 */
export function proportionalShare(orderAmount: number, orderSubtotal: number, receiptSubtotal: number, used: number, completes: boolean) {
  const left = Math.max(0, round(orderAmount - used, 2))
  if (completes || orderSubtotal <= 0) return left
  return Math.min(left, round((orderAmount * receiptSubtotal) / orderSubtotal, 2))
}

export interface DraftLine {
  poLineId: string
  quantity: number
  unitPrice: number
  open: number
}

/** Valor dos itens recebidos (Σ qtd × preço, 2 casas por linha) e se este recebimento completa o pedido. */
export function receiptTotals(lines: DraftLine[]) {
  const subtotal = round(lines.reduce((sum, l) => sum + lineTotal(l.quantity, l.unitPrice), 0), 2)
  const completes = lines.length > 0 && lines.every((l) => l.quantity >= l.open)
  return { subtotal, completes }
}

/** Texto curto do saldo (CA 1): "Saldo 6 UN" ou "Tudo recebido". */
export const balanceLabel = (open: number, unit: string, format: (n: number) => string) => (open > 0 ? `Saldo ${format(open)} ${unit}`.trim() : 'Tudo recebido')
