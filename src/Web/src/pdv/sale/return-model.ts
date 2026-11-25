/** PDV-06: textos e regras puras da devolução no caixa. */
export const RETURN_NOT_FOUND = 'Nenhuma venda encontrada com este número ou CPF.'
export const STORE_CREDIT_HINT = 'O vale-troca é o reembolso padrão: o cliente leva um papel com código, valor e validade e usa como pagamento numa compra nesta loja. O que sobrar continua no mesmo vale. Precisa de internet para usar.'

/** Só dígitos (com pontuação) de 11 ou 14: é CPF/CNPJ (vai no corpo, nunca na URL); o resto é o número do cupom. */
export function lookupBody(text: string): { number: string | null; document: string | null } | null {
  const value = text.trim()
  if (!value) return null
  const digits = value.replace(/[.\-/\s]/g, '')
  return /^\d{11}$|^\d{14}$/.test(digits) ? { number: null, document: digits } : { number: value.toUpperCase(), document: null }
}
