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
  previsto: 'A parcela espera o marco acontecer para ganhar vencimento.',
  vencido: 'Título em aberto com vencimento antes de hoje. Vencimento em dia não útil passa a vencido só no dia seguinte.',
}

const AWAITS: Record<string, string> = { FabricationDone: 'fabricação concluída', Delivered: 'entrega', TechnicalAcceptance: 'aceite técnico', Signature: 'assinatura' }

/** "Aguarda: fabricação concluída" na parcela prevista do contrato (COM-04b). */
export const awaits = (r: Pick<ReceivableDetail, 'status' | 'triggerEvent'>) => (r.status === 'Planned' && r.triggerEvent ? `Aguarda: ${AWAITS[r.triggerEvent] ?? r.triggerEvent}` : null)

export const toNumber = (s: string | null | undefined) => (s === null || s === undefined || s === '' ? 0 : Number(s))

export const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

/** Título pré-preenchido por outro módulo (PAT-02 CA 7, venda do bem): só preenche; grava com a confirmação desta tela. */
export interface ReceivablePrefill {
  customer: { id: string; label: string; termId: null } | null
  description: string
  amount: string | null
  issueDate: string | null
}

const DATE = /^\d{4}-\d{2}-\d{2}$/

/** Lê `?cliente=&clienteNome=&valor=&emissao=&descricao=`; valor inválido ou data fora do padrão ficam vazios. */
export function receivablePrefill(search: Record<string, unknown>): ReceivablePrefill {
  const text = (v: unknown) => (typeof v === 'string' ? v.trim() : typeof v === 'number' ? String(v) : '')
  const id = text(search.cliente)
  const amount = Number(text(search.valor))
  const issue = text(search.emissao)
  return {
    customer: id ? { id, label: text(search.clienteNome) || 'Cliente escolhido', termId: null } : null,
    description: text(search.descricao).slice(0, 200),
    amount: Number.isFinite(amount) && amount > 0 ? String(amount) : null,
    issueDate: DATE.test(issue) ? issue : null,
  }
}
