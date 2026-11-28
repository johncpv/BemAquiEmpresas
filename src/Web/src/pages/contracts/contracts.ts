import type { Tone } from '../finance/receivables/receivables'

export const CONTRACT_STATUS: Record<string, { label: string; tone: Tone }> = {
  Active: { label: 'Ativo', tone: 'ok' },
  Completed: { label: 'Concluído', tone: 'info' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

/** Etapas do projeto (COM-04a CA 17): letra explícita, porque "Entregue" é T. O cancelado não entra (chip neutro). */
export const PROJECT_STAGES = ['Planejado', 'Em produção', 'Fabricado', 'Instalando', 'Entregue', 'Encerrado'] as const
export const PROJECT_LETTERS = ['P', 'E', 'F', 'I', 'T', 'C'] as const
const ORDER = ['Planned', 'InProgress', 'Fabricated', 'Installing', 'Delivered', 'Closed']

/** Índice da etapa; -1 no cancelado. */
export const projectStage = (status: string) => ORDER.indexOf(status)

/** Etapas do projeto de serviço (EXE-01b CA 5): P · I · E · A · C. Espelho da ProjectStageMap do back (teste nos dois lados). */
export const SERVICE_STAGES = ['Planejado', 'Em execução', 'Entregue', 'Aceito', 'Encerrado'] as const
export const SERVICE_LETTERS = ['P', 'I', 'E', 'A', 'C'] as const
/** Posição do "A" (aceite) no indicador do serviço. */
export const SERVICE_ACCEPTANCE_INDEX = 3

type StageKind = string | null | undefined

/** Rótulos e letras por tipo, num lugar só no front (EXE-01b "Regras"): espelho da ProjectStageMap. */
export function projectStages(kind: StageKind): { stages: readonly string[]; letters: readonly string[] } {
  return kind === 'Service' ? { stages: SERVICE_STAGES, letters: SERVICE_LETTERS } : { stages: PROJECT_STAGES, letters: PROJECT_LETTERS }
}

export interface StageView {
  stages: readonly string[]
  letters: readonly string[]
  /** -1 no cancelado. */
  index: number
  label: string
  /** Posição do "A" dispensado (sem aceite exigido nem registrado), só no serviço. */
  waived?: number
}

const SERVICE_INDEX: Record<string, number> = { Planned: 0, InProgress: 1, Fabricated: 1, Installing: 1, Delivered: 2, Closed: 4 }

/**
 * Etapa para o display de andar pelo tipo (EXE-01b CA 5 e 12). Usa a etapa do servidor quando vem (o "A" e o "dispensado" dependem do contrato);
 * sem ela, calcula pelo status como a ProjectStageMap (sem saber do contrato, o "A" não aparece como dispensado).
 */
export function stageView(p: { status?: string | null; executionKind?: StageKind; stage?: { letters: string[]; index: number; label: string; acceptanceWaived?: boolean } | null }): StageView {
  if (p.executionKind !== 'Service') {
    const index = projectStage(p.status ?? '')
    return { stages: PROJECT_STAGES, letters: PROJECT_LETTERS, index, label: index < 0 ? 'Cancelado' : PROJECT_STAGES[index] }
  }

  const server = p.stage
  const index = server ? server.index : (SERVICE_INDEX[p.status ?? ''] ?? -1)
  const waived = !!server?.acceptanceWaived && index !== SERVICE_ACCEPTANCE_INDEX
  return { stages: SERVICE_STAGES, letters: SERVICE_LETTERS, index, label: index < 0 ? 'Cancelado' : SERVICE_STAGES[index], waived: waived ? SERVICE_ACCEPTANCE_INDEX : undefined }
}

/** "Gerando parcelas…" até os títulos existirem; depois de ~30 s, o aviso de demora (CA 11, ADR 0019 §5). */
export function receivablesState(receivables: number, createdAt: string, now = Date.now()): 'ready' | 'pending' | 'slow' {
  if (receivables > 0) return 'ready'
  return now - Date.parse(createdAt) > 30_000 ? 'slow' : 'pending'
}

export const CONTRACT_DICAS = {
  snapshot: 'Cópia congelada do orçamento aprovado; muda só por aditivo.',
  assinatura: 'Data em que o cliente assinou. Padrão hoje; não pode ser no futuro.',
  receita: 'Conta do plano de contas onde entram as parcelas do contrato. Sugerida: 1.1 Venda de equipamentos.',
}
