import type { ProjectCurrentStage, ProjectStageStatus, ProjectStageView, StageRole } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'

/** Rótulo do papel da etapa (EXE-02): Execução, Entrega e Aceite do cliente. */
export const ROLE_LABELS: Record<StageRole, string> = { Execution: 'Execução', Delivery: 'Entrega', Acceptance: 'Aceite do cliente' }

/** Situação da etapa (EXE-03a CA 7) com o tom do chip: concluída ok, em andamento informativo, aguardando aprovação em alerta. */
export const STATUS_LABELS: Record<ProjectStageStatus, { label: string; tone: StatusTone }> = {
  Waiting: { label: 'Aguardando', tone: 'neutral' },
  Ready: { label: 'Pronta para começar', tone: 'info' },
  InProgress: { label: 'Em andamento', tone: 'info' },
  AwaitingApproval: { label: 'Aguardando aprovação', tone: 'warn' },
  Done: { label: 'Concluída', tone: 'ok' },
}

/** "Etapa atual: T Treinamento" (CA 8); a posição vai no título, para o leitor de tela. */
export function currentStageText(stage: ProjectCurrentStage | null | undefined): string | null {
  return stage ? `Etapa atual: ${stage.code} ${stage.name}` : null
}

/** Previsto "02/12/2026 a 05/12/2026", só um lado quando falta o outro; sem nada, "—". */
export function plannedText(start: string | null | undefined, end: string | null | undefined): string {
  if (start && end) return `${formatDate(start)} a ${formatDate(end)}`
  if (start) return `a partir de ${formatDate(start)}`
  if (end) return `até ${formatDate(end)}`
  return '—'
}

/** Realizado: "Concluída em …", "Iniciada em …" ou "—". */
export function doneText(completedOn: string | null | undefined, startedAt: string | null | undefined): string {
  if (completedOn) return `Concluída em ${formatDate(completedOn)}`
  if (startedAt) return `Iniciada em ${formatDate(startedAt.slice(0, 10))}`
  return '—'
}

/** Horas previstas em pt-BR (até 2 casas na tela). */
export function hoursText(hours: number): string {
  return `${hours.toLocaleString('pt-BR', { maximumFractionDigits: 2 })} h`
}

export const STAGE_DICAS = {
  atual: 'Etapa do modelo em que o trabalho está: a primeira ainda não concluída. O indicador P · I · E · A · C continua mostrando a situação do projeto.',
  modelo: 'As etapas são uma cópia do modelo de etapas. Mudar o modelo depois não muda este projeto.',
}

/** Comando sobre a etapa (EXE-03b): o verbo do botão e o particípio do aviso, o mesmo verbo nos dois. */
export type StageAction = 'start' | 'complete' | 'approve'

export const STAGE_VERBS: Record<StageAction, { verb: string; done: string; title: string }> = {
  start: { verb: 'Iniciar etapa', done: 'Etapa iniciada', title: 'Iniciar a etapa?' },
  complete: { verb: 'Concluir etapa', done: 'Etapa concluída', title: 'Concluir a etapa?' },
  approve: { verb: 'Aprovar etapa', done: 'Etapa aprovada', title: 'Aprovar a etapa?' },
}

type StageLike = Pick<ProjectStageView, 'status' | 'role' | 'hasMilestone' | 'checklistDone' | 'checklistTotal'>

/** Etapa em foco no painel (EXE-03b): a primeira não concluída pela sequência; com todas concluídas, nenhuma. */
export function currentStageOf<T extends Pick<ProjectStageView, 'sequence' | 'status'>>(stages: readonly T[]): T | null {
  return [...stages].sort((a, b) => a.sequence - b.sequence).find((s) => s.status !== 'Done') ?? null
}

/** Permissão para concluir (CA 3, 4, 6 e 7): Entrega e marco financeiro, aprovar; Aceite, a permissão própria; o resto, editar. */
export function completionPermission(stage: Pick<ProjectStageView, 'role' | 'hasMilestone'>): string {
  if (stage.role === 'Delivery' || stage.hasMilestone) return 'projects.approve'
  if (stage.role === 'Acceptance') return 'projects.register_acceptance'
  return 'projects.edit'
}

/** Quem pode, quando o usuário não pode concluir. */
export function whoCompletes(stage: Pick<ProjectStageView, 'role' | 'hasMilestone'>): string {
  if (stage.role === 'Delivery' || stage.hasMilestone) return 'Esta etapa libera parcela: quem conclui são usuários com permissão de aprovar projetos.'
  if (stage.role === 'Acceptance') return 'Quem conclui: usuários com a permissão Registrar aceite do cliente.'
  return 'Quem conclui: usuários com permissão de editar projetos.'
}

/** Ação disponível na etapa pela situação; null quando não há ou falta a permissão. */
export function stageAction(stage: StageLike, can: (permission: string) => boolean): StageAction | null {
  switch (stage.status) {
    case 'Ready':
      return can('projects.edit') ? 'start' : null
    case 'InProgress':
      return can(completionPermission(stage)) ? 'complete' : null
    case 'AwaitingApproval':
      return can('projects.approve') ? 'approve' : null
    default:
      return null
  }
}

/** Próximo passo do cabeçalho pela etapa atual (EXE-03b CA 8): "Concluir Treinamento", "Iniciar Kickoff", "Aprovar Treinamento". */
export function stageStepText(stage: Pick<ProjectStageView, 'status' | 'name'> | null | undefined): string | null {
  if (!stage) return null
  if (stage.status === 'Ready') return `Iniciar ${stage.name}`
  if (stage.status === 'InProgress') return `Concluir ${stage.name}`
  if (stage.status === 'AwaitingApproval') return `Aprovar ${stage.name}`
  return null
}

/** A etapa pede a data com as regras da EXE-01a (situação vista e motivo da data anterior): Entrega, Aceite ou marco. */
export const stageGuarded = (stage: Pick<ProjectStageView, 'role' | 'hasMilestone'>) => stage.role !== 'Execution' || !!stage.hasMilestone

export const STAGE_EXTRA_DICAS = {
  exigeAprovacao: 'Quem conclui deixa a etapa aguardando aprovação; quem tem permissão de aprovar projetos aprova. Quem concluiu só aprova a própria etapa se a regra em Configurações > Parâmetros deixar.',
  autoaprovada: 'Quem concluiu a etapa também a aprovou, como a regra de autoaprovação permite. Fica registrado.',
}
