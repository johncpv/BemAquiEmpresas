import type { CatalogPaymentMethod } from '@/pdv/local-store/catalog-types'
import { type Cents, MoneyError, sumCents, toCents } from '@/pdv/money'

/**
 * Pagamento da venda no caixa (PDV-03, rascunho C itens 1 a 4). Tudo em centavos inteiros e sem rede.
 *
 * - **Máquina de estados do pagamento** (`Tender`): `Created → Pending → Captured → (Voided | Refunded)`, mais `Failed` e `Unknown`. No modo
 *   manual (maquininha externa) o pagamento nasce `Captured`, "atestado pelo operador"; `Pending`, `Failed` e `Unknown` existem para o TEF, o
 *   SmartPOS e o Pix dinâmico (PAG-02 a PAG-04) e já têm tela.
 * - **Invariante:** a venda só fecha com a soma dos capturados igual ao total; troco só no dinheiro, guardado à parte (o valor do pagamento
 *   nunca passa do que faltava).
 * - **Cartão:** do cartão ficam a bandeira e, se o operador quiser, o NSU. Nunca o número, a validade nem o código de segurança: os campos
 *   livres recusam sequência que pareça número de cartão.
 */

export type TenderStatus = 'Created' | 'Pending' | 'Captured' | 'Voided' | 'Refunded' | 'Failed' | 'Unknown'

/** Tipo de integração: só `Manual` por enquanto (maquininha externa); os outros chegam com a PAG-02 a PAG-04. */
export type TenderIntegration = 'Manual' | 'Tef' | 'SmartPos' | 'PixDynamic'

const TRANSITIONS: Record<TenderStatus, readonly TenderStatus[]> = {
  Created: ['Pending', 'Captured', 'Failed'],
  Pending: ['Captured', 'Failed', 'Unknown'],
  Unknown: ['Captured', 'Failed'],
  Captured: ['Voided', 'Refunded'],
  Failed: [],
  Voided: [],
  Refunded: [],
}

/** O pagamento pode ir de `from` para `to`? Capturado é fato: só sai por cancelamento (`Voided`) ou estorno (`Refunded`), na PDV-04. */
export const canTransition = (from: TenderStatus, to: TenderStatus): boolean => TRANSITIONS[from].includes(to)

/**
 * Um pagamento da venda (CA 10): todos os campos existem desde já, vazios (`null`) quando não se aplicam. `amountCents` é o que abate da venda;
 * no dinheiro, `receivedCents` é o que o cliente entregou e `changeCents` o troco.
 */
export interface Tender {
  tenderId: string
  /** Parte da venda (a divisão por pessoa e por item é da MESA-04; aqui a venda tem uma parte só). */
  partId: string
  status: TenderStatus
  methodId: string
  methodName: string
  /** Tipo da forma, como o servidor manda (`Cash`, `Pix`, `Credit`...). */
  methodType: string
  amountCents: Cents
  receivedCents: Cents | null
  changeCents: Cents
  installments: number | null
  /** Provedor da integração (TEF, SmartPOS, PSP do Pix). */
  provider: string | null
  acquirerId: string | null
  cardBrand: string | null
  nsu: string | null
  authorizationCode: string | null
  txid: string | null
  endToEndId: string | null
  integration: TenderIntegration
  /** Confirmado pelo operador, sem retorno de máquina nem de banco (modo manual). */
  operatorAttested: boolean
  /** Pix conferido pelo operador: listado um a um no fechamento (CX-02) para conferir contra o extrato. */
  needsReconciliation: boolean
  note: string | null
  /** Quem confirmou (o operador do caixa); toda ação com dinheiro leva quem a fez. */
  confirmedBy: string | null
  at: string
}

export const SINGLE_PART = 'main'

/** Como o caixa trata a forma: dinheiro dá troco; cartão pede bandeira; Pix lembra de conferir no banco; vale-troca e fiado precisam de rede. */
export type MethodGroup = 'cash' | 'pix' | 'card' | 'store-credit' | 'on-account' | 'other'

export function methodGroup(method: Pick<CatalogPaymentMethod, 'kind'>): MethodGroup {
  switch (method.kind) {
    case 'Cash': return 'cash'
    case 'Pix': return 'pix'
    case 'Debit': case 'Credit': case 'CreditInstallments': case 'Voucher': return 'card'
    case 'StoreCredit': return 'store-credit'
    case 'OnAccount': return 'on-account'
    default: return 'other'
  }
}

/** Só o dinheiro dá troco (o catálogo antigo, sem o campo, vale pelo tipo). */
export const allowsChange = (method: CatalogPaymentMethod): boolean => method.allowsChange ?? method.kind === 'Cash'

/** Bandeiras do cadastro (PAG-01); a forma sem bandeira própria na tabela mostra todas. */
export const ALL_BRANDS = ['Visa', 'Mastercard', 'Elo', 'Amex', 'Hipercard', 'Diners', 'Cabal', 'Other'] as const
export const BRAND_LABEL: Record<string, string> = {
  Visa: 'Visa', Mastercard: 'Mastercard', Elo: 'Elo', Amex: 'American Express', Hipercard: 'Hipercard', Diners: 'Diners', Cabal: 'Cabal', Other: 'Outra',
}
export const brandsOf = (method: CatalogPaymentMethod): string[] => (method.cardBrands?.length ? method.cardBrands : [...ALL_BRANDS])
export const maxInstallmentsOf = (method: CatalogPaymentMethod): number => Math.max(1, method.maxInstallments ?? 1)

