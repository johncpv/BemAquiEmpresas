import type { PauseReason, ShopfloorMyEntry, ShopfloorQueueStep, ShopfloorStepDetail } from '@/api/generated/model'

/** Motivos de pausa (CA 11), na ordem do PRD; o mais usado pelo operador vem primeiro (propositivo). */
export const PAUSE_REASONS: readonly { value: PauseReason; label: string }[] = [
  { value: 'Material', label: 'Falta de material' },
  { value: 'Machine', label: 'Máquina parada' },
  { value: 'Break', label: 'Intervalo' },
  { value: 'EndOfShift', label: 'Fim do turno' },
  { value: 'Other', label: 'Outro' },
]

export const MAX_PAUSE_NOTES = 120

export const pauseLabel = (reason: PauseReason | null | undefined) => PAUSE_REASONS.find((r) => r.value === reason)?.label ?? 'Pausa'

/** O motivo sugerido primeiro, os outros na ordem do PRD. */
export function orderedReasons(suggested: PauseReason | null | undefined) {
  const first = PAUSE_REASONS.filter((r) => r.value === suggested)
  return [...first, ...PAUSE_REASONS.filter((r) => r.value !== suggested)]
}

/** Dicas do /fab (Telas e estados). */
export const FAB_DICAS = {
  horasPadrao: 'Horas esperadas para esta etapa, copiadas do roteiro. Apontadas = soma do tempo de todos os operadores, sem as pausas.',
  pausa: 'A pausa para o seu cronômetro sem fechar o apontamento. Falta de material avisa o almoxarifado sozinha.',
  meuCentro: 'Mostra só as etapas do centro de trabalho do seu cargo. As outras continuam na fila: desligue para ver.',
} as const

/** "Só meu centro" lembrado no aparelho (CA 2): sem valor gravado, desligado. */
export const MY_CENTER_KEY = 'bq.fab.so-meu-centro'

export function loadMyCenter(storage: Pick<Storage, 'getItem'> | undefined = safeStorage()): boolean {
  try {
    return storage?.getItem(MY_CENTER_KEY) === '1'
  } catch {
    return false
  }
}

export function saveMyCenter(on: boolean, storage: Pick<Storage, 'setItem'> | undefined = safeStorage()) {
  try {
    storage?.setItem(MY_CENTER_KEY, on ? '1' : '0')
  } catch {
    // Sem armazenamento (aba privada): o filtro vale só nesta visita.
  }
}

function safeStorage(): Storage | undefined {
  try {
    return typeof window === 'undefined' ? undefined : window.localStorage
  } catch {
    return undefined
  }
}

/** Filtro "Só meu centro" (CA 2): a ordem do servidor (meu centro primeiro, prioridade, prometido) fica como está. */
export const filterQueue = (steps: readonly ShopfloorQueueStep[], onlyMine: boolean) => (onlyMine ? steps.filter((s) => s.isMyCenter) : [...steps])

/**
 * Segundos trabalhados do apontamento aberto (cronômetro, CA 7, 11, 12): agora − início − pausas fechadas − pausa aberta. O
 * relógio do servidor vale: <c>offsetMs</c> = servidor − aparelho na hora da leitura.
 */
export function workedSeconds(mine: Pick<ShopfloorMyEntry, 'startedAt' | 'closedPauseSeconds' | 'pauseStartedAt'>, nowMs: number, offsetMs = 0) {
  const now = nowMs + offsetMs
  const start = Date.parse(mine.startedAt)
  const openPause = mine.pauseStartedAt ? Math.max(now - Date.parse(mine.pauseStartedAt), 0) : 0
  return Math.max(Math.floor((now - start - openPause) / 1000) - mine.closedPauseSeconds, 0)
}

/** "02:14:36" (tabular-nums na tela). */
export function formatTimer(seconds: number) {
  const s = Math.max(Math.floor(seconds), 0)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(Math.floor(s / 3600))}:${pad(Math.floor((s % 3600) / 60))}:${pad(s % 60)}`
}

/** Horas com até 1 casa ("2,5"), como no protótipo ("Padrão da etapa: 18 h, apontadas 21 h"). */
export const floorHours = (value: number) => value.toLocaleString('pt-BR', { maximumFractionDigits: 1 })

/** Horas de um apontamento em andamento, pelos segundos do cronômetro. */
export const hoursFromSeconds = (seconds: number) => Math.round((seconds / 3600) * 10) / 10

/** "Marque os {n} itens que faltam" (CA 13), no singular para um. */
export function missingLabel(missing: number) {
  return missing === 1 ? 'Marque o item que falta' : `Marque os ${missing} itens que faltam`
}

/** Quantos itens do checklist faltam marcar. */
export const missingCount = (detail: Pick<ShopfloorStepDetail, 'checklist'>) => detail.checklist.filter((c) => !c.done).length

/** Antes → depois da folha "Finalizar etapa" (padrão 4, CA 13 a 15). */
export function finishImpact(detail: Pick<ShopfloorStepDetail, 'step' | 'requiresApproval' | 'nextStepName'>, others: readonly string[] = []) {
  const lines = [`${detail.step.stepName}: Em andamento → ${detail.requiresApproval ? 'Aguardando aprovação' : 'Concluída'}`]
  if (!detail.requiresApproval) lines.push(detail.nextStepName ? `Próxima: ${detail.nextStepName} fica pronta` : 'Última etapa: a OS fica pronta para concluir')
  else lines.push('Quem aprova na produção recebe o aviso para aprovar.')
  if (others.length > 0) lines.push(`Encerra também: ${others.join(', ')}`)
  return lines
}

/** "Sexta 16/10" (cabeçalho do app, CA 1), pela data de negócio que o servidor mandou. */
export function floorDate(isoDate: string) {
  const [y, m, d] = isoDate.split('-').map(Number)
  const date = new Date(Date.UTC(y, m - 1, d, 12))
  const weekday = date.toLocaleDateString('pt-BR', { weekday: 'long', timeZone: 'UTC' }).replace('-feira', '')
  return `${weekday} ${String(d).padStart(2, '0')}/${String(m).padStart(2, '0')}`
}

/** Tipo que abre na tela (CA 6): pdf, png e jpg; dwg e dxf só com "Baixar". */
export const viewableKind = (contentType: string) => (contentType === 'application/pdf' ? 'pdf' : contentType.startsWith('image/') ? 'image' : null)
export const NO_EMPLOYEE = 'Seu usuário não está ligado a um funcionário ativo. Fale com o supervisor.'

/** A etapa em andamento abre direto na primeira entrada (propositivo); depois de ver a etapa, "Fila" fica na fila. */
let currentSeen = false

export const stepSeen = () => currentSeen

export function markStepSeen() {
  currentSeen = true
}