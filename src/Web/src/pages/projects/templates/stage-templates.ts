import { formatBrDecimal, sumDecimal } from '@/lib/decimal'
import { suggestLetter } from '@/pages/engineering/routings/routings'

export type StageRoleCode = 'Execution' | 'Delivery' | 'Acceptance'
export type MilestoneTriggerCode = 'Signature' | 'StageCompleted' | 'Delivered' | 'TechnicalAcceptance'

/** Etapa em edição (EXE-02 CA 2): id nulo é etapa nova; a sequência é a posição na lista, regravada pelo servidor. */
export interface StageDraft {
  id: string | null
  name: string
  code: string
  /** O usuário mexeu na letra: o nome deixa de sugerir a inicial. */
  codeTouched: boolean
  role: StageRoleCode
  /** Horas por unidade normalizadas ("1.5000") ou null; 2 casas na tela. */
  hours: string | null
  positionId: string | null
  positionName: string | null
  /** Cargo gravado que foi inativado (CA 4): selo "Cargo inativo". */
  positionInactive: boolean
  requiresApproval: boolean
  checklist: string[]
}

export interface MilestoneDraft {
  /** Percentual normalizado ("50.00") ou null. */
  percent: string | null
  trigger: MilestoneTriggerCode
  stageCode: string
  days: string
}

export interface TemplateDraft {
  name: string
  segment: string
  steps: StageDraft[]
  milestones: MilestoneDraft[]
}

export const MAX_STEPS = 30
export const MAX_CHECKLIST = 30
export const MAX_CHECKLIST_TEXT = 200
export const MAX_MILESTONES = 10

export const ROLE_LABEL: Record<StageRoleCode, string> = { Execution: 'Execução', Delivery: 'Entrega', Acceptance: 'Aceite do cliente' }
export const ROLES: StageRoleCode[] = ['Execution', 'Delivery', 'Acceptance']

export const TRIGGER_LABEL: Record<MilestoneTriggerCode, string> = {
  Signature: 'Na assinatura', StageCompleted: 'Ao concluir a etapa', Delivered: 'Na entrega', TechnicalAcceptance: 'No aceite do cliente',
}
export const TRIGGERS: MilestoneTriggerCode[] = ['Signature', 'StageCompleted', 'Delivered', 'TechnicalAcceptance']

/** Textos das dicas da tela (Telas e estados da EXE-02). */
export const TEMPLATE_DICAS = {
  papel: 'Execução é trabalho. Entrega é quando o serviço é entregue ao cliente (no máximo uma). Aceite do cliente vem por último, depois da Entrega.',
  horas: 'Horas previstas para uma unidade do item de serviço. No orçamento, viram a mão de obra prevista: horas × quantidade × custo-hora do cargo.',
  marcos: 'Sugestão de parcelas para o orçamento que usar este modelo. A soma dá 100%. O gatilho por etapa só aponta para etapas de Execução; Entrega e Aceite usam os gatilhos próprios.',
  letra: 'Código da etapa no indicador do projeto. Única no modelo.',
  aprovacao: 'A etapa só é concluída depois que alguém com permissão aprova.',
} as const

export function newStage(name = '', role: StageRoleCode = 'Execution'): StageDraft {
  return { id: null, name, code: suggestLetter(name), codeTouched: false, role, hours: '0.0000', positionId: null, positionName: null, positionInactive: false, requiresApproval: false, checklist: [] }
}

/** Novo modelo: três etapas para começar (Execução, Entrega e Aceite) e os marcos 50% na assinatura e 50% no aceite. */
export function emptyDraft(): TemplateDraft {
  return {
    name: '', segment: '',
    steps: [{ ...newStage('Execução'), code: 'X' }, { ...newStage('Entrega', 'Delivery'), code: 'E' }, { ...newStage('Aceite do cliente', 'Acceptance'), code: 'A' }],
    milestones: [newMilestone('Signature', '50.00'), newMilestone('TechnicalAcceptance', '50.00')],
  }
}

export function newMilestone(trigger: MilestoneTriggerCode = 'Signature', percent: string | null = null): MilestoneDraft {
  return { percent, trigger, stageCode: '', days: '0' }
}

