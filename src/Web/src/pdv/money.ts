/**
 * Dinheiro no aparelho de caixa (OFF-01 CA 8): todo valor é guardado e calculado em **centavos inteiros** (`number` com
 * `Number.isSafeInteger`). Nada de ponto flutuante em conta de dinheiro: 0,1 + 0,2 em reais dá 0,30000000000000004; em centavos,
 * 10 + 20 dá 30. A conversão do texto decimal da API (`decimal(18,2)`) para centavos é feita pelo texto, sem multiplicar float.
 */
export type Cents = number

export class MoneyError extends Error {}

function assertCents(value: number, what = 'valor'): Cents {
  if (!Number.isSafeInteger(value)) throw new MoneyError(`O ${what} em centavos tem de ser inteiro: ${value}.`)
  return value
}

/**
 * Converte um valor decimal em reais (texto "12.34", "-0.5", "1234" ou número com até 2 casas) para centavos inteiros. Mais de 2 casas
 * decimais é erro: o servidor manda `decimal(18,2)`, e arredondar aqui esconderia um contrato quebrado.
 */
export function toCents(value: string | number): Cents {
  const text = typeof value === 'number' ? numberText(value) : value.trim()
  const match = /^(-)?(\d+)(?:[.,](\d{1,2}))?$/.exec(text)
  if (!match) throw new MoneyError(`Valor em dinheiro inválido: "${value}".`)
  const [, minus, whole, fraction = ''] = match
  const cents = Number(whole) * 100 + Number(fraction.padEnd(2, '0'))
  return assertCents(minus && cents !== 0 ? -cents : cents)
}

function numberText(value: number): string {
  if (!Number.isFinite(value)) throw new MoneyError(`Valor em dinheiro inválido: ${value}.`)
  // toString de número com até 2 casas é exato o bastante para o texto (ex.: 12.34 -> "12.34"); 1e21 e afins caem no erro do regex.
  return String(value)
}

/** Centavos de volta para o texto decimal da API ("12.34"), para enviar ao servidor. */
export function centsToDecimal(cents: Cents): string {
  assertCents(cents)
  const sign = cents < 0 ? '-' : ''
  const abs = Math.abs(cents)
  return `${sign}${Math.trunc(abs / 100)}.${String(abs % 100).padStart(2, '0')}`
}

/** Soma de centavos (inteira e conferida). */
export function sumCents(values: readonly Cents[]): Cents {
  let total = 0
  for (const v of values) total += assertCents(v)
  return assertCents(total, 'total')
}

/**
 * Preço unitário em centavos × quantidade (até 4 casas, como `decimal(18,4)`), arredondado meio para cima longe do zero, como o
 * `Math.Round(..., MidpointRounding.AwayFromZero)` do servidor. A quantidade vira inteiro em décimos de milésimo para não multiplicar float.
 */
export function lineTotalCents(unitCents: Cents, quantity: string | number): Cents {
  assertCents(unitCents, 'preço unitário')
  const text = typeof quantity === 'number' ? String(quantity) : quantity.trim()
  const match = /^(\d+)(?:[.,](\d{1,4}))?$/.exec(text)
  if (!match) throw new MoneyError(`Quantidade inválida: "${quantity}".`)
  const scaled = Number(match[1]) * 10_000 + Number((match[2] ?? '').padEnd(4, '0'))
  const product = unitCents * scaled
  assertCents(product, 'total da linha')
  const sign = product < 0 ? -1 : 1
  const abs = Math.abs(product)
  const rounded = Math.trunc(abs / 10_000) + (abs % 10_000 >= 5_000 ? 1 : 0)
  return sign * rounded
}

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

/** "R$ 12,34" para a tela (a conta nunca usa o texto formatado). */
export function formatCents(cents: Cents): string {
  assertCents(cents)
  return brl.format(cents / 100)
}
