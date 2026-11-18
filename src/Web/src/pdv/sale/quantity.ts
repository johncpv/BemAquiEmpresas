/**
 * Quantidade na venda do caixa (PDV-02 CA 9): guardada como texto decimal com ponto ("2", "1.25", "0.375"), o formato que o `lineTotalCents`
 * multiplica sem ponto flutuante. Produto pesável (ou por metro, litro) aceita até 3 casas; produto por unidade só aceita inteiro.
 */
export const MAX_QUANTITY_MILLI = 99_999_000

export type QuantityResult = { ok: true; value: string } | { ok: false; message: string }

/** Milésimos inteiros de um texto decimal com até 3 casas ("1,5" → 1500); null se não for número. */
export function toMilli(text: string): number | null {
  const match = /^(\d{1,9})(?:[.,](\d{1,3}))?$/.exec(text.trim())
  if (!match) return null
  return Number(match[1]) * 1000 + Number((match[2] ?? '').padEnd(3, '0'))
}

/** Milésimos de volta para o texto da venda, sem zeros à direita ("1500" → "1.5"). */
export function fromMilli(milli: number): string {
  const whole = Math.trunc(milli / 1000)
  const fraction = String(milli % 1000).padStart(3, '0').replace(/0+$/, '')
  return fraction ? `${whole}.${fraction}` : String(whole)
}

/** Confere a quantidade digitada para o produto. */
export function parseQuantity(text: string, fractional: boolean): QuantityResult {
  const trimmed = text.trim()
  if (!fractional && /[.,]/.test(trimmed) && !/[.,]0*$/.test(trimmed)) return { ok: false, message: 'Este produto é vendido por unidade: use um número inteiro.' }
  if (fractional && /[.,]\d{4,}$/.test(trimmed)) return { ok: false, message: 'Use no máximo 3 casas depois da vírgula (ex.: 1,250).' }
  const milli = toMilli(trimmed)
  if (milli === null) return { ok: false, message: 'Digite a quantidade em números (ex.: 2 ou 1,250).' }
  if (milli <= 0) return { ok: false, message: 'A quantidade tem de ser maior que zero.' }
  if (milli > MAX_QUANTITY_MILLI) return { ok: false, message: 'Quantidade grande demais para uma linha.' }
  return { ok: true, value: fromMilli(milli) }
}

/** Soma de duas quantidades da venda (texto com ponto). */
export function addQuantities(a: string, b: string): string {
  return fromMilli((toMilli(a) ?? 0) + (toMilli(b) ?? 0))
}

/** Diferença a − b (nunca negativa). */
export function subtractQuantities(a: string, b: string): string {
  return fromMilli(Math.max(0, (toMilli(a) ?? 0) - (toMilli(b) ?? 0)))
}

/** A quantidade da embalagem do código (número do catálogo) como texto da venda; inválida ou ausente vale 1. */
export function packQuantityText(pack: number | undefined, fractional: boolean): string {
  if (pack === undefined || !Number.isFinite(pack) || pack <= 0) return '1'
  const parsed = parseQuantity(String(pack), fractional || !Number.isInteger(pack))
  return parsed.ok ? parsed.value : '1'
}

/** "1,25" para a tela (pesável com 3 casas: "1,250"). */
export function formatQuantity(quantity: string, fractional: boolean): string {
  const milli = toMilli(quantity) ?? 0
  return (milli / 1000).toLocaleString('pt-BR', fractional ? { minimumFractionDigits: 3, maximumFractionDigits: 3 } : { maximumFractionDigits: 3 })
}
