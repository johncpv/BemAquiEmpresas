import type { ConversionDetail, ManualPostingKind, ManualPostingStatus, StockItemContext, StockMovementType, StockSituation } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Rótulos e regras do motor de estoque no front (EST-01a). As regras de verdade estão no backend; aqui só a prévia. */
export const MOVEMENT_TYPE: Record<StockMovementType, string> = {
  Receipt: 'Entrada', Issue: 'Saída', Transfer: 'Transferência', Adjustment: 'Ajuste', Return: 'Devolução', Production: 'Produção', Shipment: 'Expedição', OpeningBalance: 'Saldo inicial',
}

export const KIND: Record<ManualPostingKind, string> = { OpeningBalance: 'Saldo inicial', Receipt: 'Entrada manual', Issue: 'Saída avulsa', Transfer: 'Transferência' }

export const POSTING_STATUS: Record<ManualPostingStatus, { label: string; tone: StatusTone }> = {
  Pending: { label: 'Aguardando aprovação', tone: 'warn' },
  Posted: { label: 'Lançado', tone: 'ok' },
  Rejected: { label: 'Reprovado', tone: 'crit' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

export const SITUATION: Record<StockSituation, { label: string; tone: StatusTone }> = {
  Normal: { label: 'Normal', tone: 'ok' },
  Low: { label: 'Abaixo do mínimo', tone: 'warn' },
  Out: { label: 'Sem saldo', tone: 'crit' },
}

export const SITUATION_OPTIONS = (Object.keys(SITUATION) as StockSituation[]).map((value) => ({ value, label: SITUATION[value].label }))

export const MOVEMENT_TYPE_OPTIONS = (Object.keys(MOVEMENT_TYPE) as StockMovementType[]).map((value) => ({ value, label: MOVEMENT_TYPE[value] }))

/** Textos das dicas (padrão NSHome): o termo que confunde ganha a explicação curta. */
export const DICAS = {
  disponivel: 'Disponível é o físico menos o que já está reservado para OS abertas.',
  custoMedio: 'Média ponderada do custo das entradas; as saídas usam o custo do momento.',
  saldoInicial: 'Quantidade e custo de cada item no fim da data de corte. Só vale enquanto a implantação do estoque está aberta e sem movimento no item e local.',
  aprovacao: 'Entrada ou saída acima do limite, ou com custo muito diferente do médio, precisa da aprovação de outra pessoa antes de mexer no saldo.',
  estornar: 'O estorno lança o movimento inverso. O original continua no histórico, marcado como estornado.',
}

const qty = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 4 })
const qty4 = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 4, maximumFractionDigits: 4 })
const cost6 = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2, maximumFractionDigits: 6 })

/** Quantidade em pt-BR, até 4 casas ("12,5"); com sinal quando pedido ("+3", "−2"). */
export function formatQuantity(value: number, signed = false): string {
  const text = qty.format(Math.abs(value))
  if (!signed) return value < 0 ? `−${text}` : text
  return value > 0 ? `+${text}` : value < 0 ? `−${text}` : text
}

export const formatQuantity4 = (value: number) => qty4.format(value)

/** Custo unitário com até 6 casas (o custo médio é numeric(18,6)). */
export const formatUnitCost = (value: number) => cost6.format(value)

const round = (value: number, scale: number) => {
  const f = 10 ** scale
  return Math.sign(value) * Math.round(Math.abs(value) * f + Number.EPSILON) / f
}

export const lineTotal = (quantity: number, unitCost: number) => round(quantity * unitCost, 2)

/**
 * Quantidade na unidade de estoque (R-EST-3): mesma unidade passa direto; outra precisa de conversão cadastrada no item
 * ("1 de = fator para", nas duas direções). Sem conversão, null (o servidor recusa com 422, nunca usa fator 1).
 */
export function toStockQuantity(quantity: number, unitId: string, stockUnitId: string, conversions: readonly ConversionDetail[]): number | null {
  if (unitId === stockUnitId) return round(quantity, 4)
  const direct = conversions.find((c) => c.fromUnitId === unitId && c.toUnitId === stockUnitId)
  if (direct) return round(quantity * direct.factor, 4)
  const inverse = conversions.find((c) => c.fromUnitId === stockUnitId && c.toUnitId === unitId)
  return inverse && inverse.factor > 0 ? round(quantity / inverse.factor, 4) : null
}

