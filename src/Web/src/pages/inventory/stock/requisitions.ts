import type {
  MaterialRequestState, MaterialRequisitionKind, MaterialRequisitionStatus, RequisitionDraft, RequisitionDraftLine, RequisitionLineInput, RequisitionLotOption,
} from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatQuantity, lineTotal } from './stock'

/** Requisição de material no front (EST-03): rótulos, dicas e a prévia do diálogo; as regras de verdade estão no backend. */
export const REQUISITION_DICAS = {
  consumoAcima: 'Consumo acima da BOM é o que passa do reservado para a OS, ou um item fora da BOM (material extra). Sai do disponível do item, com o motivo, e acima de R$ 1.000,00 precisa da aprovação de outra pessoa.',
  sugerido: 'Lote sugerido: o de validade mais próxima e, sem validade ou empatado, o mais antigo pela entrada. A escolha do lote não muda o custo, que continua o médio.',
  devolver: 'Devolver ao estoque lança a devolução pelo custo da baixa original e tira o valor do realizado do projeto. Se a BOM ainda pede o item, o material volta a ficar reservado para a OS.',
  perda: 'Na OS cancelada, o material que não volta ao estoque fica no custo do projeto como perda, com o motivo.',
}

export const REQUISITION_STATUS: Record<MaterialRequisitionStatus, { label: string; tone: StatusTone }> = {
  Requested: { label: 'Pedido do operador', tone: 'warn' },
  PendingApproval: { label: 'Aguardando aprovação', tone: 'warn' },
  Completed: { label: 'Baixada', tone: 'ok' },
  Rejected: { label: 'Recusada', tone: 'crit' },
  Reversed: { label: 'Estornada', tone: 'neutral' },
}

export const REQUISITION_KIND: Record<MaterialRequisitionKind, string> = { Issue: 'Requisição', Return: 'Devolução', Loss: 'Perda' }

/** O que o operador vê no /fab (CA 7). */
export function requestStateLabel(state: MaterialRequestState, reason?: string | null): { label: string; tone: StatusTone } {
  if (state === 'Separated') return { label: 'Material separado', tone: 'ok' }
  if (state === 'Declined') return { label: reason ? `Recusado: ${reason}` : 'Recusado', tone: 'crit' }
  return { label: 'Pedido enviado ao almoxarifado', tone: 'info' }
}

/** Linha editável do diálogo "Requisitar material": uma por lote (a divisão em lotes vira várias linhas da mesma reserva). */
export interface RequisitionRow {
  key: string
  reservationId: string | null
  itemId: string
  itemCode: string
  itemName: string
  unitCode: string
  tracksLot: boolean
  locationId: string
  locationCode: string
  reserved: number
  onHand: number
  available: number
  averageCost: number
  lots: readonly RequisitionLotOption[]
  lotId: string | null
  /** O lote veio da sugestão e não foi trocado (tag "Sugerido"). */
  suggestedLot: boolean
  quantity: string | null
  overReason: string
  isExtra: boolean
}

let seq = 0
const key = () => `r${++seq}`

const round4 = (v: number) => Math.round(v * 10_000) / 10_000
export const num = (v: string | null | undefined) => (v ? Number(v) : 0)

/** Ordem da sugestão (ponto 56): validade mais próxima, sem validade por último; o servidor já manda os lotes nessa ordem. */
export function splitLots(lots: readonly RequisitionLotOption[], quantity: number): { lotId: string; quantity: number; suggested: boolean }[] {
  const picks: { lotId: string; quantity: number; suggested: boolean }[] = []
  let left = round4(quantity)
  for (const lot of lots) {
    if (left <= 0) break
    if (lot.onHand <= 0) continue
    const take = Math.min(lot.onHand, left)
    picks.push({ lotId: lot.lotId, quantity: take, suggested: picks.length === 0 })
    left = round4(left - take)
  }
  return picks
}