/** Nome mudou: a letra acompanha a inicial enquanto o usuário não a editou. */
export function renameStage(step: StageDraft, name: string): StageDraft {
  return { ...step, name, code: step.codeTouched ? step.code : suggestLetter(name) }
}

/** Total de horas por unidade ao vivo (rodapé), em 2 casas. */
export function totalHours(steps: readonly StageDraft[]): string {
  return formatBrDecimal(sumDecimal(steps.map((s) => s.hours), 4), 2) || '0,00'
}

export function milestoneSum(milestones: readonly MilestoneDraft[]): number {
  return Math.round(milestones.reduce((total, m) => total + Number(m.percent ?? 0), 0) * 100) / 100
}

/** Letras das etapas de Execução: as únicas que o gatilho por etapa pode usar (CA 5). */
export const executionCodes = (steps: readonly StageDraft[]) => steps.filter((s) => s.role === 'Execution' && /^[A-Z]$/.test(s.code)).map((s) => s.code)

/**
 * As mesmas regras do servidor antes de enviar (CA 3 e 5), com o caminho do ProblemDetails ("steps[2].role"): ao menos uma Execução; uma
 * Entrega e um Aceite no máximo; Aceite depois da Entrega e por último; nada de Execução depois da Entrega; letras únicas; soma dos marcos 100%.
 */
export function draftProblems(draft: TemplateDraft): Record<string, string> {
  const problems: Record<string, string> = {}
  const add = (field: string, message: string) => { if (!problems[field]) problems[field] = message }
  if (!draft.name.trim()) add('name', 'Informe o nome do modelo, com até 100 caracteres.')
  if (draft.steps.length === 0) add('steps', 'Inclua ao menos uma etapa.')
  else if (!draft.steps.some((s) => s.role === 'Execution')) add('steps', 'Inclua ao menos uma etapa de Execução.')
  const seen = new Map<string, number>()
  let delivery: number | null = null
  let acceptance: number | null = null
  draft.steps.forEach((s, i) => {
    const field = `steps[${i}]`
    if (!s.name.trim()) add(`${field}.name`, 'Informe o nome da etapa, com até 60 caracteres.')
    if (!/^[A-Z]$/.test(s.code)) add(`${field}.code`, 'Informe a letra da etapa, de A a Z.')
    else if (seen.has(s.code)) add(`${field}.code`, `A letra ${s.code} já está na etapa ${(seen.get(s.code) ?? 0) + 1}.`)
    else seen.set(s.code, i)
    if (s.role === 'Delivery') {
      if (delivery !== null) add(`${field}.role`, 'O modelo tem no máximo uma etapa de Entrega.')
      else delivery = i
    } else if (s.role === 'Acceptance') {
      if (acceptance !== null) add(`${field}.role`, 'O modelo tem no máximo uma etapa de Aceite do cliente.')
      else {
        acceptance = i
        if (delivery === null) add(`${field}.role`, 'O Aceite do cliente vem depois de uma etapa de Entrega.')
        else if (i !== draft.steps.length - 1) add(`${field}.role`, 'O Aceite do cliente é a última etapa.')
      }
    } else if (delivery !== null) add(`${field}.role`, 'Depois da Entrega só pode vir o Aceite do cliente.')
  })
  const execution = new Set(executionCodes(draft.steps))
  const hasAcceptance = draft.steps.some((s) => s.role === 'Acceptance')
  draft.milestones.forEach((m, i) => {
    const field = `suggestedMilestones[${i}]`
    const percent = Number(m.percent ?? 0)
    if (!(percent > 0 && percent <= 100)) add(`${field}.percent`, 'Informe o percentual entre 0,01% e 100%.')
    if (m.trigger === 'StageCompleted' && !execution.has(m.stageCode)) add(`${field}.stageCode`, 'Escolha a letra de uma etapa de Execução deste modelo.')
    if (m.trigger === 'TechnicalAcceptance' && !hasAcceptance) add(`${field}.trigger`, 'Inclua a etapa de Aceite do cliente para usar este gatilho.')
    const days = Number(m.days)
    if (!Number.isInteger(days) || days < 0 || days > 365) add(`${field}.daysAfterEvent`, 'Informe o prazo entre 0 e 365 dias.')
  })
  if (draft.milestones.length > 0 && milestoneSum(draft.milestones) !== 100) add('suggestedMilestones', 'A soma dos marcos sugeridos tem de dar 100%.')
  return problems
}

