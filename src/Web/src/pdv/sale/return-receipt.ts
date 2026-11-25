import { type Cents, toCents } from '@/pdv/money'
import { NOT_FISCAL_TEXT, type ReceiptDocument, type ReceiptLine, receiptMoney } from '@/pdv/print/receipt-model'

const when = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
const day = (date: string) => `${date.slice(8, 10)}/${date.slice(5, 7)}/${date.slice(0, 4)}`

/** O que o recibo precisa da devolução concluída (a resposta do servidor). */
export interface ReturnReceiptData {
  number: string
  saleNumber: string
  refundKind: 'StoreCredit' | 'Cash' | 'CardReversal'
  total: number | string
  cardNsu?: string | null
  completedAt?: string | null
  lines: readonly { description: string; quantity: number | string; amount: number | string; destination: 'Restock' | 'Damaged'; reason: string }[]
  storeCredit?: { code?: string | null; codeLast4: string; amount: number | string; expiresOn: string } | null
}

export const REFUND_LABEL: Record<ReturnReceiptData['refundKind'], string> = { StoreCredit: 'Vale-troca', Cash: 'Dinheiro', CardReversal: 'Estorno no cartão' }

const cents = (value: number | string): Cents => toCents(String(value))

/**
 * Recibo de devolução (PDV-06 CA 6) e, no vale, o **vale-troca** (CA 7) com código, valor e validade, montados no aparelho com a resposta do servidor.
 * No MVP não há documento fiscal: o recibo diz "NÃO É DOCUMENTO FISCAL". O código do vale só existe neste papel (o servidor guarda o hash).
 */
export function buildReturnReceipt(data: ReturnReceiptData, place: string, operatorName: string): ReceiptDocument {
  const lines: ReceiptLine[] = [
    { kind: 'text', text: 'RECIBO DE DEVOLUÇÃO', align: 'center', bold: true },
    { kind: 'text', text: place, align: 'center' },
    { kind: 'rule' },
    { kind: 'pair', left: 'Devolução', right: data.number },
    { kind: 'pair', left: 'Venda', right: data.saleNumber },
    { kind: 'pair', left: 'Data', right: when(data.completedAt ?? new Date().toISOString()) },
    { kind: 'pair', left: 'Operador', right: operatorName },
    { kind: 'rule' },
    ...data.lines.flatMap((l): ReceiptLine[] => [
      { kind: 'text', text: l.description },
      { kind: 'pair', left: `${String(l.quantity).replace('.', ',')} · ${l.destination === 'Damaged' ? 'avariado' : 'volta ao estoque'}`, right: receiptMoney(cents(l.amount)) },
    ]),
    { kind: 'rule' },
    { kind: 'pair', left: 'TOTAL DEVOLVIDO', right: receiptMoney(cents(data.total)), bold: true },
    { kind: 'pair', left: 'Reembolso', right: REFUND_LABEL[data.refundKind] },
  ]
  if (data.refundKind === 'CardReversal' && data.cardNsu) lines.push({ kind: 'pair', left: 'NSU do estorno', right: data.cardNsu })
  lines.push({ kind: 'text', text: NOT_FISCAL_TEXT, align: 'center' }, { kind: 'blank' }, { kind: 'cut' })

  const credit = data.storeCredit
  if (credit?.code) {
    lines.push(
      { kind: 'text', text: 'VALE-TROCA', align: 'center', bold: true },
      { kind: 'text', text: place, align: 'center' },
      { kind: 'rule' },
      { kind: 'text', text: credit.code, align: 'center', bold: true },
      { kind: 'pair', left: 'Valor', right: receiptMoney(cents(credit.amount)), bold: true },
      { kind: 'pair', left: 'Válido até', right: day(credit.expiresOn) },
      { kind: 'pair', left: 'Devolução', right: data.number },
      { kind: 'blank' },
      { kind: 'text', text: 'Guarde este vale: o código vale como dinheiro na loja e não é reimpresso.', align: 'center' },
      { kind: 'text', text: NOT_FISCAL_TEXT, align: 'center' },
      { kind: 'blank' }, { kind: 'cut' },
    )
  }

  return { title: 'Recibo de devolução', lines }
}
