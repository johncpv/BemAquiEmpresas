import { DEFAULT_LETTERS, DEFAULT_STAGES } from '@/lib/stages'
import { formatBrDecimal, sumDecimal } from '@/lib/decimal'

/** Etapa em edição (CA 3 a 6): id nulo é etapa nova; a sequência é a posição na lista, regravada pelo servidor. */
export interface StepDraft {
  id: string | null
  name: string
  code: string
  /** O usuário mexeu na letra: o nome deixa de sugerir a inicial. */
  codeTouched: boolean
  workCenterId: string | null
  workCenterName: string | null
  /** Centro sugerido pelo último roteiro (tag "Sugerido", padrão de tela 6), até o usuário trocar. */
  suggested: boolean
  /** Horas normalizadas ("1.5000") ou null; 2 casas na tela. */
  standardHours: string | null
  isQualityStep: boolean
  requiresApproval: boolean
  checklist: string[]
}

export interface RoutingDraft {
  name: string
  productType: string
  steps: StepDraft[]
}

export interface CenterOption {
  id: string
  name: string
  line?: string | null
}

export interface CenterSuggestion {
  stepName: string
  workCenterId: string
}

export const MAX_STEPS = 30
export const MAX_CHECKLIST = 30
export const MAX_CHECKLIST_TEXT = 120

/** Textos das dicas da tela (Telas e estados da ENG-01). */
export const ROUTING_DICAS = {
  letra: 'Código da etapa no quadro da produção. A mesma letra em roteiros diferentes fica na mesma coluna.',
  horas: 'Horas de trabalho esperadas para uma unidade nesta etapa; o planejamento usa este número.',
  qualidade: 'Etapa em que a peça é inspecionada. Ao chegar nela, a OS passa para Qualidade.',
  aprovacao: 'A etapa só é concluída depois que alguém com permissão aprova.',
  semBom: 'A primeira etapa pode começar antes de a BOM ser liberada. As seguintes só começam com a BOM liberada (a partir da ENG-02).',
} as const

/** Letra sugerida pela inicial do nome, sem acento (CA 3): "Expedição" vira E. */
export function suggestLetter(name: string): string {
  const plain = name.normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase()
  return /[A-Z]/.exec(plain)?.[0] ?? ''
}

/** Só uma letra de A a Z, maiúscula (D10). */
export const normalizeLetter = (text: string) => suggestLetter(text.slice(-1)) || ''

/** Centro sugerido: o último usado numa etapa com o mesmo nome; sem histórico, o único centro ativo. */
export function suggestCenter(stepName: string, suggestions: readonly CenterSuggestion[], centers: readonly CenterOption[]): CenterOption | null {
  const key = stepName.trim().toLocaleLowerCase('pt-BR')
  const byName = suggestions.find((s) => s.stepName.trim().toLocaleLowerCase('pt-BR') === key)
  const found = byName ? centers.find((c) => c.id === byName.workCenterId) : undefined
  if (found) return found
  return centers.length === 1 ? centers[0] : null
}

export function newStep(name = '', suggestions: readonly CenterSuggestion[] = [], centers: readonly CenterOption[] = []): StepDraft {
  const center = name ? suggestCenter(name, suggestions, centers) : centers.length === 1 ? centers[0] : null
  return {
    id: null, name, code: suggestLetter(name), codeTouched: false, workCenterId: center?.id ?? null, workCenterName: center?.name ?? null, suggested: !!center,
    standardHours: '0.0000', isQualityStep: false, requiresApproval: false, checklist: [],
  }
}

/** Novo roteiro vem com as 5 etapas de exemplo e as letras (P-04), e o centro sugerido em cada uma (propositivo). */
export function exampleDraft(suggestions: readonly CenterSuggestion[], centers: readonly CenterOption[]): RoutingDraft {
  return {
    name: '', productType: '',
    steps: DEFAULT_STAGES.map((name, i) => ({
      ...newStep(name, suggestions, centers),
      code: DEFAULT_LETTERS[i],
      isQualityStep: name === 'Qualidade',
      checklist: name === 'Expedição' ? ['Preparar e embalar para o romaneio'] : [],
    })),
  }
}

/** Nome mudou: a letra acompanha a inicial enquanto o usuário não a editou. */
export function renameStep(step: StepDraft, name: string): StepDraft {
  return { ...step, name, code: step.codeTouched ? step.code : suggestLetter(name) }
}

