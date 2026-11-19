import { type Cents, formatCents, lineTotalCents } from '@/pdv/money'
import { formatQuantity } from '@/pdv/sale/quantity'

/**
 * Modelo estruturado do que sai na impressora do caixa (IMP-01, rascunho B item 1): linhas de texto, pares "descrição … valor", réguas, QR Code,
 * corte e gaveta. Nesta story o modelo vira HTML para a impressão do navegador (`BrowserPrintBridge`); na IMP-02 o **mesmo** modelo vira bytes
 * ESC/POS. Tudo é montado no aparelho, do dado local, sem servidor.
 */
export type ReceiptLine =
  | { kind: 'text'; text: string; align?: 'left' | 'center' | 'right'; bold?: boolean; large?: boolean }
  | { kind: 'pair'; left: string; right: string; bold?: boolean }
  | { kind: 'rule'; char?: '-' | '=' }
  | { kind: 'blank' }
  | { kind: 'qr'; data: string; caption?: string }
  | { kind: 'cut' }
  | { kind: 'drawer' }

export interface ReceiptDocument {
  /** Título do trabalho de impressão (aparece na fila do sistema). */
  title: string
  lines: ReceiptLine[]
}

/** Colunas que a bobina comporta (CA 3): 58 mm com 32; 80 mm com 42 ou 48 (depende da fonte do modelo). */
export type ReceiptColumns = 32 | 42 | 48
export const RECEIPT_COLUMNS: readonly ReceiptColumns[] = [32, 42, 48]

/** Linha pronta para a impressora: o texto já cabe nas colunas (nunca passa delas). */
export type LaidOutRow =
  | { kind: 'text'; text: string; bold?: boolean; large?: boolean }
  | { kind: 'qr'; data: string }
  | { kind: 'cut' }
  | { kind: 'drawer' }

/** "Não é documento fiscal" é fixo no modelo do recibo (regra da story): o tenant não remove nem edita. */
export const NOT_FISCAL_TEXT = 'NÃO É DOCUMENTO FISCAL'

/** Valor para a bobina: "R$ 1.234,56" com espaço comum (o `Intl` põe espaço não separável, que algumas impressoras trocam por "?"). */
export const receiptMoney = (cents: Cents) => formatCents(cents).replace(/\s/g, ' ')

/** Quebra o texto em linhas de até `width`, por palavra; palavra maior que a linha é cortada em pedaços (nunca some texto). */
export function wrapText(text: string, width: number): string[] {
  const out: string[] = []
  for (const paragraph of text.split('\n')) {
    let line = ''
    for (const word of paragraph.split(/\s+/).filter(Boolean)) {
      let rest = word
      while (rest.length > width) {
        if (line) { out.push(line); line = '' }
        out.push(rest.slice(0, width))
        rest = rest.slice(width)
      }
      if (!rest) continue
      if (!line) line = rest
      else if (line.length + 1 + rest.length <= width) line += ' ' + rest
      else { out.push(line); line = rest }
    }
    out.push(line)
  }
  return out
}

function align(text: string, width: number, how: 'left' | 'center' | 'right'): string {
  if (how === 'left' || text.length >= width) return text
  const pad = width - text.length
  return how === 'right' ? ' '.repeat(pad) + text : ' '.repeat(Math.floor(pad / 2)) + text
}

/**
 * Monta as linhas para `columns` colunas (CA 3): texto quebrado por palavra; o par "descrição … valor" põe o valor à direita na mesma linha quando
 * cabe e, quando não cabe, quebra a descrição e põe o valor sozinho, alinhado à direita, na linha de baixo. Nenhum texto nem valor é cortado.
 * Texto grande (dobro da largura) usa metade das colunas.
 */
