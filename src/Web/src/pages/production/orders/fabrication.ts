import type { StatusTone } from '@/components/ui/status-chip'

/** Situação da OS de fabricação (04-fluxos §1). */
export const FABRICATION_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Open: { label: 'Aberta', tone: 'info' },
  InProgress: { label: 'Em produção', tone: 'warn' },
  Paused: { label: 'Pausada', tone: 'warn' },
  Quality: { label: 'Aguardando qualidade', tone: 'info' },
  ReadyToComplete: { label: 'Pronta para concluir', tone: 'info' },
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
  { value: 'pronta', label: 'Prontas para concluir' },
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
  pronta: 'Todas as etapas e a qualidade foram feitas. Falta o supervisor concluir a OS.',
  estoque: 'A entrada do produto acabado no estoque, com as séries e os lotes, chega numa próxima versão. Até lá, concluir a OS só muda o projeto e libera a parcela.',
} as const

/** Parcela "fabricação concluída" no diálogo de impacto (PRD-06a1 CA 2 e 10). */
export interface CompletionMilestone { description: string; amount?: number | null; dueDate?: string | null; released: boolean; received: boolean }

/**
 * Linhas do impacto de "Concluir OS" (PRD-06a1 CA 2): a OS conclui; se é a última OS de fabricação do projeto em produção, o projeto passa a
 * Fabricado e as parcelas ainda previstas são liberadas (valor e vencimento só com finance.view). O valor vem formatado por quem chama.
 */
export function completionImpact(p: { orderNumber: string; lastFabricationOrder: boolean; otherOpenOrders: number; projectNumber: string; projectStatus: string; showAmounts: boolean; milestones: CompletionMilestone[] },
  money: (value: number) => string, date: (iso: string) => string): string[] {
  const lines = [`A ${p.orderNumber} passa a Concluída.`]
  const inProduction = p.projectStatus === 'Planned' || p.projectStatus === 'InProgress'
  if (!p.lastFabricationOrder) {
    lines.push(p.otherOpenOrders === 1 ? `Falta 1 OS de fabricação para o projeto ${p.projectNumber} passar a Fabricado.` : `Faltam ${p.otherOpenOrders} OS de fabricação para o projeto ${p.projectNumber} passar a Fabricado.`)
    return lines
  }
  if (!inProduction) {
    lines.push(`O projeto ${p.projectNumber} já passou da produção: ele não muda.`)
    return lines
  }
  lines.push(`O projeto ${p.projectNumber} passa a Fabricado.`)
  for (const m of p.milestones.filter((x) => !x.released)) {
    lines.push(p.showAmounts && m.amount != null && m.dueDate ? `Libera a parcela ${m.description} de ${money(m.amount)}, vencendo em ${date(m.dueDate)}.` : `Libera a parcela do marco ${m.description}.`)
  }
  return lines
}

/** Linhas do impacto de "Estornar conclusão" (PRD-06a1 CA 10): a OS volta, o projeto volta quando foi fabricado pelas OS, a parcela fica. */
export function reversalImpact(p: { orderNumber: string; projectNumber: string; projectStatus: string; projectFabricatedByOrders: boolean; milestones: CompletionMilestone[] }): string[] {
  const lines = [`A ${p.orderNumber} volta a Em andamento, na última etapa.`]
  if (p.projectStatus === 'Fabricated' && p.projectFabricatedByOrders) lines.push(`O projeto ${p.projectNumber} volta de Fabricado a Em andamento.`)
  for (const m of p.milestones.filter((x) => x.released)) lines.push(`A parcela ${m.description} já liberada continua em aberto. O Financeiro é avisado.`)
  return lines
}

/** "Concluir OS" aparece para quem tem production.approve na OS pronta ou ainda em produção (desabilitado com o motivo). */
export const canShowComplete = (order: { status: string; steps: unknown[] }, canApprove: boolean) =>
  canApprove && order.steps.length > 0 && !['Completed', 'Cancelled'].includes(order.status)

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