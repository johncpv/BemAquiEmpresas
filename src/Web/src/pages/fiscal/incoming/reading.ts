import type { StatusTone } from '@/components/ui/status-chip'

/**
 * Leitura assistida e "Digitar a nota" (FIS-05 CA 7 a 9): os campos do formulário, a situação em texto, quantos faltam conferir, o
 * primeiro campo a conferir (o foco vai para ele) e a soma dos itens. Os valores ficam em texto, como o usuário digita (vírgula decimal).
 */

export type FieldState = 'Read' | 'Uncertain' | 'NotFound'

export type FieldKind = 'key' | 'date' | 'text' | 'document' | 'money' | 'quantity' | 'price' | 'ncm' | 'unit'

export type FieldDef = { key: string; label: string; kind: FieldKind; hint?: string }

export const HEADER_FIELDS: FieldDef[] = [
  { key: 'accessKey', label: 'Chave de acesso', kind: 'key', hint: '44 dígitos, na DANFE em cima do código de barras' },
  { key: 'issuedAt', label: 'Data de emissão', kind: 'date' },
  { key: 'issuerName', label: 'Emitente (razão social)', kind: 'text' },
  { key: 'recipientDocument', label: 'CNPJ do destinatário', kind: 'document', hint: 'Tem de ser o CNPJ desta empresa' },
]

export const TOTAL_FIELDS: FieldDef[] = [
  { key: 'totals.products', label: 'Valor total dos produtos', kind: 'money' },
  { key: 'totals.freight', label: 'Valor do frete', kind: 'money' },
  { key: 'totals.insurance', label: 'Valor do seguro', kind: 'money' },
  { key: 'totals.discount', label: 'Desconto', kind: 'money' },
  { key: 'totals.otherExpenses', label: 'Outras despesas', kind: 'money' },
  { key: 'totals.ipi', label: 'Valor do IPI', kind: 'money' },
  { key: 'totals.icmsBase', label: 'Base de cálculo do ICMS', kind: 'money' },
  { key: 'totals.icms', label: 'Valor do ICMS', kind: 'money' },
  { key: 'totals.icmsStBase', label: 'Base de cálculo do ICMS-ST', kind: 'money' },
  { key: 'totals.icmsSt', label: 'Valor do ICMS-ST', kind: 'money' },
  { key: 'totals.total', label: 'Valor total da nota', kind: 'money' },
]

export const ITEM_FIELDS: { field: string; label: string; kind: FieldKind }[] = [
  { field: 'code', label: 'Código', kind: 'text' },
  { field: 'description', label: 'Descrição', kind: 'text' },
  { field: 'ncm', label: 'NCM', kind: 'ncm' },
  { field: 'unit', label: 'Unidade', kind: 'unit' },
  { field: 'quantity', label: 'Quantidade', kind: 'quantity' },
  { field: 'unitPrice', label: 'Valor unitário', kind: 'price' },
  { field: 'amount', label: 'Valor total', kind: 'money' },
]

export const itemKey = (index: number, field: string) => `items.${index}.${field}`

/** Todas as chaves a conferir, na ordem da tela (o servidor exige as mesmas). */
export function requiredKeys(items: number): string[] {
  return [
    ...HEADER_FIELDS.map((f) => f.key),
    ...TOTAL_FIELDS.map((f) => f.key),
    ...Array.from({ length: items }, (_, i) => ITEM_FIELDS.map((f) => itemKey(i, f.field))).flat(),
  ]
}

/** Situação em texto (nunca só a cor, CA 7) e o tom do chip. */
export const STATE_LABEL: Record<FieldState, { label: string; tone: StatusTone }> = {
  Read: { label: 'Lido', tone: 'ok' },
  Uncertain: { label: 'Leitura incerta: confira', tone: 'warn' },
  NotFound: { label: 'Não encontrado', tone: 'crit' },
}

export const DIGITADO = { label: 'Digite', tone: 'info' as StatusTone }

/** "{n} campos a conferir" (rodapé fixo, `aria-live`). */
export function pendingLabel(count: number) {
  if (count === 0) return 'Todos os campos conferidos'
  return count === 1 ? '1 campo a conferir' : `${count} campos a conferir`
}

export function pendingCount(keys: string[], confirmed: ReadonlySet<string>) {
  return keys.filter((k) => !confirmed.has(k)).length
}

/** O foco vai para o primeiro campo incerto ou não encontrado; sem nenhum, o primeiro ainda não conferido. */
export function firstToCheck(keys: string[], states: Readonly<Record<string, FieldState | undefined>>, confirmed: ReadonlySet<string>): string | undefined {
  return keys.find((k) => !confirmed.has(k) && (states[k] === 'Uncertain' || states[k] === 'NotFound' || states[k] === undefined))
    ?? keys.find((k) => !confirmed.has(k))
}

/** Número do servidor ("1234.5") para o campo (vírgula decimal). */
export function toInput(kind: FieldKind, value: string | null | undefined): string {
  if (value == null) return ''
  return kind === 'money' || kind === 'quantity' || kind === 'price' ? value.replace('.', ',') : value
}

/** Número digitado ("1.234,56", "10", "12,5") para o servidor; null se não for número. */
export function parseNumber(text: string): number | null {
  const value = text.trim().replace(/^R\$\s*/, '')
  if (!/^(\d{1,3}(\.\d{3})+|\d+)(,\d+)?$/.test(value) && !/^\d+(\.\d+)?$/.test(value)) return null
  const normalized = value.includes(',') ? value.replace(/\./g, '').replace(',', '.') : value
  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : null
}