export function layoutReceipt(doc: ReceiptDocument, columns: ReceiptColumns): LaidOutRow[] {
  const rows: LaidOutRow[] = []
  for (const line of doc.lines) {
    switch (line.kind) {
      case 'text': {
        const width = line.large ? Math.floor(columns / 2) : columns
        for (const part of wrapText(line.text, width)) rows.push({ kind: 'text', text: align(part, width, line.align ?? 'left'), bold: line.bold, large: line.large })
        break
      }
      case 'pair': {
        const right = line.right
        if (line.left.length + 1 + right.length <= columns) {
          rows.push({ kind: 'text', text: line.left + ' '.repeat(columns - line.left.length - right.length) + right, bold: line.bold })
        } else {
          for (const part of wrapText(line.left, columns)) rows.push({ kind: 'text', text: part, bold: line.bold })
          for (const part of wrapText(right, columns)) rows.push({ kind: 'text', text: align(part, columns, 'right'), bold: line.bold })
        }
        break
      }
      case 'rule':
        rows.push({ kind: 'text', text: (line.char ?? '-').repeat(columns) })
        break
      case 'blank':
        rows.push({ kind: 'text', text: '' })
        break
      case 'qr':
        rows.push({ kind: 'qr', data: line.data })
        if (line.caption) for (const part of wrapText(line.caption, columns)) rows.push({ kind: 'text', text: align(part, columns, 'center') })
        break
      case 'cut':
        rows.push({ kind: 'cut' })
        break
      case 'drawer':
        rows.push({ kind: 'drawer' })
        break
    }
  }
  return rows
}

/** Linha da venda para o recibo: descrição, quantidade com unidade, preço unitário e observação. */
export interface ReceiptItem {
  name: string
  quantity: string
  unit: string
  fractional: boolean
  unitCents: Cents
  /** Desconto da linha (a PDV-04 traz o desconto; até lá, 0). */
  discountCents?: Cents
  note?: string | null
}

export interface ReceiptPayment {
  label: string
  amountCents: Cents
}

/** Dados da venda concluída para o cupom (CA 2), todos do aparelho. */
export interface SaleReceiptData {
  companyName: string | null
  companyDocument: string | null
  storeName: string
  /** CNPJ próprio da loja (estabelecimento, NFCE-07); ausente, sai só o da empresa. */
  storeDocument?: string | null
  number: string
  completedAt: string
  operatorName: string | null
  items: ReceiptItem[]
  totalCents: Cents
  payments: ReceiptPayment[]
  changeCents: Cents
  customerName?: string | null
  /** Documento do consumidor já mascarado para o papel (PP-26): o CPF nunca sai inteiro no cupom não fiscal. */
  customerDocument?: string | null
  /** 1 = original; 2 = "2ª VIA" (CA 4). */
  copy: number
  /** Instante da reimpressão (na 2ª via). */
  reprintedAt?: string | null
}

const dateTime = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' }).replace(',', '')

/** "12.345.678/0001-95" para o cabeçalho. */
export function formatCnpj(value: string): string {
  const v = value.replace(/[^0-9A-Za-z]/g, '').toUpperCase()
  return v.length === 14 ? `${v.slice(0, 2)}.${v.slice(2, 5)}.${v.slice(5, 8)}/${v.slice(8, 12)}-${v.slice(12)}` : value
}

/**
 * CPF mascarado para o papel (PP-26: o CPF serve à nota e à devolução; no cupom não fiscal não precisa sair inteiro): "***.456.789-**", o
 * padrão de máscara da LGPD. CNPJ do consumidor (empresa) sai inteiro.
 */
export function receiptDocument(value: string, kind: 'cpf' | 'cnpj'): string {
  const digits = value.replace(/\D/g, '')
  if (kind === 'cnpj') return formatCnpj(value)
  return digits.length === 11 ? `***.${digits.slice(3, 6)}.${digits.slice(6, 9)}-**` : '***'
}

const ordinal = (copy: number) => `${copy}ª VIA`