/** Total de horas padrão ao vivo (rodapé), em 2 casas. */
export function totalHours(steps: readonly StepDraft[]): string {
  return formatBrDecimal(sumDecimal(steps.map((s) => s.standardHours), 4), 2) || '0,00'
}

/**
 * As mesmas regras do servidor antes de enviar (CA 5), com o caminho do ProblemDetails ("steps[2].code"): sem etapa,
 * letra fora de A a Z ou repetida, sem centro, primeira de qualidade e duas de qualidade seguidas.
 */
export function draftProblems(draft: RoutingDraft): Record<string, string> {
  const problems: Record<string, string> = {}
  if (!draft.name.trim()) problems.name = 'Informe o nome do roteiro, com até 100 caracteres.'
  if (draft.steps.length === 0) problems.steps = 'Inclua ao menos uma etapa.'
  if (draft.steps.length > MAX_STEPS) problems.steps = `Inclua no máximo ${MAX_STEPS} etapas.`
  const seen = new Map<string, number>()
  draft.steps.forEach((s, i) => {
    const field = `steps[${i}]`
    if (!s.name.trim()) problems[`${field}.name`] = 'Informe o nome da etapa, com até 60 caracteres.'
    if (!/^[A-Z]$/.test(s.code)) problems[`${field}.code`] = 'Informe a letra da etapa, de A a Z.'
    else if (seen.has(s.code)) problems[`${field}.code`] = `A letra ${s.code} já está na etapa ${(seen.get(s.code) ?? 0) + 1}.`
    else seen.set(s.code, i)
    if (!s.workCenterId) problems[`${field}.workCenterId`] = 'Escolha o centro de trabalho da etapa.'
    if (s.isQualityStep && i === 0) problems[`${field}.isQualityStep`] = 'A primeira etapa não pode ser de qualidade.'
    else if (s.isQualityStep && draft.steps[i - 1]?.isQualityStep) problems[`${field}.isQualityStep`] = 'Duas etapas de qualidade não podem ficar seguidas.'
  })
  return problems
}

/** Corpo do salvar: checklist sem linhas vazias, horas em número e a letra maiúscula. */
export function toStepsInput(steps: readonly StepDraft[]) {
  return steps.map((s) => ({
    id: s.id, name: s.name.trim(), code: s.code.trim().toUpperCase(), workCenterId: s.workCenterId, standardHours: Number(s.standardHours ?? 0),
    isQualityStep: s.isQualityStep, requiresApproval: s.requiresApproval, checklist: s.checklist.map((t) => t.trim()).filter(Boolean),
  }))
}

interface StepLike {
  id?: string | null
  name: string
  code: string
  workCenterId?: string | null
  workCenterName?: string | null
  standardHours: number
  isQualityStep: boolean
  requiresApproval: boolean
  checklist: readonly (string | { text: string })[]
}

/** Etapas do detalhe ou da duplicação para o editor; o nome do centro vem da lista quando a resposta não traz. */
export function toDraftSteps(steps: readonly StepLike[], centers: readonly CenterOption[]): StepDraft[] {
  return steps.map((s) => ({
    id: s.id ?? null, name: s.name, code: s.code, codeTouched: true, workCenterId: s.workCenterId ?? null,
    workCenterName: s.workCenterName ?? centers.find((c) => c.id === s.workCenterId)?.name ?? (s.workCenterId ? 'Centro inativo' : null), suggested: false,
    standardHours: Number(s.standardHours).toFixed(4), isQualityStep: s.isQualityStep, requiresApproval: s.requiresApproval,
    checklist: s.checklist.map((c) => (typeof c === 'string' ? c : c.text)),
  }))
}

/** Situação da lista na URL (?situacao=ativos), para o filtro da API. */
export const ROUTING_SITUATIONS = [{ value: 'ativos', label: 'Ativos' }, { value: 'inativos', label: 'Inativos' }]
export const situationParam = (value: string | undefined) => (value === 'ativos' ? 'active' : value === 'inativos' ? 'inactive' : undefined)

/** Rótulo de cada entidade no histórico do roteiro (CA 18). */
export const HISTORY_ENTITY: Record<string, string> = { RoutingTemplate: 'Roteiro', RoutingTemplateStep: 'Etapa', RoutingTemplateItem: 'Produto vinculado' }