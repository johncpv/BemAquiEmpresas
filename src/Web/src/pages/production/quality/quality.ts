import type { ChecklistResult, NonConformityStatus, QualityChecklistItem, QualityDecisionResult, QualityReturnOption } from '@/api/generated/model'
import type { QueryClient } from '@tanstack/react-query'
import type { StatusTone } from '@/components/ui/status-chip'

/** Recarrega a fila e a etapa de qualidade quando o quadro muda (tempo real da produção). */
export const refreshQuality = (client: QueryClient) =>
  void client.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith('/api/v1/production/quality') })

/** Resultados do item do checklist da qualidade (PRD-05 CA 3 e 6), na ordem dos botões. */
export const QUALITY_RESULTS: readonly { value: ChecklistResult; label: string }[] = [
  { value: 'Done', label: 'Conforme' },
  { value: 'NonConforming', label: 'Não conforme' },
  { value: 'NotApplicable', label: 'Não se aplica' },
]

export const resultLabel = (value: ChecklistResult | null | undefined) => QUALITY_RESULTS.find((r) => r.value === value)?.label ?? 'Sem marca'

/** Situação da NC (CA 11): Aberta crítico, Em ação alerta, Encerrada ok. */
export const NC_STATUS: Record<NonConformityStatus, { label: string; tone: StatusTone }> = {
  Open: { label: 'Aberta', tone: 'crit' },
  InAction: { label: 'Em ação', tone: 'warn' },
  Closed: { label: 'Encerrada', tone: 'ok' },
}

/** Filtros da lista de NC na URL (CA 11): ?filtro=todas e ?situacao=. */
export const NC_SITUATIONS = [
  { value: 'pendente', label: 'Não encerradas' },
  { value: 'aberta', label: 'Abertas' },
  { value: 'acao', label: 'Em ação' },
  { value: 'encerrada', label: 'Encerradas' },
] as const

export const MAX_DESCRIPTION = 1000
export const MAX_CAUSE = 500
export const MAX_ACTION = 500
export const MAX_RESULT = 500

/** Dicas (Telas e estados da PRD-05). */
export const QUALITY_DICAS = {
  etapaRetorno: 'Etapa que será refeita. Ela fica pronta de novo e as seguintes, até a qualidade, voltam a esperar. As horas do retrabalho entram como novos apontamentos.',
  naoConformidade: 'Registro do defeito encontrado na inspeção: o que houve, a causa, a ação corretiva e quem acompanha até encerrar.',
  autoinspecao: 'Aprovada por quem fez parte do trabalho, porque não havia outro aprovador. O Proprietário foi avisado.',
  tentativa: 'Cada reprovação abre uma nova tentativa da mesma etapa de qualidade, com o checklist zerado. As anteriores ficam guardadas.',
  custo: 'Estimativa só para informação. Não entra no custo realizado do projeto, porque o retrabalho já entra pelas horas e pelo material.',
} as const

/** Itens sem marca (aprovar exige todos "Conforme" ou "Não se aplica", CA 3). */
export const missingQuality = (checklist: readonly Pick<QualityChecklistItem, 'result'>[]) => checklist.filter((c) => !c.result).length

/** Itens "Não conforme" (pedem a reprovação, CA 6). */
export const nonConforming = <T extends Pick<QualityChecklistItem, 'result'>>(checklist: readonly T[]) => checklist.filter((c) => c.result === 'NonConforming')

/** Pode aprovar: sem item faltando nem "Não conforme" e sem bloqueio de segregação. */
export const canApprove = (checklist: readonly Pick<QualityChecklistItem, 'result'>[], blocked: boolean) =>
  !blocked && missingQuality(checklist) === 0 && nonConforming(checklist).length === 0

/** Rascunho da descrição pelos itens "Não conforme" (propositivo). */
export function draftDescription(checklist: readonly Pick<QualityChecklistItem, 'result' | 'text'>[]) {
  const items = nonConforming(checklist).map((c) => c.text)
  return items.length === 0 ? '' : `Não conforme: ${items.join('; ')}.`
}

/** Etapa de retorno padrão: a sugerida (a produtiva imediatamente anterior), senão a primeira opção. */
export const defaultReturn = (options: readonly QualityReturnOption[]) => (options.find((o) => o.suggested) ?? options[0])?.stepId ?? null

/** "Marque os {n} itens que faltam", no singular para um. */
export const missingText = (n: number) => (n === 1 ? 'Marque o item que falta.' : `Marque os ${n} itens que faltam.`)

/**
 * Próximo passo depois da decisão (CA 4): última etapa aprovada → "Concluir OS" só para quem tem production.approve; quem só tem
 * production.approve_quality vê o aviso sem ação. Reprovada → abrir a NC.
 */
export function decisionNext(result: Pick<QualityDecisionResult, 'result' | 'readyToComplete' | 'canCompleteOrder' | 'nextStepName' | 'returnToStepName'>) {
  if (result.result === 'Rejected') return { text: `A OS voltou para ${result.returnToStepName ?? 'a etapa escolhida'}.`, complete: false, openNc: true }
  if (result.readyToComplete) {
    return result.canCompleteOrder
      ? { text: 'A OS está pronta para concluir.', complete: true, openNc: false }
      : { text: 'A OS está pronta para concluir. O supervisor conclui.', complete: false, openNc: false }
  }
  return { text: `A OS vai para ${result.nextStepName ?? 'a próxima etapa'}.`, complete: false, openNc: false }
}

/** "há 3 d" / "hoje" (idade da NC, CA 11). */
export const ageLabel = (days: number) => (days <= 0 ? 'Hoje' : days === 1 ? '1 dia' : `${days} dias`)