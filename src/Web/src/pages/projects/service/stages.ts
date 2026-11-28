import type { ProjectCurrentStage, ProjectStageStatus, StageRole } from '@/api/generated/model'
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
