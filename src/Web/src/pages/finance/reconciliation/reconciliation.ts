import type { StatusTone } from '@/components/ui/status-chip'

/** Confiança da sugestão (CA 1): Alta (ok) e Média (alerta). */
export const CONFIDENCE: Record<string, { label: string; tone: StatusTone }> = {
  High: { label: 'Alta', tone: 'ok' },
  Medium: { label: 'Média', tone: 'warn' },
}

/** Como a conciliação foi feita. */
export const KIND_LABEL: Record<string, string> = {
  Suggested: 'Sugestão aceita',
  Manual: 'Escolhida à mão',
  BankEntry: 'Lançamento sem OP',
  Settlement: 'Baixa de título',
}

/** Tipos do lançamento sem OP que a conciliação aceita (CA 8): na saída, só tarifa, IOF e juros. */
export const ENTRY_KINDS = [
  { value: 'BankFee', label: 'Tarifa bancária', out: true },
  { value: 'Iof', label: 'IOF', out: true },
  { value: 'InterestPaid', label: 'Juros pagos', out: true },
  { value: 'InterestReceived', label: 'Juros recebidos', out: false },
  { value: 'Yield', label: 'Rendimento', out: false },
  { value: 'OtherIn', label: 'Outra entrada', out: false },
] as const

export type EntryKindValue = (typeof ENTRY_KINDS)[number]['value']

export const entryKindsFor = (amount: number) => ENTRY_KINDS.filter((k) => k.out === amount < 0)

const brl = (v: number) => v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })

/** Soma do vínculo (CA 4), a mesma regra do servidor: nulo quando bate. */
export function sumMismatch(lines: number[], transactions: number[]): string | null {
  if (lines.length === 0 || transactions.length === 0) return 'Escolha a linha do banco e ao menos uma transação do sistema.'
  if (lines.length > 1 && transactions.length > 1) return 'Concilie uma linha com várias transações, ou várias linhas com uma transação.'
  const round = (v: number) => Math.round(v * 100) / 100
  const l = round(lines.reduce((a, b) => a + b, 0))
  const t = round(transactions.reduce((a, b) => a + b, 0))
  if (l === t) return null
  return lines.length === 1 ? `A soma das transações (${brl(t)}) não bate com a linha (${brl(l)}).` : `A soma das linhas (${brl(l)}) não bate com a transação (${brl(t)}).`
}

/** "dd/mm/aaaa" de uma data de negócio (yyyy-mm-dd), sem converter fuso. */
export function fullDate(iso: string): string {
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

/** Dia seguinte (yyyy-mm-dd) e o mês que começa nele: o próximo período depois de fechar (propositivo). */
export function nextPeriod(until: string): { from: string; to: string } {
  const next = new Date(Date.parse(until + 'T12:00:00Z') + 86_400_000)
  const from = next.toISOString().slice(0, 10)
  const end = new Date(Date.UTC(next.getUTCFullYear(), next.getUTCMonth() + 1, 0)).toISOString().slice(0, 10)
  return { from, to: end }
}

/** Palavra sugerida para "Criar regra a partir desta linha": a primeira palavra útil da descrição. */
export function ruleKeyword(description: string): string {
  const words = description.toUpperCase().split(/[^A-Z0-9ÇÃÕÁÉÍÓÚÂÊÔ]+/).filter((w) => w.length >= 3 && !['PIX', 'TED', 'DOC', 'DEB', 'CRED'].includes(w))
  return words[0] ?? description.trim().slice(0, 20)
}

/** Recebível de cartão em aberto, como a lista do depósito precisa (CX-03 CA 8). */
export type CardOption = { id: string; partyId: string; balance: number }

export const CARD_DEPOSIT_DICA =
  'Recebido − previsto. Depósito menor que o previsto: o último recebível (pelo vencimento) fica com a diferença em aberto, para conferir com o extrato da maquininha.'

const cents = (n: number) => Math.round(n * 100) / 100

/**
 * Totais do depósito da maquininha (CX-03 CA 8): o líquido previsto dos escolhidos, a diferença (recebido − previsto) e se são de uma maquininha só.
 * `suggested`: os primeiros recebíveis (na ordem da lista, por vencimento) da maquininha do primeiro, até cobrir o depósito.
 */
export function cardDeposit(items: CardOption[], chosen: string[], amount: number) {
  const picked = items.filter((i) => chosen.includes(i.id))
  const expected = cents(picked.reduce((s, i) => s + i.balance, 0))
  const suggested: string[] = []
  let sum = 0
  for (const i of items) {
    if (sum >= amount || i.partyId !== items[0]?.partyId) continue
    suggested.push(i.id)
    sum = cents(sum + i.balance)
  }
  return { count: picked.length, expected, difference: cents(amount - expected), sameParty: new Set(picked.map((i) => i.partyId)).size <= 1, suggested }
}
