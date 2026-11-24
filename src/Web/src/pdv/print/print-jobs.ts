import type { CatalogPrinter } from '@/pdv/local-store/catalog-types'
import type { LocalStore } from '@/pdv/local-store/local-store'
import type { Cents } from '@/pdv/money'
import { BRAND_LABEL, isCaptured } from '@/pdv/payment/payment-model'
import type { CompletedSale } from '@/pdv/sale/sale-register'
import { lineTotal, prorateCents } from '@/pdv/sale/sale-model'
import { uuidv7 } from '@/pdv/uuid-v7'
import { BROWSER_DEFAULT_PRINTER, type HardwareBridge, type PrinterTarget, type PrintOutcome } from './hardware-bridge'
import { buildSaleReceipt, type ReceiptDocument, type ReceiptItem, type ReceiptPayment, receiptDocument } from './receipt-model'

/** Fila de saída: cada impressão de cupom (original ou 2ª via) sobe como evento `ReceiptPrinted` ou `ReceiptReprinted` (IMP-01 CA 4). */
export const PRINT_OUTBOX_KIND = 'pos.print'

/** Últimos cupons do aparelho, para a 2ª via (sem CPF em claro: só o id do registro cifrado, PP-26). */
export const RECEIPTS_META = 'pdv.receipts'
export const MAX_STORED_RECEIPTS = 30

/** Impressora instalada neste aparelho pelo assistente, enquanto a sincronização não traz o cadastro (CA 7). */
export const INSTALLED_PRINTER_META = 'pdv.printer.installed'

export interface StoredReceipt {
  saleId: string
  number: string
  completedAt: string
  operatorId: string | null
  operatorName: string | null
  items: ReceiptItem[]
  totalCents: Cents
  changeCents: Cents
  payments: ReceiptPayment[]
  customerName: string | null
  /** Registro cifrado do CPF/CNPJ (PP-26); o documento só é decifrado na hora de imprimir, e sai mascarado. */
  documentId: string | null
  documentKind: 'cpf' | 'cnpj' | null
  /** Quantas vias já saíram (0 = nenhuma). A próxima é a 2ª via quando já saiu uma. */
  copies: number
}

/** Cabeçalho do cupom, do catálogo da loja (empresa e loja). */
export interface ReceiptHeader {
  companyName: string | null
  companyDocument: string | null
  storeName: string
}

const asList = (value: unknown): StoredReceipt[] => (Array.isArray(value) ? (value as StoredReceipt[]) : [])

export async function recentReceipts(store: Pick<LocalStore, 'getMeta'>): Promise<StoredReceipt[]> {
  return asList(await store.getMeta<StoredReceipt[]>(RECEIPTS_META))
}

/** Guarda a venda concluída para o cupom e a 2ª via (a mais nova primeiro, até 30). */
export async function rememberReceipt(store: Pick<LocalStore, 'updateMeta'>, receipt: StoredReceipt): Promise<void> {
  await store.updateMeta(RECEIPTS_META, (current) => [receipt, ...asList(current).filter((r) => r.saleId !== receipt.saleId)].slice(0, MAX_STORED_RECEIPTS))
}

/** A venda concluída como fica guardada para o cupom (itens, pagamentos capturados com a forma e a bandeira, cliente sem o documento em claro). */
export function storedReceiptOf(done: CompletedSale, operatorName: string | null): StoredReceipt | null {
  if (!done.saleId || !done.lines) return null
  // PDV-04: o desconto no total sai rateado nas linhas do cupom (a soma fecha o TOTAL), como no servidor.
  const shares = prorateCents(done.lines.map(lineTotal), done.discountCents ?? 0)
  return {
    saleId: done.saleId, number: done.number, completedAt: done.completedAt ?? new Date().toISOString(), operatorId: done.operatorId ?? null, operatorName,
    items: done.lines.map((l, i) => ({ name: l.name, quantity: l.quantity, unit: l.unit, fractional: l.fractional, unitCents: l.unitCents, note: l.note, discountCents: (l.discountCents ?? 0) + shares[i] })),
    totalCents: done.totalCents, changeCents: done.changeCents,
    payments: done.tenders.filter(isCaptured).map((t) => ({
      label: [t.methodName, t.cardBrand ? BRAND_LABEL[t.cardBrand] ?? t.cardBrand : null, t.installments && t.installments > 1 ? `${t.installments}x` : null].filter(Boolean).join(' '),
      amountCents: t.amountCents,
      methodType: t.methodType,
    })),
    customerName: done.customer?.customerName ?? null, documentId: done.customer?.documentId ?? null, documentKind: done.customer?.documentKind ?? null, copies: 0,
  }
}

