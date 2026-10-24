import type { CostType } from '@/api/generated/model'
import { COST_SECTIONS } from '@/pages/quotes/quotes'

/** Textos da seção Compras de Configurações → Parâmetros (CMP-03a, decisão do PO de 21/10). */
export const PAYABLE_DICA = 'Conta do plano e natureza de custo dos títulos previstos que o pedido de compra lança no caixa quando é aprovado. O frete e o IPI do próprio fornecedor vão nas mesmas parcelas. A natureza separa o custo do projeto (Material entra em Comprometido até a baixa para a OS).'
export const FREIGHT_DICA = 'Conta e natureza do título de frete pago a outro fornecedor (transportadora). O frete do próprio fornecedor do pedido vai nas parcelas do pedido.'
export const PENDING_LABEL = 'A confirmar com a contadora'
export const TOLERANCE_DICA = 'Tolerância é quanto o recebimento pode passar do pedido sem voltar à aprovação: a quantidade de cada linha e o valor total aprovado podem passar até este percentual. Com 0%, o recebimento nunca passa do pedido; o excedente vira outro pedido.'

/** Tolerância de 0% a 50%, até 2 casas (vírgula ou ponto); null quando inválida. */
export function parseTolerance(text: string): number | null {
  const value = Number(text.trim().replace(',', '.'))
  if (text.trim() === '' || Number.isNaN(value) || value < 0 || value > 50 || Math.round(value * 100) !== value * 100) return null
  return value
}

export interface PurchasingAccounting {
  defaultLeadDays: number
  payableAccountCode: string
  payableCostType: CostType
  freightAccountCode: string
  freightCostType: CostType
  accountingConfirmed: boolean
  /** Tolerância do recebimento (CMP-04, D8): % acima da quantidade e do aprovado; padrão 0%. */
  receiptTolerancePct?: number
}

/** Alguma coisa mudou em relação ao que está salvo (habilita "Salvar parâmetros de compras"). */
const KEYS: (keyof PurchasingAccounting)[] = ['defaultLeadDays', 'payableAccountCode', 'payableCostType', 'freightAccountCode', 'freightCostType', 'accountingConfirmed', 'receiptTolerancePct']

export const purchasingChanged = (saved: PurchasingAccounting, draft: PurchasingAccounting) => KEYS.some((k) => (saved[k] ?? 0) !== (draft[k] ?? 0))

/** Selo "A confirmar com a contadora" enquanto ninguém marcou a conferência. */
export const accountingPending = (saved: Pick<PurchasingAccounting, 'accountingConfirmed'>) => !saved.accountingConfirmed

export const costTypeLabel = (type: CostType) => COST_SECTIONS.find((c) => c.type === type)?.label ?? type