/** O cupom da venda (CA 2 e 4): cabeçalho, itens, totais, pagamentos, troco, consumidor e, em destaque, "NÃO É DOCUMENTO FISCAL". */
export function buildSaleReceipt(data: SaleReceiptData): ReceiptDocument {
  const lines: ReceiptLine[] = []
  const company = data.companyName?.trim() || data.storeName
  lines.push({ kind: 'text', text: company, align: 'center', bold: true })
  if (data.companyDocument) lines.push({ kind: 'text', text: `CNPJ ${formatCnpj(data.companyDocument)}`, align: 'center' })
  if (company !== data.storeName) lines.push({ kind: 'text', text: data.storeName, align: 'center' })
  if (data.storeDocument) lines.push({ kind: 'text', text: `CNPJ da loja ${formatCnpj(data.storeDocument)}`, align: 'center' })
  lines.push({ kind: 'rule', char: '=' })
  lines.push({ kind: 'text', text: NOT_FISCAL_TEXT, align: 'center', bold: true, large: true })
  lines.push({ kind: 'rule', char: '=' })
  if (data.copy > 1) {
    lines.push({ kind: 'text', text: ordinal(data.copy), align: 'center', bold: true, large: true })
    if (data.reprintedAt) lines.push({ kind: 'text', text: `Reimpresso em ${dateTime(data.reprintedAt)}`, align: 'center' })
  }
  lines.push({ kind: 'pair', left: 'Venda', right: data.number })
  lines.push({ kind: 'pair', left: 'Data', right: dateTime(data.completedAt) })
  if (data.operatorName) lines.push({ kind: 'pair', left: 'Operador', right: data.operatorName })
  lines.push({ kind: 'rule' })
  lines.push({ kind: 'pair', left: 'Item', right: 'Total' })
  let discount = 0
  data.items.forEach((item, index) => {
    const gross = lineTotalCents(item.unitCents, item.quantity)
    discount += item.discountCents ?? 0
    lines.push({ kind: 'text', text: `${String(index + 1).padStart(3, '0')} ${item.name}` })
    lines.push({ kind: 'pair', left: `    ${formatQuantity(item.quantity, item.fractional)} ${item.unit} x ${receiptMoney(item.unitCents)}`, right: receiptMoney(gross) })
    if (item.discountCents) lines.push({ kind: 'pair', left: '    Desconto', right: `-${receiptMoney(item.discountCents)}` })
    if (item.note) lines.push({ kind: 'text', text: `    Obs.: ${item.note}` })
  })
  lines.push({ kind: 'rule' })
  lines.push({ kind: 'pair', left: `Itens: ${data.items.length}`, right: '' })
  if (discount > 0) lines.push({ kind: 'pair', left: 'Descontos', right: `-${receiptMoney(discount)}` })
  lines.push({ kind: 'pair', left: 'TOTAL', right: receiptMoney(data.totalCents), bold: true })
  for (const p of data.payments) lines.push({ kind: 'pair', left: p.label, right: receiptMoney(p.amountCents) })
  if (data.changeCents > 0) lines.push({ kind: 'pair', left: 'Troco', right: receiptMoney(data.changeCents), bold: true })
  if (data.customerName || data.customerDocument) {
    lines.push({ kind: 'rule' })
    if (data.customerName) lines.push({ kind: 'pair', left: 'Cliente', right: data.customerName })
    if (data.customerDocument) lines.push({ kind: 'pair', left: 'CPF/CNPJ', right: data.customerDocument })
  }
  lines.push({ kind: 'rule', char: '=' })
  lines.push({ kind: 'text', text: NOT_FISCAL_TEXT, align: 'center', bold: true })
  lines.push({ kind: 'text', text: 'Obrigado pela preferência!', align: 'center' })
  lines.push({ kind: 'blank' })
  lines.push({ kind: 'cut' })
  return { title: data.copy > 1 ? `Cupom ${data.number} (${ordinal(data.copy)})` : `Cupom ${data.number}`, lines }
}