/** Impressoras do caixa: o cadastro que desceu (CA 7) e, à frente, a instalada neste aparelho que ainda não voltou na sincronização. */
export function printersOf(catalog: readonly CatalogPrinter[], installed: CatalogPrinter | null | undefined): CatalogPrinter[] {
  const list = [...catalog]
  if (installed && !list.some((p) => p.id === installed.id)) list.unshift(installed)
  return list.sort((a, b) => Number(b.isDefault ?? false) - Number(a.isDefault ?? false))
}

/** O alvo da impressão: a impressora escolhida, a padrão do caixa ou, sem cadastro, a padrão do navegador (42 colunas). */
export function targetOf(printer: CatalogPrinter | null | undefined): PrinterTarget {
  if (!printer || !printer.columns || !printer.paperWidthMm) return BROWSER_DEFAULT_PRINTER
  const columns = ([32, 42, 48] as const).find((c) => c === printer.columns) ?? 42
  return { id: printer.id, name: printer.name, paperWidthMm: printer.paperWidthMm === 58 ? 58 : 80, columns }
}

export interface PrintSaleOptions {
  store: Pick<LocalStore, 'getMeta' | 'updateMeta'>
  bridge: HardwareBridge
  printer: PrinterTarget
  saleId: string
  header: ReceiptHeader
  /** Quem imprime agora (vai no evento: a reimpressão fica registrada com quem e quando, CA 4). */
  operatorId: string | null
  registerId: string | null
  /** Decifra o documento do consumidor (nunca vai para log nem fila). */
  decryptDocument?: (documentId: string) => Promise<string | null>
  now?: () => Date
}

export type PrintSaleResult = PrintOutcome & { copy?: number }

/**
 * Imprime o cupom da venda (CA 2) ou a 2ª via (CA 4): a primeira via que sai é a original; qualquer impressão depois dela sai com "2ª VIA" (3ª...).
 * Só conta como impressa a que a porta confirmou; aí, numa transação, a via sobe no registro do aparelho e o evento vai para a fila. A falha
 * nunca mexe na venda (CA 8): a tela oferece tentar de novo, outra impressora ou seguir sem imprimir.
 */
export async function printSaleReceipt(options: PrintSaleOptions): Promise<PrintSaleResult> {
  const receipts = await recentReceipts(options.store)
  const receipt = receipts.find((r) => r.saleId === options.saleId)
  if (!receipt) return { ok: false, message: 'Esta venda não está mais guardada neste aparelho para imprimir.' }
  const copy = receipt.copies + 1
  const at = (options.now?.() ?? new Date()).toISOString()
  let customerDocument: string | null = null
  if (receipt.documentId && receipt.documentKind && options.decryptDocument) {
    const plain = await options.decryptDocument(receipt.documentId).catch(() => null)
    customerDocument = plain ? receiptDocument(plain, receipt.documentKind) : null
  }

  const doc = buildSaleReceipt({
    ...options.header, number: receipt.number, completedAt: receipt.completedAt, operatorName: receipt.operatorName, items: receipt.items,
    totalCents: receipt.totalCents, payments: receipt.payments, changeCents: receipt.changeCents, customerName: receipt.customerName, customerDocument,
    copy, reprintedAt: copy > 1 ? at : null,
  })
  const outcome = await options.bridge.print(doc, options.printer)
  if (!outcome.ok) return outcome

  await options.store.updateMeta(RECEIPTS_META, (current) => asList(current).map((r) => (r.saleId === receipt.saleId ? { ...r, copies: copy } : r)), {
    kind: PRINT_OUTBOX_KIND,
    at,
    payload: {
      type: copy > 1 ? 'ReceiptReprinted' : 'ReceiptPrinted', id: uuidv7(Date.parse(at)), registerId: options.registerId, printerId: options.printer.id,
      kind: copy > 1 ? 'Reprint' : 'Receipt', saleId: receipt.saleId, saleNumber: receipt.number, copy, operatorId: options.operatorId, printedAt: at,
    },
  })
  return { ok: true, copy }
}

/** Imprime um documento que não é venda (comprovante do caixa, página de teste) pela mesma porta. */
export function printDocument(bridge: HardwareBridge, doc: ReceiptDocument, printer: PrinterTarget): Promise<PrintOutcome> {
  return bridge.print(doc, printer)
}