function rowOf(line: RequisitionDraftLine, quantity: number, lotId: string | null, suggested: boolean, isExtra = false): RequisitionRow {
  return {
    key: key(), reservationId: line.reservationId ?? null, itemId: line.itemId, itemCode: line.itemCode, itemName: line.itemName, unitCode: line.unitCode, tracksLot: line.tracksLot,
    locationId: line.locationId, locationCode: line.locationCode, reserved: line.reserved, onHand: line.onHand, available: line.available, averageCost: line.averageCost,
    lots: line.lots, lotId, suggestedLot: suggested, quantity: quantity > 0 ? String(quantity) : null, overReason: '', isExtra,
  }
}

/** Linhas iniciais (CA 1): todo o reservado a baixar, já divididas pelos lotes sugeridos. */
export function rowsFromDraft(draft: RequisitionDraft): RequisitionRow[] {
  return draft.lines.flatMap((line) => {
    if (!line.tracksLot) return [rowOf(line, line.reserved, null, false)]
    const picks = line.picks.length > 0 ? line.picks : []
    return picks.length > 0 ? picks.map((p) => rowOf(line, p.quantity, p.lotId, p.suggested)) : [rowOf(line, line.reserved, null, false)]
  })
}

/**
 * Linhas do pedido do operador (CA 7): a quantidade pedida por item, na reserva do item no diálogo, com o lote sugerido; item que não
 * tem reserva no diálogo fica de fora (o almoxarife acrescenta como material extra).
 */
export function rowsFromRequest(draft: RequisitionDraft, requested: readonly { itemId: string; quantity: number }[]): RequisitionRow[] {
  const byItem = new Map<string, number>()
  for (const r of requested) byItem.set(r.itemId, (byItem.get(r.itemId) ?? 0) + r.quantity)
  const rows: RequisitionRow[] = []
  for (const [itemId, quantity] of byItem) {
    const line = draft.lines.find((l) => l.itemId === itemId)
    if (!line) continue
    if (!line.tracksLot) {
      rows.push(rowOf(line, quantity, null, false))
      continue
    }
    const picks = splitLots(line.lots, quantity)
    rows.push(...(picks.length > 0 ? picks.map((p) => rowOf(line, p.quantity, p.lotId, p.suggested)) : [rowOf(line, quantity, null, false)]))
  }
  return rows
}

/** Outra linha da mesma reserva para dividir em vários lotes (CA 1). */
export function splitRow(row: RequisitionRow): RequisitionRow {
  const used = row.lotId
  const next = row.lots.find((l) => l.lotId !== used)
  return { ...row, key: key(), lotId: next?.lotId ?? null, suggestedLot: false, quantity: null, overReason: '' }
}

/** Linha de material extra (CA 3): sem reserva, com o lote sugerido do local. */
export function extraRow(item: { itemId: string; itemCode: string; itemName: string; unitCode: string; tracksLot: boolean; averageCost: number },
  location: { locationId: string; locationCode: string; onHand: number; available: number; lots: readonly RequisitionLotOption[] }): RequisitionRow {
  const lot = item.tracksLot ? location.lots.find((l) => l.suggested) ?? location.lots[0] : undefined
  return {
    key: key(), reservationId: null, itemId: item.itemId, itemCode: item.itemCode, itemName: item.itemName, unitCode: item.unitCode, tracksLot: item.tracksLot,
    averageCost: item.averageCost, locationId: location.locationId, locationCode: location.locationCode, reserved: 0, onHand: location.onHand, available: location.available,
    lots: location.lots, lotId: lot?.lotId ?? null, suggestedLot: !!lot, quantity: null, overReason: '', isExtra: true,
  }
}

