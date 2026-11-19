import type { CatalogPaymentMethod } from '@/pdv/local-store/catalog-types'
import type { Cents } from '@/pdv/money'
import { methodGroup, SINGLE_PART, type Tender, type TenderIntegration } from './payment-model'

/**
 * Porta `PaymentTerminal` (PDV-03 CA 11, rascunho C item 3): a tela de pagamento só fala com ela. Hoje a única implementação é a
 * `ManualTerminal` (maquininha externa: o operador cobra na maquininha e confirma na tela). Depois vêm a `TefTerminal` (PayGo ou SiTef pelo
 * agente), a `SmartPosTerminal` (casca Android) e o Pix dinâmico, sem mudar a tela: todas devolvem o mesmo `Tender` e passam pelos mesmos
 * estados (`Pending` enquanto espera, `Captured`, `Failed` ou `Unknown`).
 */
export interface ChargeRequest {
  tenderId: string
  method: CatalogPaymentMethod
  /** O que abate da venda (nunca mais do que falta). */
  amountCents: Cents
  /** Dinheiro: o que o cliente entregou (o troco é a diferença). */
  receivedCents?: Cents | null
  cardBrand?: string | null
  installments?: number | null
  nsu?: string | null
  note?: string | null
  operatorId: string | null
  at: string
}

export interface PaymentTerminal {
  readonly integration: TenderIntegration
  /**
   * Cobra. `onUpdate` recebe cada mudança de estado (por exemplo `Pending` enquanto a maquininha integrada espera o cartão); a promessa termina
   * no estado final: `Captured`, `Failed` ou `Unknown` (não se sabe se foi aprovado: pede conferência humana).
   */
  charge(request: ChargeRequest, onUpdate?: (tender: Tender) => void): Promise<Tender>
  /** Confere de novo um pagamento `Unknown` (no manual, quem confere é o operador, na maquininha). */
  check(tender: Tender, approved: boolean, at: string): Promise<Tender>
}

/** O pagamento com todos os campos (CA 10), vazios quando não se aplicam. */
export function tenderFrom(request: ChargeRequest, integration: TenderIntegration, status: Tender['status']): Tender {
  const group = methodGroup(request.method)
  const received = group === 'cash' ? request.receivedCents ?? request.amountCents : null
  return {
    tenderId: request.tenderId,
    partId: SINGLE_PART,
    status,
    methodId: request.method.id,
    methodName: request.method.name,
    methodType: request.method.kind,
    amountCents: request.amountCents,
    receivedCents: received,
    changeCents: received === null ? 0 : Math.max(0, received - request.amountCents),
    installments: group === 'card' ? Math.max(1, request.installments ?? 1) : null,
    provider: null,
    acquirerId: null,
    cardBrand: group === 'card' ? request.cardBrand ?? null : null,
    nsu: group === 'card' ? request.nsu ?? null : null,
    authorizationCode: null,
    txid: null,
    endToEndId: null,
    integration,
    operatorAttested: integration === 'Manual',
    needsReconciliation: integration === 'Manual' && group === 'pix',
    note: request.note ?? null,
    confirmedBy: request.operatorId,
    at: request.at,
  }
}

/**
 * Maquininha externa (CA 4, 5 e 12): o operador já cobrou na maquininha (ou conferiu o Pix no aplicativo do banco) e confirma. O pagamento nasce
 * `Captured`, "atestado pelo operador", e funciona igual com e sem rede. O Pix sai marcado para conferência contra o extrato no fechamento.
 */
export class ManualTerminal implements PaymentTerminal {
  readonly integration = 'Manual' as const

  charge(request: ChargeRequest): Promise<Tender> {
    return Promise.resolve(tenderFrom(request, this.integration, 'Captured'))
  }

  check(tender: Tender, approved: boolean, at: string): Promise<Tender> {
    return Promise.resolve({ ...tender, status: approved ? 'Captured' : 'Failed', operatorAttested: true, at })
  }
}
