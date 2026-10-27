import type { DeliveryRisk, KioskBoard, KioskCard, KioskDelays } from '@/api/generated/model'

/** Textos das visões do Painel da TV (PNL-01b). */
export const PANEL_TEXT = {
  loading: 'Conectando ao painel…',
  empty: 'Nenhuma OS em produção nesta linha.',
  noForecast: 'Previsão em cálculo',
  queueEmpty: 'Nenhum projeto na fila com OS nesta linha.',
  late: 'ATRASADA',
  atRisk: 'EM RISCO',
  allLines: 'Todas as linhas',
} as const

/** As três visões, na ordem do painel (ponto 50). */
export const VIEW_ORDER = ['board', 'queue', 'late'] as const
export const VIEW_NAMES: Record<string, string> = { board: 'Quadro por etapa', queue: 'Fila e previsões', late: 'Atrasos e riscos' }

/** Até 3 cartões por coluna (ADR 0030 §5) e até 6 linhas nas visões de fila e de atrasos. */
export const CARDS_PER_COLUMN = 3
/** Largura mínima de uma coluna do quadro: 5 colunas a 1920 px, 3 a 1366 px (CA 11). */
export const COLUMN_MIN_PX = 340
export const COLUMN_GAP_PX = 24

/**
 * Visões que entram na rotação (CA 6 e 7): as da tela, na ordem do painel; "Atrasos e riscos" sai quando está vazia (só depois de
 * carregar: enquanto carrega, fica).
 */
export function rotationViews(views: readonly string[], delays: Pick<KioskDelays, 'total'> | undefined): string[] {
  return VIEW_ORDER.filter((v) => views.includes(v) && !(v === 'late' && delays !== undefined && delays.total === 0))
}

/** Próxima visão da rotação; com uma visão só, fica nela. */
export const nextIndex = (index: number, count: number, step = 1) => (count <= 1 ? 0 : (((index + step) % count) + count) % count)

/** Tempo na etapa em h:mm, desde o início real (o servidor manda o início; a TV só anima). */
export function elapsedText(startedAt: string, now: Date): string {
  const minutes = Math.max(0, Math.floor((now.getTime() - new Date(startedAt).getTime()) / 60_000))
  return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`
}

const hours = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 })

/** "{apontadas} de {padrão} h" (CA 2). */
export const hoursText = (applied: number, standard: number) => `${hours.format(applied)} de ${hours.format(standard)} h`

/** Medidor de horas: fração do padrão (até 1) e se passou do padrão (crítico). */
export function meter(applied: number, standard: number): { ratio: number; over: boolean } {
  if (standard <= 0) return { ratio: applied > 0 ? 1 : 0, over: applied > 0 }
  return { ratio: Math.min(applied / standard, 1), over: applied > standard }
}

/** Data de negócio curta (dd/mm), sem converter fuso. */
export function shortDate(value: string | null | undefined): string {
  if (!value) return ''
  const [, m, d] = value.split('-')
  return `${d}/${m}`
}

const rank = (risk: DeliveryRisk) => (risk === 'Late' ? 0 : risk === 'AtRisk' ? 1 : 2)

/** Ordem dos cartões na coluna (CA 3): atrasadas (maior atraso primeiro), em risco, depois pelo fim previsto e pelo número. */
export function sortCards(cards: readonly KioskCard[]): KioskCard[] {
  return [...cards].sort((a, b) =>
    rank(a.risk) - rank(b.risk)
    || (b.delayDays ?? 0) - (a.delayDays ?? 0)
    || (a.plannedEnd ?? '9999-12-31').localeCompare(b.plannedEnd ?? '9999-12-31')
    || a.number.localeCompare(b.number))
}

/** Até 3 cartões e quantos não couberam ("+{n} OS"). */
export function columnCards(cards: readonly KioskCard[], columnKey: string, max = CARDS_PER_COLUMN): { shown: KioskCard[]; more: number } {
  const mine = sortCards(cards.filter((c) => c.columnKey === columnKey))
  return { shown: mine.slice(0, max), more: Math.max(mine.length - max, 0) }
}

/** Linha 2 do cartão: o operador, "Pausada: {motivo}" ou "Sem operador". */
export function operatorLine(card: Pick<KioskCard, 'operator' | 'pausedReason'>): string {
  if (card.pausedReason) return `Pausada: ${card.pausedReason}`
  return card.operator ?? 'Sem operador'
}

/**
 * Recarga agrupada por cartão (CA 8 e 9): troca (ou tira, se a OS saiu da linha ou fechou) o cartão no quadro e recalcula as contagens,
 * sem piscar a tela inteira. Devolve null quando o cartão foi para uma coluna que o quadro não tem: aí a TV recarrega o quadro.
 */
export function patchBoard(board: KioskBoard, id: string, card: KioskCard | null): KioskBoard | null {
  if (card && !board.columns.some((c) => c.key === card.columnKey)) return null
  const cards = card ? (board.cards.some((c) => c.id === id) ? board.cards.map((c) => (c.id === id ? card : c)) : [...board.cards, card]) : board.cards.filter((c) => c.id !== id)
  const columns = board.columns.map((c) => ({ ...c, count: cards.filter((x) => x.columnKey === c.key).length })).filter((c) => c.count > 0)
  return {
    ...board,
    cards: sortCards(cards),
    columns,
    inProgressCount: cards.length,
    lateCount: cards.filter((c) => c.risk === 'Late').length,
    atRiskCount: cards.filter((c) => c.risk === 'AtRisk').length,
  }
}

/** Quantas colunas cabem na largura (CA 11): 5 a 1920 px, 3 a 1366 px; no mínimo 1. */
export const columnsPerPage = (width: number) => Math.max(1, Math.floor((width + COLUMN_GAP_PX) / (COLUMN_MIN_PX + COLUMN_GAP_PX)))

/** Página de colunas da vez: com mais colunas do que cabem, a TV rola as colunas a cada rotação (CA 11). */
export function columnPage<T>(columns: readonly T[], perPage: number, tick: number): { items: T[]; page: number; pages: number } {
  const pages = Math.max(1, Math.ceil(columns.length / perPage))
  const page = ((tick % pages) + pages) % pages
  return { items: columns.slice(page * perPage, page * perPage + perPage), page, pages }
}

/** Cabeçalho do quadro: "{linha} — {n} OS em andamento, {m} atrasadas". */
export function headline(line: string | null | undefined, board: Pick<KioskBoard, 'inProgressCount' | 'lateCount'> | undefined): string {
  const name = line ?? PANEL_TEXT.allLines
  if (!board) return name
  const late = board.lateCount === 1 ? '1 atrasada' : `${board.lateCount} atrasadas`
  return `${name} — ${board.inProgressCount} OS em andamento, ${late}`
}