/** Quanto de cada linha sai da reserva e quanto passa dela (CA 3), na ordem das linhas — a mesma conta do servidor. */
export function excessOf(rows: readonly RequisitionRow[]): number[] {
  const left = new Map<string, number>()
  for (const r of rows) if (r.reservationId && !left.has(r.reservationId)) left.set(r.reservationId, r.reserved)
  return rows.map((r) => {
    const quantity = num(r.quantity)
    if (!r.reservationId) return round4(quantity)
    const available = left.get(r.reservationId) ?? 0
    const take = Math.min(available, quantity)
    left.set(r.reservationId, available - take)
    return round4(quantity - take)
  })
}

/** Valor acima da BOM pelo custo médio do momento (CA 4). */
export const overValue = (rows: readonly RequisitionRow[], excess = excessOf(rows)) =>
  rows.reduce((sum, r, i) => sum + (excess[i] > 0 ? lineTotal(excess[i], r.averageCost) : 0), 0)

/** Total da requisição pelo custo médio do momento (impacto do CA 2). */
export const totalValue = (rows: readonly RequisitionRow[]) => rows.reduce((sum, r) => sum + lineTotal(num(r.quantity), r.averageCost), 0)

/** Acima do limite, "Enviar para aprovação" no lugar de "Baixar material" (CA 4). */
export const needsApproval = (value: number, limit: number) => value > limit

/**
 * Impacto por item e local (CA 2): quantidade, físico antes → depois e disponível antes → depois (só o excedente tira do disponível,
 * o reservado já era da OS).
 */
export function impactOf(rows: readonly RequisitionRow[]) {
  const excess = excessOf(rows)
  const groups = new Map<string, { itemCode: string; itemName: string; unitCode: string; locationCode: string; quantity: number; over: number; onHand: number; available: number; lots: string[] }>()
  rows.forEach((r, i) => {
    const k = `${r.itemId}|${r.locationId}`
    const g = groups.get(k) ?? { itemCode: r.itemCode, itemName: r.itemName, unitCode: r.unitCode, locationCode: r.locationCode, quantity: 0, over: 0, onHand: r.onHand, available: r.available, lots: [] }
    g.quantity = round4(g.quantity + num(r.quantity))
    g.over = round4(g.over + excess[i])
    const lot = r.lots.find((l) => l.lotId === r.lotId)?.lotNumber
    if (lot && !g.lots.includes(lot)) g.lots.push(lot)
    groups.set(k, g)
  })
  return [...groups.values()].map((g) => ({ ...g, onHandAfter: round4(g.onHand - g.quantity), availableAfter: round4(g.available - g.over), short: g.over > g.available }))
}

/** O que o servidor recebe (CA 2). */
export const toInput = (rows: readonly RequisitionRow[]): RequisitionLineInput[] => rows.filter((r) => num(r.quantity) > 0).map((r) => ({
  reservationId: r.reservationId, itemId: r.itemId, locationId: r.locationId, lotId: r.tracksLot ? r.lotId : null, quantity: num(r.quantity), overReason: r.overReason.trim() || null,
}))

/** Pendências do diálogo (CA 1 a 3): quantidade, lote, motivo do excedente e o disponível. */
export function rowProblems(rows: readonly RequisitionRow[]): string[] {
  const problems: string[] = []
  const filled = rows.filter((r) => num(r.quantity) > 0)
  if (filled.length === 0) problems.push('Ao menos uma linha com quantidade.')
  const excess = excessOf(rows)
  rows.forEach((r, i) => {
    if (num(r.quantity) <= 0) return
    if (r.tracksLot && !r.lotId) problems.push(`Lote escolhido para ${r.itemCode}.`)
    if (excess[i] > 0 && !r.overReason.trim()) problems.push(`Motivo do consumo acima da BOM para ${r.itemCode}.`)
  })
  for (const g of impactOf(rows)) if (g.short) problems.push(`Disponível suficiente de ${g.itemCode} em ${g.locationCode} (há ${formatQuantity(Math.max(0, g.available))} ${g.unitCode} livre).`)
  return problems
}

/** "Para separar hoje": número de OS em texto curto. */
export const pickingSummary = (count: number) => (count === 1 ? '1 OS com material para separar' : `${count} OS com material para separar`)
