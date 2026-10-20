import type { ReceivableDetail } from '@/api/generated/model'

export type Tone = 'ok' | 'warn' | 'crit' | 'info' | 'neutral'

/** Situação exibida (mapa de chips da Fase 2, FIN-01 CA 6 e 7): "Vencido" e "Vence hoje" são calculados no servidor. */
export function situation(r: Pick<ReceivableDetail, 'status' | 'isOverdue' | 'isDueToday'>): { label: string; tone: Tone } {
  if (r.isOverdue) return { label: 'Vencido', tone: 'crit' }
  if (r.isDueToday) return { label: 'Vence hoje', tone: 'warn' }
  switch (r.status) {
    case 'Planned': return { label: 'Previsto', tone: 'neutral' }
    case 'PartiallyPaid': return { label: 'Parcial', tone: 'info' }
    case 'Paid': return { label: 'Pago', tone: 'ok' }
    case 'Cancelled': return { label: 'Cancelado', tone: 'neutral' }
    default: return { label: 'Em aberto', tone: 'info' }
  }
}

export const SITUATION_OPTIONS = [
  { value: 'aberto', label: 'Em aberto' },
  { value: 'vencido', label: 'Vencidos' },
  { value: 'previsto', label: 'Previstos' },
  { value: 'pago', label: 'Pagos' },
  { value: 'cancelado', label: 'Cancelados' },
]

export const RECEIVABLE_DICAS = {
  principal: 'A parte do valor do título que está sendo paga. Juros e multa entram à parte; o desconto abate o que entra na conta.',
  juros: 'Valor de juros cobrado pelo atraso, informado à mão. Entra na conta junto com o principal.',
  multa: 'Multa por atraso, informada à mão. Entra na conta junto com o principal.',
  desconto: 'Valor abatido nesta baixa. Acima do limite de Configurações → Parâmetros, só quem aprova pode confirmar.',
  quitar: 'Zera o saldo que o cliente não vai pagar (ex.: centavos), sem movimentar a conta. Conta como desconto.',
  vencido: 'Título em aberto com vencimento antes de hoje. Vencimento em dia não útil passa a vencido só no dia seguinte.',
}

export const toNumber = (s: string | null | undefined) => (s === null || s === undefined || s === '' ? 0 : Number(s))

export const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })
