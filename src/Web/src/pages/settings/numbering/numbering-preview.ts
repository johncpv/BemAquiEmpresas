import type { NumberFormatRow } from '@/api/generated/model'

export interface NumberingDraft {
  prefix: string
  yearly: boolean
  digits: number
  /** "Próximo número" digitado; vazio continua do último emitido. */
  next: string
}

export const toDraft = (row: NumberFormatRow): NumberingDraft => ({ prefix: row.prefix, yearly: row.yearly, digits: row.digits, next: '' })

/** Ano da prévia: o do servidor, quando a linha já reinicia por ano (evita divergir na virada); senão, o de Brasília. */
function previewYear(row: NumberFormatRow) {
  const fromServer = row.yearly ? Number(row.next.split('-').at(-2)) : NaN
  return Number.isFinite(fromServer) ? fromServer : Number(new Intl.DateTimeFormat('en', { year: 'numeric', timeZone: 'America/Sao_Paulo' }).format(new Date()))
}

/** Prévia do próximo número, igual ao NumberFormat.Format do servidor (CAD-06 CA 9 e 10). */
export function previewNext(row: NumberFormatRow, draft: NumberingDraft) {
  const typed = Number(draft.next)
  const value = draft.next && Number.isInteger(typed) && typed > 0 ? typed : row.lastIssued + 1
  const prefix = draft.prefix.trim().toUpperCase() || row.prefix
  const digits = draft.digits >= 3 && draft.digits <= 8 ? draft.digits : row.digits
  const number = String(value).padStart(digits, '0')
  return draft.yearly ? `${prefix}-${previewYear(row)}-${number}` : `${prefix}-${number}`
}

/** Erros que a tela antecipa; o servidor confere de novo (CA 11 e 13). */
export function draftErrors(row: NumberFormatRow, draft: NumberingDraft): Partial<Record<keyof NumberingDraft, string>> {
  const errors: Partial<Record<keyof NumberingDraft, string>> = {}
  if (!/^[A-Z0-9]{1,6}$/.test(draft.prefix.trim().toUpperCase())) errors.prefix = 'O prefixo tem de 1 a 6 letras ou números, sem espaço nem acento.'
  if (!Number.isInteger(draft.digits) || draft.digits < 3 || draft.digits > 8) errors.digits = 'Use de 3 a 8 dígitos.'
  if (draft.next) {
    const n = Number(draft.next)
    if (!Number.isInteger(n) || n <= 0) errors.next = 'Informe um número inteiro.'
    else if (draft.yearly === row.yearly && n <= row.lastIssued) errors.next = `O próximo número precisa ser maior que o último emitido (${row.lastIssued}).`
    else if (String(n).length > draft.digits) errors.next = `O próximo número não cabe em ${draft.digits} dígitos.`
  }
  return errors
}
