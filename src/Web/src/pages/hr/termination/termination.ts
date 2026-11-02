/** Rescisão (FLH-05): dicas, tons e as contas do formulário "Simular rescisão", sem chamada à API. */
import type { StatusTone } from '@/components/ui/status-chip'

export const TERMINATION_DICAS = {
  avisoIndenizado: 'Na dispensa, o aviso prévio que o funcionário não trabalha é pago: 30 dias mais 3 por ano completo de casa, até 90 (Lei 12.506/2011). No acordo, metade. Os dias contam no 13º e nas férias proporcionais.',
  multa: 'Multa que a empresa recolhe sobre o saldo do FGTS: 40% na dispensa sem justa causa, 20% no acordo, nada no pedido de demissão e na justa causa. Não sai do líquido do funcionário.',
  saldoFgts: 'O saldo para fins rescisórios do extrato do FGTS do funcionário (FGTS Digital ou Caixa). O sistema soma os depósitos desta rescisão e calcula a multa.',
} as const

export const NOTICE_LABELS: Record<string, string> = { Worked: 'Trabalhado', Indemnified: 'Indenizado', Waived: 'Dispensado' }

export const STATUS_TONES: Record<string, StatusTone> = { Draft: 'neutral', Calculated: 'info', Closed: 'ok', Cancelled: 'neutral' }

export type TerminationDraft = { kind: string; terminationDate: string; noticeKind: string; contractEndDate: string; fgtsBalance: string; fgtsStatementDate: string }

/** Número digitado ("1.234,56" ou "1234.56") → número; vazio é null. */
export function parseMoney(text: string): number | null {
  const t = text.trim()
  if (!t) return null
  const normalized = t.includes(',') ? t.replace(/\./g, '').replace(',', '.') : t
  const n = Number(normalized)
  return Number.isFinite(n) ? n : NaN
}

/** Erro local do formulário, antes da API (a validação completa é do servidor, com a matriz). */
export function terminationDraftError(d: TerminationDraft, opts: { needsContractEnd: boolean; hasFine: boolean; calculate: boolean }): string | null {
  if (!d.kind) return 'Escolha o tipo do desligamento.'
  if (!d.terminationDate) return 'Escolha o tipo e a data para simular.'
  if (opts.needsContractEnd && (!d.contractEndDate || d.contractEndDate <= d.terminationDate)) return 'Informe o fim previsto do contrato de experiência, depois do desligamento.'
  const balance = parseMoney(d.fgtsBalance)
  if (Number.isNaN(balance) || (balance !== null && balance < 0)) return 'Informe o saldo do FGTS do extrato, zero ou mais.'
  if (opts.calculate && opts.hasFine && balance === null) return 'Informe o saldo do FGTS do extrato para calcular a multa.'
  if (balance !== null && !d.fgtsStatementDate) return 'Informe a data do extrato do FGTS.'
  return null
}

/** O pedido para a API (datas e números no formato da API, vazios como null). */
export function terminationBody(d: TerminationDraft, needsContractEnd: boolean) {
  const balance = parseMoney(d.fgtsBalance)
  return {
    kind: d.kind as never,
    noticeKind: (d.noticeKind || null) as never,
    terminationDate: d.terminationDate || null,
    contractEndDate: needsContractEnd ? d.contractEndDate || null : null,
    fgtsBalance: balance === null || Number.isNaN(balance) ? null : balance,
    fgtsStatementDate: balance === null ? null : d.fgtsStatementDate || null,
  }
}
