import type { UserRow } from '@/api/generated/model'

/**
 * Usuário de operação (PDV-01 CA 12, ADR 0049): quem só usa o caixa, sem e-mail. Regras do PIN iguais às do servidor (PinRules): 6 números, os dois
 * iguais e nada óbvio (todos iguais, sequência, padrão repetido). A data de nascimento só o servidor confere.
 */
export const OPERATION_PROFILES = ['retail'] as const

export const OPERATION_EMAIL_TEXT = 'Sem e-mail · entra por PIN no caixa'

export function isObviousPin(pin: string): boolean {
  const digits = [...pin].map(Number)
  const steps = new Set(digits.slice(1).map((d, i) => (d - digits[i]! + 10) % 10))
  if (steps.size === 1 && [0, 1, 9].includes([...steps][0]!)) return true
  return (pin.slice(0, 2) === pin.slice(2, 4) && pin.slice(2, 4) === pin.slice(4)) || pin.slice(0, 3) === pin.slice(3)
}

/** Mensagem do problema do PIN, ou null quando serve. */
export function pinProblem(pin: string, confirmation: string): string | null {
  if (!/^\d{6}$/.test(pin)) return 'O PIN tem 6 números.'
  if (pin !== confirmation) return 'Os dois PINs não são iguais. Digite de novo.'
  return isObviousPin(pin) ? 'Esse PIN é fácil de adivinhar. Evite números repetidos e sequências.' : null
}

/** Linha de baixo do nome na lista de usuários: o e-mail, ou o aviso de que entra só por PIN. */
export const userContact = (u: Pick<UserRow, 'email' | 'isOperationUser'>) => (u.isOperationUser ? OPERATION_EMAIL_TEXT : u.email)