/**
 * CA 9: vale-troca e fiado dependem de dado do servidor. Sem rede, aparecem desabilitados ("Disponível com internet."); com rede, ficam ocultos
 * enquanto a PDV-06 e a PDV-08 não chegam.
 */
export type MethodAvailability = 'enabled' | 'needs-network' | 'hidden'
export function methodAvailability(method: CatalogPaymentMethod, online: boolean): MethodAvailability {
  const group = methodGroup(method)
  if (group !== 'store-credit' && group !== 'on-account') return 'enabled'
  return online ? 'hidden' : 'needs-network'
}

export const isCaptured = (t: Tender) => t.status === 'Captured'
/** Soma dos pagamentos capturados (o que abate da venda). */
export const paidCents = (tenders: readonly Tender[]): Cents => sumCents(tenders.filter(isCaptured).map((t) => t.amountCents))
export const changeCents = (tenders: readonly Tender[]): Cents => sumCents(tenders.filter(isCaptured).map((t) => t.changeCents))
export const remainingCents = (totalCents: Cents, tenders: readonly Tender[]): Cents => Math.max(0, totalCents - paidCents(tenders))

/** CA 2: no dinheiro, o pagamento é o que faltava (ou menos) e o que passou é troco. */
export function splitCash(receivedCents: Cents, remaining: Cents): { amountCents: Cents; changeCents: Cents } {
  const amountCents = Math.min(receivedCents, remaining)
  return { amountCents, changeCents: receivedCents - amountCents }
}

export const ONLY_CASH_GIVES_CHANGE = 'Só o dinheiro dá troco.'

/** CA 3: fora do dinheiro, valor maior que o que falta é limitado ao que falta, com o aviso. */
export function limitToRemaining(valueCents: Cents, remaining: Cents): { valueCents: Cents; message: string | null } {
  return valueCents > remaining ? { valueCents: remaining, message: ONLY_CASH_GIVES_CHANGE } : { valueCents, message: null }
}

const NOTES: readonly Cents[] = [200, 500, 1_000, 2_000, 5_000, 10_000, 20_000]

/** CA 2: botões de valor rápido: o exato e as próximas notas acima (até 4 opções, sem repetir). */
export function quickCashOptions(remaining: Cents): Cents[] {
  if (remaining <= 0) return []
  const options = [remaining]
  for (const note of NOTES) {
    const rounded = Math.ceil(remaining / note) * note
    if (rounded > remaining && !options.includes(rounded)) options.push(rounded)
    if (options.length === 4) break
  }
  return options.sort((a, b) => a - b)
}

/** Valor digitado ("12,50", "1.234,56", "12") em centavos; null quando não é dinheiro. */
export function parseMoney(text: string): Cents | null {
  const clean = text.replace(/\s|R\$/g, '')
  if (!clean) return null
  const normalized = clean.includes(',') ? clean.replace(/\./g, '').replace(',', '.') : clean
  try {
    const cents = toCents(normalized)
    return cents >= 0 ? cents : null
  } catch (e) {
    if (e instanceof MoneyError) return null
    throw e
  }
}

export const CARD_NUMBER_MESSAGE = 'Não digite o número do cartão.'

/**
 * CA 4 (segurança): o texto tem uma sequência que parece número de cartão (13 ou mais dígitos)? O NSU tem até 12 dígitos; número de cartão
 * tem de 13 a 19. Antes de contar, normaliza (revisão de segurança): NFKC (dígitos fullwidth viram ASCII), tira os caracteres invisíveis de
 * formatação (`\p{Cf}`: zero-width space, word joiner...) e trata qualquer coisa que não seja letra nem número entre dígitos como separador
 * (espaço, ponto, traço, barra, sublinhado, vírgula, asterisco, travessões).
 */
export function looksLikeCardNumber(text: string): boolean {
  const joined = text.normalize('NFKC').replace(/\p{Cf}/gu, '').replace(/(\d)[^\p{L}\p{N}]+(?=\d)/gu, '$1')
  return /\p{Nd}{13,}/u.test(joined)
}

/** NSU (opcional): letras e números, até 12. */
export function checkNsu(text: string): { ok: true; value: string | null } | { ok: false; message: string } {
  const value = text.trim()
  if (!value) return { ok: true, value: null }
  if (looksLikeCardNumber(value)) return { ok: false, message: CARD_NUMBER_MESSAGE }
  if (!/^[0-9A-Za-z]{1,12}$/.test(value)) return { ok: false, message: 'O NSU tem até 12 letras ou números, como sai no comprovante da maquininha.' }
  return { ok: true, value }
}

/** Observação do pagamento (opcional, até 140), sem número de cartão. */
export function checkTenderNote(text: string): { ok: true; value: string | null } | { ok: false; message: string } {
  const value = text.trim().slice(0, 140)
  if (!value) return { ok: true, value: null }
  if (looksLikeCardNumber(value)) return { ok: false, message: CARD_NUMBER_MESSAGE }
  return { ok: true, value }
}

/**
 * Estado da venda no pagamento (CA 1, 6, 7 e 8): `Open` (lançando itens), `InPayment` (na tela de pagamento, nada capturado),
 * `PartiallyPaid` (algo capturado e saldo a pagar) e `Completed` (soma dos capturados igual ao total).
 */
export type SalePaymentState = 'Open' | 'InPayment' | 'PartiallyPaid' | 'Completed'

export function salePaymentState(inPayment: boolean, totalCents: Cents, tenders: readonly Tender[]): SalePaymentState {
  const paid = paidCents(tenders)
  if (paid > 0 && paid >= totalCents) return 'Completed'
  if (paid > 0) return 'PartiallyPaid'
  return inPayment ? 'InPayment' : 'Open'
}
