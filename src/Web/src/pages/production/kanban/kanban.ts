import type { BoardCard } from '@/api/generated/model'
import { formatDate } from '@/lib/format'

/** Textos das dicas do Kanban (Telas e estados da PRD-02). */
export const KANBAN_DICAS = {
  emRisco: 'A entrega prevista do projeto (o plano, mais o prazo de instalação) passou da data prometida ao cliente, mesmo que por um dia. O plano se refaz sozinho a cada apontamento.',
  horasRestantes: 'Soma do que falta das horas padrão das etapas atuais desta coluna: padrão menos apontadas, nunca abaixo de zero.',
  concluirEtapa: 'Use quando a etapa terminou sem apontamento. Fica registrado quem concluiu e por quê.',
} as const

export const MAX_REASON = 200

/** Horas em 1 casa, com vírgula ("12,5 h"). */
export const hoursText = (value: number) => `${value.toLocaleString('pt-BR', { maximumFractionDigits: 1 })} h`

/**
 * Faixa lateral sempre com texto (padrão 2 da Fase 3): vermelha "Atrasada {n} d", âmbar "Em risco, prometido {data}" (PLN-01b: previsão de entrega depois da prometida).
 * Sem atraso nem risco, sem faixa.
 */
export function stripe(card: Pick<BoardCard, 'risk' | 'delayDays' | 'promisedDate'>): { tone: 'crit' | 'warn'; text: string } | null {
  if (card.risk === 'Late') return { tone: 'crit', text: `Atrasada ${card.delayDays ?? 0} d` }
  if (card.risk === 'AtRisk' && card.promisedDate) return { tone: 'warn', text: `Em risco, prometido ${formatDate(card.promisedDate)}` }
  return null
}

/** Prazo do cartão sem faixa: "Prometido {data}" (a previsão do plano vem logo abaixo, PLN-01a). */
export const promisedText = (card: Pick<BoardCard, 'promisedDate'>) => (card.promisedDate ? `Prometido ${formatDate(card.promisedDate)}` : 'Sem data prometida')

/** Medidor horas apontadas × padrão (CA 3): acima do padrão, vermelho com "+{h} h acima do padrão". */
export function meter(card: Pick<BoardCard, 'appliedHours' | 'standardHours'>) {
  const over = card.appliedHours - card.standardHours
  const ratio = card.standardHours > 0 ? Math.min(card.appliedHours / card.standardHours, 1) : card.appliedHours > 0 ? 1 : 0
  return { percent: Math.round(ratio * 100), over: over > 0 ? `+${hoursText(over)} acima do padrão` : null }
}

/** "Concluir etapa" no menu (CA 8): só a etapa atual que não é de qualidade, em OS ainda não pronta para concluir. */
export const canCompleteStep = (card: Pick<BoardCard, 'currentStepId' | 'currentStepIsQuality' | 'readyToComplete' | 'noRouting'>) =>
  !!card.currentStepId && !card.currentStepIsQuality && !card.readyToComplete && !card.noRouting

/** Rótulo do item do menu: "Concluir etapa {letra} {nome}". */
export const completeLabel = (card: Pick<BoardCard, 'currentStepCode' | 'currentStepName'>) => `Concluir etapa ${card.currentStepCode ?? ''} ${card.currentStepName ?? ''}`.replace(/\s+/g, ' ').trim()

/** Filtros do Kanban na URL (CA 6): projeto, cliente, linha, roteiro, atrasadas e em risco. */
export const KANBAN_FILTERS = ['projeto', 'cliente', 'linha', 'roteiro', 'atrasadas', 'risco', 'ver', 'coluna'] as const

export function boardParams(f: Record<string, string | undefined>) {
  return {
    projectId: f.projeto || undefined,
    partyId: f.cliente || undefined,
    line: f.linha || undefined,
    routingTemplateId: f.roteiro || undefined,
    late: f.atrasadas === '1' ? true : undefined,
    atRisk: f.risco === '1' ? true : undefined,
  }
}

export const hasFilters = (f: Record<string, string | undefined>) => !!(f.projeto || f.cliente || f.linha || f.roteiro || f.atrasadas === '1' || f.risco === '1')