/** Entrada pondera pelo custo; com o físico total zerado, o médio é o custo da entrada (04-fluxos §3.1). */
export function averageAfterEntry(onHand: number, average: number, quantity: number, unitCost: number): number {
  return round(onHand <= 0 ? unitCost : (onHand * average + quantity * unitCost) / (onHand + quantity), 6)
}

/** Mesma regra da D7 do backend (ManualPostingRules.ApprovalReason), para avisar antes de enviar. */
export function approvalReason(kind: ManualPostingKind, value: number, unitCost: number | null, average: number, limit: number, deviationPct: number): string | null {
  if (kind !== 'Receipt' && kind !== 'Issue') return null
  const reasons: string[] = []
  if (value > limit) reasons.push(`Acima de ${new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(limit).replace(/ /g, ' ')}`)
  if (kind === 'Receipt' && unitCost !== null && average > 0) {
    const deviation = (unitCost - average) / average * 100
    if (Math.abs(deviation) > deviationPct) reasons.push(`Custo ${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 }).format(round(Math.abs(deviation), 1))}% ${deviation > 0 ? 'acima' : 'abaixo'} do médio`)
  }
  return reasons.length ? reasons.join(' · ') : null
}

export interface Impact {
  onHandBefore: number
  onHandAfter: number
  averageBefore: number
  averageAfter: number
  value: number
  insufficient: boolean
  /** Disponível (físico − reservado) e reservado para OS no local de origem (EST-02 CA 13). */
  available: number
  reserved: number
  approval: string | null
  /** Destino da transferência. */
  target?: { before: number; after: number }
}

/** Impacto antes → depois (padrão 4 da Fase 3): saldo do local, custo médio global e se vai para aprovação. */
export function previewImpact(kind: ManualPostingKind, context: StockItemContext, locationId: string, quantity: number, unitCost: number | null, targetId?: string): Impact {
  const at = (id?: string) => context.positions.find((p) => p.locationId === id)?.onHand ?? 0
  const before = at(locationId)
  const here = context.positions.find((p) => p.locationId === locationId)
  const reserved = here?.reserved ?? 0
  const available = here ? here.available : 0
  const average = context.averageCost
  const entry = kind === 'Receipt' || kind === 'OpeningBalance'
  const cost = entry ? (unitCost ?? average) : average
  const value = lineTotal(quantity, cost)
  return {
    onHandBefore: before,
    onHandAfter: entry ? before + quantity : before - quantity,
    averageBefore: average,
    averageAfter: entry && quantity > 0 ? averageAfterEntry(context.totalOnHand, average, quantity, cost) : average,
    value,
    // A saída e a transferência respeitam o disponível: o reservado para OS só sai pela requisição (EST-02 CA 13).
    insufficient: !entry && quantity > available,
    available,
    reserved,
    approval: approvalReason(kind, value, entry ? cost : null, average, context.manualApprovalAmount, context.manualCostDeviationPct),
    target: kind === 'Transfer' ? { before: at(targetId), after: at(targetId) + quantity } : undefined,
  }
}

/** Unidade de digitação lembrada por item (propositivo): só conveniência do navegador, sem efeito no saldo. */
export function rememberedUnit(itemId: string): string | null {
  try {
    return localStorage.getItem(`bq.stock.unit.${itemId}`)
  } catch {
    return null
  }
}

export function rememberUnit(itemId: string, unitId: string) {
  try {
    localStorage.setItem(`bq.stock.unit.${itemId}`, unitId)
  } catch {
    // Sem armazenamento (janela privada): só não lembra.
  }
}

/** Linha colada do "Saldo inicial em lote": código, quantidade, custo e lote, separados por tabulação ou ponto e vírgula. */
export interface PastedLine { code: string; quantity: string | null; unitCost: string | null; lotNumber: string }

const decimal = (text: string | undefined) => {
  const t = (text ?? '').trim().replace(/\s/g, '')
  if (!t) return null
  const normalized = t.includes(',') ? t.replace(/\./g, '').replace(',', '.') : t
  return Number.isFinite(Number(normalized)) ? normalized : null
}

export function parsePastedLines(text: string): PastedLine[] {
  return text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).map((l) => {
    const [code, q, c, lot] = l.split(/\t|;/)
    return { code: (code ?? '').trim(), quantity: decimal(q), unitCost: decimal(c), lotNumber: (lot ?? '').trim() }
  })
}