/** Página de teste do assistente (CA 6): régua de colunas, acentos, valores alinhados, QR Code e corte. */
export function buildTestPage(input: { printerName: string; columns: ReceiptColumns; paperWidthMm: number; at: string }): ReceiptDocument {
  const ruler = Array.from({ length: input.columns }, (_, i) => String((i + 1) % 10)).join('')
  return {
    title: `Teste da impressora ${input.printerName}`,
    lines: [
      { kind: 'text', text: 'TESTE DE IMPRESSÃO', align: 'center', bold: true, large: true },
      { kind: 'text', text: `${input.printerName} · ${input.paperWidthMm} mm · ${input.columns} colunas`, align: 'center' },
      { kind: 'text', text: dateTime(input.at), align: 'center' },
      { kind: 'rule', char: '=' },
      { kind: 'text', text: 'Régua: a última coluna tem de aparecer inteira.' },
      { kind: 'text', text: ruler },
      { kind: 'text', text: `${'1234567890'.repeat(Math.ceil(input.columns / 10)).slice(0, input.columns - 1)}|` },
      { kind: 'rule' },
      { kind: 'text', text: 'Acentos: ÁÉÍÓÚ áéíóú ÂÊÔ âêô ÃÕ ãõ Ç ç À à Ü ü' },
      { kind: 'text', text: 'Símbolos: R$ % º ª ° @ # & / \\ * + = < >' },
      { kind: 'rule' },
      { kind: 'pair', left: 'Valor alinhado', right: receiptMoney(123456) },
      { kind: 'pair', left: 'Valor grande', right: receiptMoney(999999999) },
      { kind: 'rule' },
      { kind: 'qr', data: 'https://bemaqui.com.br', caption: 'O QR Code tem de ser lido pela câmera do celular.' },
      { kind: 'rule' },
      { kind: 'text', text: 'Abaixo, o corte do papel (quando a impressora corta sozinha).', align: 'center' },
      { kind: 'blank' },
      { kind: 'cut' },
    ],
  }
}

/** Comprovante de sangria ou suprimento (CX-01 CA 4 e 5), uma via por bloco, separado por corte. */
export function buildCashReceipt(input: {
  kind: 'CashOut' | 'CashIn'; place: string; at: string; amountCents: Cents; reason: string; operatorName: string | null
}): ReceiptDocument {
  const copies = input.kind === 'CashOut' ? ['1ª via — caixa', '2ª via — quem retira'] : ['Via única']
  const lines: ReceiptLine[] = []
  for (const copy of copies) {
    lines.push({ kind: 'text', text: input.kind === 'CashOut' ? 'COMPROVANTE DE SANGRIA' : 'COMPROVANTE DE SUPRIMENTO', align: 'center', bold: true })
    lines.push({ kind: 'text', text: copy, align: 'center' })
    lines.push({ kind: 'text', text: input.place, align: 'center' })
    lines.push({ kind: 'rule' })
    lines.push({ kind: 'pair', left: 'Data', right: dateTime(input.at) })
    lines.push({ kind: 'pair', left: 'Valor', right: receiptMoney(input.amountCents), bold: true })
    lines.push({ kind: 'text', text: `Motivo: ${input.reason}` })
    if (input.operatorName) lines.push({ kind: 'pair', left: 'Operador', right: input.operatorName })
    if (input.kind === 'CashOut') {
      lines.push({ kind: 'blank' }, { kind: 'blank' }, { kind: 'text', text: '______________________________', align: 'center' }, { kind: 'text', text: 'Assinatura de quem retira', align: 'center' })
    }
    lines.push({ kind: 'text', text: NOT_FISCAL_TEXT, align: 'center' })
    lines.push({ kind: 'blank' }, { kind: 'cut' })
  }
  return { title: input.kind === 'CashOut' ? 'Comprovante de sangria' : 'Comprovante de suprimento', lines }
}