/** Corpo do salvar: checklist sem linhas vazias, horas e % em número, letra maiúscula e a letra só no gatilho por etapa. */
export function toInput(draft: TemplateDraft) {
  return {
    name: draft.name.trim(),
    segment: draft.segment || null,
    steps: draft.steps.map((s) => ({
      id: s.id, name: s.name.trim(), code: s.code.trim().toUpperCase(), role: s.role, standardHoursPerUnit: Number(s.hours ?? 0), positionId: s.positionId,
      requiresApproval: s.requiresApproval, checklist: s.checklist.map((t) => t.trim()).filter(Boolean),
    })),
    suggestedMilestones: draft.milestones.map((m) => ({
      percent: Number(m.percent ?? 0), trigger: m.trigger, stageCode: m.trigger === 'StageCompleted' ? m.stageCode : null, daysAfterEvent: Number(m.days) || 0,
    })),
  }
}

interface StepLike {
  id: string
  name: string
  code: string
  role: string
  standardHoursPerUnit: number
  positionId?: string | null
  positionName?: string | null
  positionActive: boolean
  requiresApproval: boolean
  checklist: readonly string[]
}

interface MilestoneLike {
  percent: number
  trigger: string
  stageCode?: string | null
  daysAfterEvent: number
}

/** Detalhe do servidor para o editor; cargo gravado e inativo ganha o selo (CA 4). */
export function toDraft(t: { name: string; segment?: string | null; steps: readonly StepLike[]; suggestedMilestones: readonly MilestoneLike[] }): TemplateDraft {
  return {
    name: t.name, segment: t.segment ?? '',
    steps: t.steps.map((s) => ({
      id: s.id, name: s.name, code: s.code, codeTouched: true, role: s.role as StageRoleCode, hours: Number(s.standardHoursPerUnit).toFixed(4),
      positionId: s.positionId ?? null, positionName: s.positionName ?? (s.positionId ? 'Cargo' : null), positionInactive: !!s.positionId && !s.positionActive,
      requiresApproval: s.requiresApproval, checklist: [...s.checklist],
    })),
    milestones: t.suggestedMilestones.map((m) => ({ percent: Number(m.percent).toFixed(2), trigger: m.trigger as MilestoneTriggerCode, stageCode: m.stageCode ?? '', days: String(m.daysAfterEvent) })),
  }
}

/** Resumo de um marco para a lista e a prévia: "50% ao concluir a etapa K". */
export function milestoneText(m: MilestoneLike): string {
  const pct = `${Number(m.percent).toLocaleString('pt-BR', { maximumFractionDigits: 2 })}%`
  const when = m.trigger === 'StageCompleted' ? `ao concluir a etapa ${m.stageCode ?? ''}` : TRIGGER_LABEL[m.trigger as MilestoneTriggerCode]?.toLowerCase() ?? m.trigger
  return `${pct} ${when}${m.daysAfterEvent ? `, +${m.daysAfterEvent} dias` : ''}`
}

/** Situação da lista na URL (?situacao=ativos), para o filtro da API. */
export const TEMPLATE_SITUATIONS = [{ value: 'ativos', label: 'Ativos' }, { value: 'inativos', label: 'Inativos' }]
export const situationParam = (value: string | undefined) => (value === 'ativos' ? 'active' : value === 'inativos' ? 'inactive' : undefined)

/** "Usado em {n} projetos" (CA 1 e 6). */
export const usageText = (n: number) => (n === 1 ? 'Usado em 1 projeto' : `Usado em ${n} projetos`)

/** Rótulo de cada entidade no histórico do modelo (CA 14). */
export const HISTORY_ENTITY: Record<string, string> = { StageTemplate: 'Modelo', StageTemplateStep: 'Etapa', StageTemplateItem: 'Item vinculado' }
