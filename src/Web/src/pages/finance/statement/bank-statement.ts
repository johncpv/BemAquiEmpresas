import type { StatusTone } from '@/components/ui/status-chip'

/** Situação da linha do extrato do banco (CA 8): Pendente informativo, Conciliada ok, Ignorada neutro. */
export const LINE_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Pending: { label: 'Pendente', tone: 'info' },
  Reconciled: { label: 'Conciliada', tone: 'ok' },
  Ignored: { label: 'Ignorada', tone: 'neutral' },
}

export const LINE_KIND: Record<string, string> = {
  Pix: 'Pix', Boleto: 'Boleto', Ted: 'TED', Doc: 'DOC', Transfer: 'Transferência', Fee: 'Tarifa', Interest: 'Juros', Yield: 'Rendimento',
  Tax: 'Imposto', Card: 'Cartão', Check: 'Cheque', Deposit: 'Depósito', Reversal: 'Estorno', Other: 'Outro',
}

export const SOURCE_LABEL: Record<string, string> = { Api: 'API', Ofx: 'OFX' }

export const TRIGGER_LABEL: Record<string, string> = { Scheduled: 'Busca diária', Manual: 'Buscar agora', Upload: 'Arquivo OFX' }

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`

/** Resumo da importação (CA 5): "{n} novas, {m} já importadas, {k} recusadas". */
export function importSummary(r: { newCount: number; duplicateCount: number; rejectedCount: number }): string {
  return `${plural(r.newCount, 'nova', 'novas')}, ${plural(r.duplicateCount, 'já importada', 'já importadas')}, ${plural(r.rejectedCount, 'recusada', 'recusadas')}`
}

/** Próximo passo depois de importar (propositivo): conciliar as pendentes (BNK-03b). */
export function reconcileLabel(pending: number): string | null {
  return pending > 0 ? `Conciliar ${plural(pending, 'linha pendente', 'linhas pendentes')}` : null
}

/** "dd/mm" de uma data de negócio (yyyy-mm-dd), sem converter fuso. */
export function dayMonth(iso: string): string {
  const [, m, d] = iso.split('-')
  return `${d}/${m}`
}

/** Fases da busca no Inter (padrão 6): Enviado, Aguardando, Demorando (30 s), Confirmado ou Recusado com motivo e ação. */
export type FetchPhase = 'idle' | 'sent' | 'waiting' | 'slow' | 'confirmed' | 'refused'

export const FETCH_PHASE_TEXT: Record<'sent' | 'waiting' | 'slow', string> = {
  sent: 'Pedido enviado ao Inter…',
  waiting: 'Aguardando o Inter…',
  slow: 'O Inter está demorando mais que o normal. Você pode continuar; o extrato aparece aqui quando terminar.',
}

export const isBusy = (phase: FetchPhase) => phase === 'sent' || phase === 'waiting' || phase === 'slow'

/** Último dia permitido para a contingência (CA 6): hoje + 30 dias. */
export function contingencyMax(today: string, days = 30): string {
  const d = new Date(Date.parse(today + 'T12:00:00Z') + days * 86_400_000)
  return d.toISOString().slice(0, 10)
}
