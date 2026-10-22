import type { StatusTone } from '@/components/ui/status-chip'

/** Situação da OS de fabricação (04-fluxos §1). */
export const FABRICATION_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Open: { label: 'Aberta', tone: 'info' },
  InProgress: { label: 'Em produção', tone: 'warn' },
  Paused: { label: 'Pausada', tone: 'warn' },
  Quality: { label: 'Aguardando qualidade', tone: 'info' },
  Completed: { label: 'Concluída', tone: 'ok' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
}

/** Rótulos do apontamento (PRD-03a CA 31): como o apontamento fechou. */
export const CLOSE_KIND: Record<string, string> = {
  Operator: 'Finalizado pelo operador', Left: 'Saiu da etapa', Supervisor: 'Encerrado pelo supervisor', StepCompleted: 'Encerrado por',
}

export const STEP_STATUS: Record<string, string> = {
  Waiting: 'Aguardando', Ready: 'Pronta', InProgress: 'Em andamento', Paused: 'Pausada', Done: 'Concluída', Rejected: 'Reprovada',
}

/** Filtro de situação na URL (?situacao=aberta). */
export const FABRICATION_SITUATIONS = [
  { value: 'aberta', label: 'Abertas' },
  { value: 'andamento', label: 'Em produção' },
  { value: 'concluida', label: 'Concluídas' },
  { value: 'cancelada', label: 'Canceladas' },
]

/** Textos das dicas (Telas e estados da PRD-01). */
export const FABRICATION_DICAS = {
  horas: 'Horas de trabalho esperadas para uma unidade nesta etapa, copiadas do roteiro. Mudar aqui não muda o roteiro.',
  qualidade: 'Etapa em que a peça é inspecionada. Ao chegar nela, a OS passa para Qualidade.',
  semRoteiro: 'Nenhum roteiro foi sugerido para este item. Escolha o roteiro para a OS ganhar as etapas.',
  gerarOs: 'Equipamento gera OS sozinho. Componente fabricado, pelo botão. Serviço e contrato de implantação não geram.',
  prometido: 'Data prometida ao cliente no projeto. Passou da data e a OS não foi concluída, ela aparece como atrasada.',
} as const

/** Faixa lateral com texto (padrão 2): "Atrasada {n} d". */
export const delayLabel = (days: number | null | undefined) => (days ? `Atrasada ${days} d` : null)

/** Horas em 2 casas, com vírgula. */
export const hours = (value: number) => value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

export const quantity = (value: number) => value.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

/** A OS ainda pode mudar as etapas: aberta e sem etapa iniciada (CA 7 e 8). */
export const canChangeSteps = (order: { status: string; started: boolean }) => order.status === 'Open' && !order.started

/** Cancelar (CA 16 e 17): a não iniciada com production.edit; a iniciada só com production.approve. */
export function canCancel(order: { status: string; started: boolean }, canEdit: boolean, canApprove: boolean) {
  if (order.status === 'Completed' || order.status === 'Cancelled') return false
  return order.started ? canApprove : canEdit
}

/** Impacto da troca de roteiro (CA 9): etapas e horas, antes → depois. */
export function routingImpact(current: { steps: number; hours: number }, next: { steps: number; hours: number }) {
  return `${current.steps} → ${next.steps} etapas · ${hours(current.hours)} h → ${hours(next.hours)} h padrão`
}