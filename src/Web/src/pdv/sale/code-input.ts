/**
 * O que o operador digita (ou o leitor escreve) no campo de código (PDV-02 CA 6): `3*7891234567895` lança 3 do código; `1,5*789...` lança
 * 1,5 (pesável). Sem `*`, a quantidade vem da embalagem do código (CA 2) ou é 1. Só dígitos é código de barras; com letras, é busca pelo nome.
 */
export interface CodeInput {
  /** Quantidade antes do `*` (texto como digitado) ou null. */
  quantity: string | null
  /** O resto: código ou texto da busca. */
  text: string
  /** Só dígitos (código de barras, PLU). */
  isCode: boolean
}

export function parseCodeInput(raw: string): CodeInput {
  const value = raw.trim()
  const match = /^(\d+(?:[.,]\d+)?)\s*\*\s*(.*)$/.exec(value)
  const quantity = match ? match[1] : null
  const text = (match ? match[2] : value).trim()
  return { quantity, text, isCode: /^\d+$/.test(text) }
}