export const digits = (text: string) => text.replace(/\D/g, '')

/** Dígito verificador da chave (módulo 11): a tela avisa antes de enviar. */
export function isValidAccessKey(text: string): boolean {
  const key = digits(text)
  if (key.length !== 44) return false
  let sum = 0
  let weight = 2
  for (let i = 42; i >= 0; i--) {
    sum += Number(key[i]) * weight
    weight = weight === 9 ? 2 : weight + 1
  }
  const rest = sum % 11
  return Number(key[43]) === (rest < 2 ? 0 : 11 - rest)
}

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

/** "A soma dos itens (R$ X) não bate com o total da nota (R$ Y)." (CA 8), ou null quando bate. */
export function sumMismatch(values: Readonly<Record<string, string>>, items: number): string | null {
  const products = parseNumber(values['totals.products'] ?? '')
  if (products == null || items === 0) return null
  const cents = Array.from({ length: items }, (_, i) => Math.round((parseNumber(values[itemKey(i, 'amount')] ?? '') ?? 0) * 100)).reduce((a, b) => a + b, 0)
  const total = Math.round(products * 100)
  return cents === total ? null : `A soma dos itens (${brl.format(cents / 100)}) não bate com o total da nota (${brl.format(total / 100)}).`
}

/** Tira o item da posição e renumera as chaves dos seguintes (valores, situação e conferência). */
export function removeItem<T>(map: Readonly<Record<string, T>>, index: number, count: number): Record<string, T> {
  const next: Record<string, T> = {}
  for (const [key, value] of Object.entries(map)) {
    const m = /^items\.(\d+)\.(.+)$/.exec(key)
    if (!m) {
      next[key] = value
      continue
    }
    const i = Number(m[1])
    if (i < index) next[key] = value
    else if (i > index && i < count) next[itemKey(i - 1, m[2])] = value
  }
  return next
}

export function removeConfirmed(confirmed: ReadonlySet<string>, index: number, count: number): Set<string> {
  const asMap = Object.fromEntries([...confirmed].map((k) => [k, true]))
  return new Set(Object.keys(removeItem(asMap, index, count)))
}

/** Corpo do "Conferir nota" a partir do formulário. */
export function toRequest(values: Readonly<Record<string, string>>, items: number, confirmed: ReadonlySet<string>) {
  const n = (key: string) => parseNumber(values[key] ?? '')
  const t = (key: string) => (values[key] ?? '').trim() || null
  return {
    accessKey: digits(values.accessKey ?? '') || null,
    issuedOn: t('issuedAt'),
    issuerName: t('issuerName'),
    recipientDocument: digits(values.recipientDocument ?? '') || null,
    totals: {
      products: n('totals.products'), freight: n('totals.freight') ?? 0, insurance: n('totals.insurance') ?? 0, discount: n('totals.discount') ?? 0,
      otherExpenses: n('totals.otherExpenses') ?? 0, ipi: n('totals.ipi') ?? 0, icmsBase: n('totals.icmsBase') ?? 0, icms: n('totals.icms') ?? 0,
      icmsStBase: n('totals.icmsStBase') ?? 0, icmsSt: n('totals.icmsSt') ?? 0, total: n('totals.total'),
    },
    items: Array.from({ length: items }, (_, i) => ({
      code: t(itemKey(i, 'code')), description: t(itemKey(i, 'description')), ncm: digits(values[itemKey(i, 'ncm')] ?? '') || null, unit: t(itemKey(i, 'unit')),
      quantity: n(itemKey(i, 'quantity')), unitPrice: n(itemKey(i, 'unitPrice')), amount: n(itemKey(i, 'amount')),
    })),
    confirmed: requiredKeys(items).filter((k) => confirmed.has(k)),
  }
}

/** Situação da leitura que não serviu (CA 3 e estados da story). */
export function failedTitle(result: string | null | undefined) {
  switch (result) {
    case 'NoText': return 'PDF sem texto para ler'
    case 'Unsafe': return 'PDF recusado por segurança'
    case 'TimedOut': return 'A leitura falhou por tempo'
    case 'TooManyPages': return 'PDF com páginas demais'
    default: return 'Não conseguimos ler este PDF'
  }
}

/** Marca da nota sem XML (CA 8 e 9), em todas as telas. */
export const SOURCE_MARK: Record<string, string> = { Pdf: 'Lida do PDF, sem XML', Manual: 'Digitada, sem XML' }

export const READING_DICAS = {
  pdf: 'A nota foi lida do PDF da DANFE e conferida campo a campo, mas o sistema não tem o XML: não há assinatura nem protocolo para conferir. Peça o XML ao fornecedor e use Substituir pelo XML quando chegar.',
  manual: 'A nota foi digitada e conferida campo a campo, sem o XML: não há assinatura nem protocolo para conferir. Peça o XML ao fornecedor e use Substituir pelo XML quando chegar.',
  incerta: 'O leitor achou um valor, mas não tem certeza (a soma não bate, o número está fora do lugar). Confira com a DANFE e corrija se precisar.',
} as const

/** Até 5 MB, só PDF (CA 1); o servidor confere o resto (antivírus, conteúdo ativo). */
export const MAX_PDF_BYTES = 5 * 1024 * 1024

export function precheckPdf(file: { name: string; size: number }): string | null {
  if (!file.name.toLowerCase().endsWith('.pdf')) return 'Envie o PDF da DANFE (arquivo .pdf).'
  if (file.size === 0) return 'O arquivo está vazio.'
  if (file.size > MAX_PDF_BYTES) return 'O PDF precisa ter até 5 MB.'
  return null
}
