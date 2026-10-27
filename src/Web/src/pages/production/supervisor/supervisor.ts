import { formatDate } from '@/lib/format'

/** Textos de "Como calculamos" e das dicas do painel do supervisor (DSH-03, Telas e estados). */
export const SUPERVISOR_HOW = {
  board: 'As colunas do Kanban por etapa, com quantas OS estão nela e as horas padrão que faltam. Atrasada: a data prometida já passou. Em risco: a entrega prevista pelo Planejamento passou da prometida.',
  capacity: 'Horas comprometidas pelo Planejamento divididas pelas horas que cada centro tem na semana. Folga abaixo de 60%, Lotado de 95% a 100%, Acima da capacidade quando passa de 100%.',
  quality: 'As etapas de qualidade prontas para decidir que você pode decidir (sem as OS em que você apontou uma etapa da tentativa atual) e as não conformidades ainda não encerradas, da mais antiga para a mais nova.',
  shortages: 'Material que falta reservar nas OS abertas. Bloqueia quando a próxima etapa da OS está prevista para começar em até 5 dias úteis do calendário padrão. A compra é lida das solicitações e dos pedidos.',
  indicators: 'Últimos 30 dias, contando hoje. Horas paradas: minutos das pausas por motivo ÷ 60 (intervalo e fim de turno não contam). Taxa de NC: reprovações ÷ decisões da etapa de qualidade.',
} as const

export const SUPERVISOR_DICAS = {
  taxaNc: 'Reprovações da etapa de qualidade divididas pelo total de decisões (aprovações mais reprovações) nos últimos 30 dias, com uma casa.',
  horasParadas: 'Soma dos minutos das pausas por Material, Máquina e Outro nos últimos 30 dias, dividida por 60. Intervalo e fim de turno não são parada.',
  bloqueia: 'Dias úteis, pelo calendário padrão, até o início previsto da próxima etapa da OS. Até 5 dias úteis, a falta bloqueia a produção.',
  autoinspecao: 'Você fez parte do trabalho e não há outro aprovador: a aprovação leva a marca Autoinspeção e o Proprietário é avisado.',
} as const

/** Ordem a 400 px (Telas e estados): Qualidade → Faltas → Quadro → Capacidade → Indicadores; no desktop, a ordem do HTML. */
export const MOBILE_ORDER: Record<string, string> = {
  quality: 'order-1',
  shortages: 'order-2',
  board: 'order-3',
  capacity: 'order-4',
  indicators: 'order-5',
}

/** Ordem no desktop: o quadro em cima, qualidade e faltas lado a lado, capacidade e indicadores embaixo. */
export const DESKTOP = ['board', 'quality', 'shortages', 'capacity', 'indicators'] as const

export const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`

/** "Bloqueia hoje", "Bloqueia em 1 dia", "Bloqueia em 3 dias" (dias úteis). */
export function blocksLabel(days: number | null | undefined) {
  if (days === null || days === undefined) return null
  return days <= 0 ? 'Bloqueia hoje' : `Bloqueia em ${plural(days, 'dia', 'dias')}`
}

/** "{n} etapas aguardam a qualidade" (sem production.approve_quality, CA 4). */
export const pendingLabel = (n: number) => (n === 1 ? '1 etapa aguarda a qualidade' : `${n} etapas aguardam a qualidade`)

/** Horas com 1 casa ("1,5 h"). */
export const hoursText = (value: number) => `${value.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} h`

/** Percentual com 1 casa ("25,0%"); sem decisão, "—". */
export const percentText = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : `${value.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`

/** Quantidade sem zeros à direita ("6", "2,5"). */
export const quantityText = (value: number) => value.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

/** "há 5 dias", "hoje", "1 dia". */
export const ageText = (days: number) => (days <= 0 ? 'Hoje' : plural(days, 'dia', 'dias'))

/** Busca do Kanban filtrado (CA 2): os chips levam a ?atrasadas=1 ou ?risco=1 na mesma linha. */
export function kanbanSearch(filter: 'late' | 'risk' | null, line: string | undefined) {
  return {
    ...(filter === 'late' ? { atrasadas: '1' } : filter === 'risk' ? { risco: '1' } : {}),
    ...(line ? { linha: line } : {}),
  }
}

/** Faixa do cartão atrasado ou em risco, sempre com texto. */
export function riskText(card: { risk: string; delayDays?: number | null; promisedDate?: string | null }) {
  if (card.risk === 'Late') return { tone: 'crit' as const, text: `Atrasada ${card.delayDays ?? 0} d` }
  return { tone: 'warn' as const, text: card.promisedDate ? `Em risco, prometido ${formatDate(card.promisedDate)}` : 'Em risco' }
}

/** "Sem 44" a partir do número ISO da semana. */
export const weekLabel = (isoWeek: number) => `Sem ${isoWeek}`

/** Link para gerar a solicitação de compra já preenchida (CMP-01): a OS e o item. */
export const newRequestSearch = (orderId: string, itemId: string) => ({ os: orderId, item: itemId })
