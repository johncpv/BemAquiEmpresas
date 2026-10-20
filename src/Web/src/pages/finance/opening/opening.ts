import type { OpeningStatus } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import type { ComboOption } from '@/components/form/combobox'
import { formatBrDecimal } from '@/lib/decimal'
import { useCan } from '@/pages/use-can'

/** Passos da implantação (FIN-00), na ordem: o id é a aba na URL e a aba da ajuda. */
export const OPENING_STEPS = [
  { id: 'data-de-corte', label: 'Data de corte' },
  { id: 'saldos', label: 'Saldos das contas' },
  { id: 'receber', label: 'A receber em aberto' },
  { id: 'pagar', label: 'A pagar em aberto' },
  { id: 'conferencia', label: 'Conferência' },
] as const

export const OPENING_DICAS = {
  corte: 'O dia em que o BemAqui começa. Tudo o que estava em aberto no fim desse dia entra aqui; o histórico anterior fica no sistema antigo.',
  fimDoDia: 'O saldo do extrato no fim do dia de corte, depois do último lançamento do dia.',
  implantacao: 'Título que já estava em aberto na data de corte, lançado pelo saldo. Não entra no resultado por competência nem na pontualidade.',
}

/** Ação do Financeiro liberada para o usuário (o mesmo critério do <Can>). */
export function useCanFinance(action: string) {
  return useCan('finance.' + action)
}

/** Passo atual do progresso: o primeiro que falta. */
export function currentStep(status: OpeningStatus) {
  if (status.completedAt) return OPENING_STEPS.length - 1
  if (!status.cutoverDate) return 0
  if (status.accounts.every((a) => a.openingBalance === null || a.openingBalance === undefined)) return 1
  return status.receivables.length === 0 ? 2 : status.payables.length === 0 ? 3 : 4
}

/** Linha do lote: o que o editor guarda (dinheiro como string normalizada, datas aaaa-mm-dd). */
export interface OpeningLineDraft {
  party: ComboOption | null
  description: string
  reference: string
  issueDate: string
  dueDate: string
  balance: string | null
  accountId: string
  costCenterId: string
}

export const emptyLine = (): OpeningLineDraft => ({ party: null, description: '', reference: '', issueDate: '', dueDate: '', balance: null, accountId: '', costCenterId: '' })

/** Erros do 422 por linha ("lines[3].balance" → linha 3), cada linha com a primeira mensagem. */
export function lineErrors(error: unknown): { byLine: Record<number, string>; general?: string } {
  if (!(error instanceof ApiError)) return { byLine: {}, general: 'Não foi possível lançar os títulos.' }
  const byLine: Record<number, string> = {}
  for (const [path, messages] of Object.entries(error.fields)) {
    const match = /^lines\[(\d+)\]/.exec(path)
    if (match && byLine[Number(match[1])] === undefined) byLine[Number(match[1])] = messages[0]
  }

  return { byLine, general: Object.keys(byLine).length ? undefined : error.message }
}

/** Erros locais antes de enviar (a API confere de novo). */
export function localLineErrors(lines: OpeningLineDraft[], toNumber: (v: string | null) => number) {
  const byLine: Record<number, string> = {}
  lines.forEach((l, i) => {
    if (!l.party) byLine[i] = 'Escolha o terceiro.'
    else if (!l.description.trim()) byLine[i] = 'Informe a descrição.'
    else if (!l.dueDate) byLine[i] = 'Informe o vencimento.'
    else if (!(toNumber(l.balance) > 0)) byLine[i] = 'Informe um saldo maior que zero.'
  })
  return byLine
}

const cell = (v: string) => (/[;"\n]/.test(v) ? `"${v.replaceAll('"', '""')}"` : v)

/** Conferência em CSV (CA 9), com ";" e vírgula decimal, que é o que o Excel em português abre direto. */
export function reconciliationCsv(status: OpeningStatus, statement: Record<string, string | null>) {
  const money = (v: number | null | undefined) => (v === null || v === undefined ? '' : formatBrDecimal(String(v), 2))
  const rows: string[][] = [['Tipo', 'Nome', 'Quantidade', 'Saldo do sistema', 'Saldo no extrato', 'Diferença']]
  for (const a of status.accounts) {
    const typed = statement[a.id]
    const diff = typed === null || typed === undefined ? null : Math.round((Number(typed) - a.systemBalanceAtCutover) * 100) / 100
    rows.push(['Conta', a.name, '', money(a.systemBalanceAtCutover), money(typed === null || typed === undefined ? null : Number(typed)), money(diff)])
  }

  for (const r of status.receivables) rows.push(['A receber', r.partyName, String(r.count), money(r.total), '', ''])
  for (const p of status.payables) rows.push(['A pagar', p.partyName, String(p.count), money(p.total), '', ''])
  return rows.map((r) => r.map(cell).join(';')).join('\r\n')
}
