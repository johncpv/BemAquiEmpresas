/**
 * Máscaras e documentos brasileiros (FND-09d CA 3 e 4). A máscara é só de exibição: o formulário recebe o valor
 * normalizado (sem pontuação). "9" é dígito e "X" é letra ou dígito (CNPJ alfanumérico, IN RFB 2.229/2024).
 */
export function applyPattern(value: string, pattern: string): string {
  let out = ''
  let i = 0
  for (const slot of pattern) {
    if (i >= value.length) break
    if (slot === '9' || slot === 'X') {
      out += value[i++]
    } else {
      out += slot
    }
  }
  return out
}

export const onlyDigits = (value: string) => value.replace(/\D/g, '')
export const onlyAlnum = (value: string) => value.replace(/[^0-9a-z]/gi, '').toUpperCase()

export interface MaskSpec {
  /** Valor normalizado a partir do que foi digitado ou colado. */
  normalize: (raw: string) => string
  format: (normalized: string) => string
  inputMode: 'numeric' | 'text' | 'tel'
  maxLength: number
}

const digits = (pattern: string, inputMode: MaskSpec['inputMode'] = 'numeric'): MaskSpec => {
  const size = [...pattern].filter((c) => c === '9').length
  return { normalize: (raw) => onlyDigits(raw).slice(0, size), format: (v) => applyPattern(v, pattern), inputMode, maxLength: pattern.length }
}

export const CPF_PATTERN = '999.999.999-99'
export const CNPJ_PATTERN = 'XX.XXX.XXX/XXXX-99'

/** CPF ou CNPJ no mesmo campo: até 11 dígitos é CPF; com letra ou mais de 11 caracteres, CNPJ (FND-09d CA 3). */
export const documentMask: MaskSpec = {
  normalize: (raw) => onlyAlnum(raw).slice(0, 14),
  format: (v) => (/^\d{0,11}$/.test(v) ? applyPattern(v, CPF_PATTERN) : applyPattern(v, CNPJ_PATTERN)),
  inputMode: 'text',
  maxLength: CNPJ_PATTERN.length,
}

export const cnpjMask: MaskSpec = { ...documentMask, format: (v) => applyPattern(v, CNPJ_PATTERN) }
export const cpfMask = digits(CPF_PATTERN)
export const cepMask = digits('99999-999')
export const ncmMask = digits('9999.99.99')
export const cestMask = digits('99.999.99')
export const cboMask = digits('9999-99')

/** Telefone fixo (10 dígitos) ou celular (11), com DDD. */
export const phoneMask: MaskSpec = {
  normalize: (raw) => onlyDigits(raw).slice(0, 11),
  format: (v) => applyPattern(v, v.length > 10 ? '(99) 99999-9999' : '(99) 9999-9999'),
  inputMode: 'tel',
  maxLength: 15,
}

export type DocumentKind = 'PF' | 'PJ'

/** PF ou PJ pelo tamanho e pelo conteúdo: 11 dígitos é CPF; 14 caracteres é CNPJ. */
export function documentKind(normalized: string): DocumentKind | null {
  if (/^\d{11}$/.test(normalized)) return 'PF'
  if (/^[0-9A-Z]{12}\d{2}$/.test(normalized)) return 'PJ'
  return null
}

const allSame = (value: string) => /^(.)\1*$/.test(value)

export function isValidCpf(value: string): boolean {
  const cpf = onlyDigits(value)
  if (cpf.length !== 11 || allSame(cpf)) return false
  const dv = (size: number) => {
    let sum = 0
    for (let i = 0; i < size; i++) sum += Number(cpf[i]) * (size + 1 - i)
    const rest = (sum * 10) % 11
    return rest === 10 ? 0 : rest
  }
  return dv(9) === Number(cpf[9]) && dv(10) === Number(cpf[10])
}

/**
 * CNPJ numérico ou alfanumérico (IN RFB 2.229/2024): cada caractere vale o código ASCII menos 48 ("0" = 0, "A" = 17)
 * e os dois dígitos verificadores seguem o módulo 11 de sempre.
 */
export function isValidCnpj(value: string): boolean {
  const cnpj = onlyAlnum(value)
  if (!/^[0-9A-Z]{12}\d{2}$/.test(cnpj) || allSame(cnpj)) return false
  const dv = (size: number) => {
    const weights = size === 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]
    let sum = 0
    for (let i = 0; i < size; i++) sum += (cnpj.charCodeAt(i) - 48) * weights[i]
    const rest = sum % 11
    return rest < 2 ? 0 : 11 - rest
  }
  return dv(12) === Number(cnpj[12]) && dv(13) === Number(cnpj[13])
}

export function isValidDocument(value: string): boolean {
  const kind = documentKind(onlyAlnum(value))
  return kind === 'PF' ? isValidCpf(value) : kind === 'PJ' ? isValidCnpj(value) : false
}
