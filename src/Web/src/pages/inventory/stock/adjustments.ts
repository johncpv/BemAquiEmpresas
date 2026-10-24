import type {
  ConversionDetail, StockAdjustmentLineInput, StockAdjustmentMode, StockAdjustmentReason, StockAdjustmentStatus, StockItemContext, StockLotBalance,
} from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatQuantity4, lineTotal, toStockQuantity } from './stock'

/** Ajuste de estoque no front (EST-01b): rótulos, dicas e a prévia do diálogo; as regras de verdade estão no backend. */
export const ADJUSTMENT_STATUS: Record<StockAdjustmentStatus, { label: string; tone: StatusTone }> = {
  Pending: { label: 'Aguardando aprovação', tone: 'info' },
  Approved: { label: 'Aprovado', tone: 'ok' },
  Rejected: { label: 'Reprovado', tone: 'crit' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

export const ADJUSTMENT_MODE: Record<StockAdjustmentMode, string> = { Counted: 'Quantidade contada', Delta: 'Diferença' }

export const ADJUSTMENT_REASON: Record<StockAdjustmentReason, string> = {
  Loss: 'Perda/avaria', EntryError: 'Erro de lançamento', Surplus: 'Sobra encontrada', Count: 'Contagem', Other: 'Outro',
}

export const ADJUSTMENT_DICAS = {
  modo: 'Quantidade contada: digite o que existe na prateleira; a diferença é calculada sobre o saldo do sistema, e de novo na aprovação, sobre o saldo daquele momento. Diferença: digite quanto sobrou (+) ou faltou (−); a aprovação soma a diferença ao saldo do momento.',
  autoaprovado: 'Autoaprovado: o Proprietário era o único com permissão de aprovar no Estoque e decidiu o próprio ajuste. O sistema avisa o Proprietário.',
  aprovacao: 'Todo ajuste muda o saldo sem documento de origem, então passa pela aprovação de outra pessoa com permissão de aprovar no Estoque. O saldo e o custo médio só mudam quando for aprovado.',
}

/** Uma linha editável do diálogo "Lançar ajuste": o item com o saldo do local e a quantidade digitada. */
export interface AdjustmentRow {
  key: string
  itemId: string
  itemCode: string
  itemName: string
  unitCode: string
  stockUnitId: string
  tracksLot: boolean
  averageCost: number
  minStock?: number
  onHand: number
  reserved: number
  available: number
  /** Lotes com saldo registrado no local. */
  lots: readonly StockLotBalance[]
  lotId: string | null
  unitId: string
  unitOptions: readonly { id: string; code: string }[]
  conversions: readonly ConversionDetail[]
  quantity: string | null
  /** Saldo do item em todos os locais, para trocar o local sem buscar de novo. */
  context: StockItemContext
}

let seq = 0
const nextKey = () => `a${++seq}`
const round4 = (v: number) => Math.round(v * 10_000) / 10_000
const num = (v: string | null | undefined) => (v ? Number(v) : 0)

/** Linha nova a partir do saldo do item (GET /items/{id}/stock) no local escolhido. */
export function rowFromContext(context: StockItemContext, conversions: readonly ConversionDetail[], locationId: string): AdjustmentRow {
  const here = context.positions.find((p) => p.locationId === locationId)
  const lots = context.lots.filter((l) => l.locationId === locationId)
  const unitOptions = [{ id: context.unitId, code: context.unitCode }, ...conversions.map((c) => (c.fromUnitId === context.unitId ? { id: c.toUnitId, code: c.toUnitCode } : { id: c.fromUnitId, code: c.fromUnitCode }))]
  return {
    key: nextKey(), itemId: context.itemId, itemCode: context.code, itemName: context.name, unitCode: context.unitCode, stockUnitId: context.unitId, tracksLot: context.tracksLot,
    averageCost: context.averageCost, onHand: here?.onHand ?? 0, reserved: here?.reserved ?? 0, available: here?.available ?? 0, lots,
    lotId: context.tracksLot ? (lots[0]?.lotId ?? null) : null, unitId: context.unitId, unitOptions, conversions, quantity: null, context,
  }
}

/** O local mudou: o saldo de cada linha passa a ser o do novo local; a quantidade e a unidade ficam. */
export const relocate = (rows: readonly AdjustmentRow[], locationId: string): AdjustmentRow[] =>
  rows.map((r) => {
    const next = rowFromContext(r.context, r.conversions, locationId)
    return { ...next, key: r.key, quantity: r.quantity, unitId: r.unitId, lotId: next.lots.some((l) => l.lotId === r.lotId) ? r.lotId : next.lotId }
  })

/** Saldo do sistema da linha: o do lote, no item com lote; o do item no local, no item sem lote. */
export const systemQty = (row: AdjustmentRow) => (row.tracksLot ? (row.lots.find((l) => l.lotId === row.lotId)?.onHand ?? 0) : row.onHand)

/** Quantidade digitada na unidade de estoque, com o sinal da diferença; null sem conversão cadastrada. */
export function stockQuantity(row: AdjustmentRow): number | null {
  if (row.quantity === null || row.quantity === '') return null
  const value = num(row.quantity)
  const converted = toStockQuantity(Math.abs(value), row.unitId, row.stockUnitId, row.conversions)
  return converted === null ? null : Math.sign(value) * converted
}

/** Diferença da linha (CA 2): contado − sistema no modo "Quantidade contada"; a própria diferença no modo "Diferença". */
export function deltaOf(mode: StockAdjustmentMode, row: AdjustmentRow): number | null {
  const quantity = stockQuantity(row)
  if (quantity === null) return null
  return round4(mode === 'Counted' ? quantity - systemQty(row) : quantity)
}

export interface RowPreview {
  system: number
  delta: number | null
  value: number
  /** Físico do item no local depois de todas as linhas do item. */
  positionAfter: number
  /** "O saldo ficaria {x}, abaixo do reservado ({y}) para OS." ou o lote negativo (CA 4). */
  refusal: string | null
}

/** Prévia de todas as linhas: diferença, valor, físico depois e a mesma recusa do servidor (CA 4). */
export function previewRows(mode: StockAdjustmentMode, rows: readonly AdjustmentRow[]): RowPreview[] {
  const deltas = rows.map((r) => deltaOf(mode, r))
  const byItem = new Map<string, number>()
  rows.forEach((r, i) => byItem.set(r.itemId, round4((byItem.get(r.itemId) ?? 0) + (deltas[i] ?? 0))))
  return rows.map((r, i) => {
    const delta = deltas[i]
    const positionAfter = round4(r.onHand + (byItem.get(r.itemId) ?? 0))
    const lotAfter = round4(systemQty(r) + (delta ?? 0))
    let refusal: string | null = null
    if (delta !== null && delta < 0) {
      if (lotAfter < 0) refusal = `O saldo do lote ficaria ${formatQuantity4(lotAfter)} ${r.unitCode}, abaixo de zero.`
      else if (positionAfter < r.reserved) refusal = `O saldo ficaria ${formatQuantity4(positionAfter)} ${r.unitCode}, abaixo do reservado (${formatQuantity4(r.reserved)} ${r.unitCode}) para OS.`
    }
    return { system: systemQty(r), delta, value: delta === null ? 0 : lineTotal(Math.abs(delta), r.averageCost), positionAfter, refusal }
  })
}

/** Sobras e faltas em R$ (valor estimado ao vivo no rodapé). */
export function totalsOf(previews: readonly RowPreview[]): { surplus: number; shortage: number } {
  return previews.reduce((t, p) => (p.delta === null || p.delta === 0 ? t : p.delta > 0 ? { ...t, surplus: t.surplus + p.value } : { ...t, shortage: t.shortage + p.value }), { surplus: 0, shortage: 0 })
}

/** Pendências antes de enviar (padrão 4): local, motivo, ao menos uma linha, lote, quantidade, conversão, diferença e reservado. */
export function adjustmentProblems(mode: StockAdjustmentMode, locationId: string, reason: StockAdjustmentReason | '', detail: string, rows: readonly AdjustmentRow[]): string[] {
  const problems: string[] = []
  if (!locationId) problems.push('Local escolhido.')
  if (!reason) problems.push('Motivo escolhido.')
  if (reason === 'Other' && !detail.trim()) problems.push('Descrição do motivo "Outro".')
  if (rows.length === 0) problems.push('Ao menos um item no ajuste.')
  const previews = previewRows(mode, rows)
  rows.forEach((r, i) => {
    if (r.tracksLot && !r.lotId) problems.push(`Lote escolhido para ${r.itemCode}.`)
    if (r.quantity === null || r.quantity === '') problems.push(mode === 'Counted' ? `Quantidade contada de ${r.itemCode}.` : `Diferença de ${r.itemCode}.`)
    else if (stockQuantity(r) === null) problems.push(`Conversão cadastrada para a unidade de ${r.itemCode}.`)
    else if (mode === 'Counted' && num(r.quantity) < 0) problems.push(`Quantidade contada de ${r.itemCode} sem sinal negativo.`)
    else if (previews[i].delta === 0) problems.push(`Diferença em ${r.itemCode} (o contado é igual ao saldo do sistema; tire a linha).`)
    if (previews[i].refusal) problems.push(`${r.itemCode}: ${previews[i].refusal}`)
  })
  const seen = new Set<string>()
  for (const r of rows) {
    const k = `${r.itemId}|${r.lotId ?? ''}`
    if (seen.has(k)) problems.push(`${r.itemCode} aparece duas vezes${r.tracksLot ? ' com o mesmo lote' : ''}.`)
    seen.add(k)
  }
  return problems
}

/** O que o servidor recebe (CA 2): a quantidade digitada e a unidade; o modo vai no ajuste. */
export const toLineInputs = (rows: readonly AdjustmentRow[]): StockAdjustmentLineInput[] =>
  rows.map((r) => ({ itemId: r.itemId, lotId: r.tracksLot ? r.lotId : null, quantity: num(r.quantity), unitId: r.unitId === r.stockUnitId ? null : r.unitId }))

/** Trocar o modo com quantidades preenchidas pede confirmação e limpa as quantidades (CA 2). */
export const hasQuantities = (rows: readonly AdjustmentRow[]) => rows.some((r) => r.quantity !== null && r.quantity !== '')

export const clearQuantities = (rows: readonly AdjustmentRow[]): AdjustmentRow[] => rows.map((r) => ({ ...r, quantity: null }))

/** "1 item" / "3 itens". */
export const itemsLabel = (count: number) => (count === 1 ? '1 item' : `${count} itens`)
