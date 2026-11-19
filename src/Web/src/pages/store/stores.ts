/** Regras puras da tela Lojas e caixas (PDV-01), espelho das do servidor (PosRules) para avisar antes de enviar. */

/** Código da loja: 2 a 6 letras ou números, em maiúsculas (entra no número da venda: LOJA-01.1-000001). */
export function normalizeStoreCode(value: string): string {
  return value.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-zA-Z0-9]/g, '').toUpperCase().slice(0, 6)
}

export const isValidStoreCode = (code: string) => /^[A-Z0-9]{2,6}$/.test(code)

/** Número do caixa como a tela mostra: 01, 02... */
export const registerLabel = (n: number) => String(n).padStart(2, '0')

/** Próximo número livre na loja. */
export function nextRegisterNumber(used: readonly number[]): number {
  for (let n = 1; n <= 99; n++) if (!used.includes(n)) return n
  return 99
}

/** A partir de 2 h sem contato, o chip avisa (o caixa pode estar desligado ou sem internet). */
export const NO_CONTACT_WARNING_HOURS = 2

export function contactText(hours: number): string | null {
  if (hours < NO_CONTACT_WARNING_HOURS) return null
  if (hours < 48) return `Sem contato há ${hours} h`
  return `Sem contato há ${Math.floor(hours / 24)} dias`
}

export const UI_MODE_LABEL: Record<string, string> = { Touch: 'Toque', Keyboard: 'Teclado e leitor' }

/** Valor em reais para o campo ("1.000,00"). */
export const moneyText = (value: number) => value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** Texto em reais ("1.000,00", "1000", "1000.5") para número; undefined se não for valor válido com até 2 casas (CX-01, aviso e teto do caixa). */
export function parseAmount(text: string): number | undefined {
  const clean = text.replace(/\s|R\$/g, '')
  const normalized = clean.includes(',') ? clean.replace(/\./g, '').replace(',', '.') : clean
  if (!/^\d{1,7}(\.\d{1,2})?$/.test(normalized)) return undefined
  return Number(normalized)
}
