import type { BomDetail, BomLineInput, BomLineView, ItemUnitOption } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatBrDecimal } from '@/lib/decimal'

/** Situação da revisão da BOM (ENG-02 CA 15). */
export const BOM_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Draft: { label: 'Rascunho', tone: 'info' },
  Released: { label: 'Liberada', tone: 'ok' },
  Superseded: { label: 'Substituída', tone: 'neutral' },
}

/** Situação da BOM na lista (CA 17). */
export const BOM_SITUATION: Record<string, { label: string; tone: StatusTone }> = {
  None: { label: 'Sem BOM', tone: 'warn' },
  Draft: { label: 'Rascunho', tone: 'info' },
  Released: { label: 'Liberada', tone: 'ok' },
}

/** Filtro da lista na URL (?situacao=vao-parar), com o valor da API. Sem filtro na URL, a lista abre em "vão parar" (CA 17). */
export const BOM_SITUATIONS = [
  { value: 'vao-parar', label: 'OS que vão parar sem BOM', api: 'will_stop' },
  { value: 'sem-bom', label: 'Sem BOM', api: 'without' },
  { value: 'rascunho', label: 'Com rascunho', api: 'draft' },
  { value: 'liberada', label: 'Liberadas', api: 'released' },
  { value: 'todas', label: 'Todas as OS em aberto', api: '' },
] as const

export const DEFAULT_BOM_SITUATION = 'vao-parar'

export const bomSituationParam = (value: string | undefined) => BOM_SITUATIONS.find((s) => s.value === (value || DEFAULT_BOM_SITUATION))?.api || undefined

/** Textos das dicas (Telas e estados da ENG-02). */
export const BOM_DICAS = {
  perda: 'Quanto a mais o material gasta no processo (corte, sobra, refugo). 5% pede 5% a mais do que a quantidade da peça.',
  necessidade: 'Quantidade por unidade × quantidade da OS × (1 + perda %), na unidade de estoque. É o que o almoxarifado reserva.',
  liberar: 'Liberar avisa o estoque para reservar o material e o comprador para comprar o que falta. Depois de liberada, a BOM só muda por revisão.',
  semItem: 'Esta OS não tem o produto no estoque. Para concluir a OS, cadastre o item e ligue à OS.',
  substituida: 'Revisão que deixou de valer quando uma revisão mais nova foi liberada. Fica guardada para consulta.',
  primeiraEtapa: 'A primeira etapa da OS começa sem BOM. As seguintes só começam com a BOM liberada.',
}

/** Linha do editor: valores normalizados em string (DecimalInput) e a unidade escolhida. */
export interface BomLineDraft {
  id: string | null
  itemId: string | null
  itemCode: string
  itemName: string
  itemActive: boolean
  stockUnitId: string
  stockUnitCode: string
  units: ItemUnitOption[]
  unitId: string | null
  quantity: string | null
  scrapPct: string | null
  notes: string
  available: number | null
  onHand: number | null
  reserved: number | null
}

export const emptyLine = (): BomLineDraft => ({
  id: null, itemId: null, itemCode: '', itemName: '', itemActive: true, stockUnitId: '', stockUnitCode: '', units: [], unitId: null, quantity: null, scrapPct: '0.00',
  notes: '', available: null, onHand: null, reserved: null,
})

export const toDraft = (l: BomLineView): BomLineDraft => ({
  id: l.id, itemId: l.itemId, itemCode: l.itemCode, itemName: l.itemName, itemActive: l.itemActive, stockUnitId: l.stockUnitId, stockUnitCode: l.stockUnitCode,
  units: l.units.length > 0 ? l.units : [{ unitId: l.stockUnitId, code: l.stockUnitCode, isStockUnit: true, toStock: 1 }],
  unitId: l.unitId, quantity: l.quantity.toFixed(4), scrapPct: l.scrapPct.toFixed(2), notes: l.notes ?? '', available: l.available ?? null, onHand: l.onHand ?? null,
  reserved: l.reserved ?? null,
})

export const draftLines = (bom: BomDetail | null | undefined) => (bom?.lines ?? []).map(toDraft)

export const toInput = (l: BomLineDraft): BomLineInput => ({
  id: l.id, itemId: l.itemId ?? '', quantity: Number(l.quantity ?? 0), unitId: l.unitId, scrapPct: Number(l.scrapPct ?? 0), notes: l.notes.trim() || null,
})

const round4 = (n: number) => Math.round((n + Number.EPSILON) * 10000) / 10000

/**
 * Necessidade da OS ao vivo (CA 3), na unidade de estoque: quantidade por unidade × quantidade da OS × (1 + perda %), convertida pelo
 * fator da unidade e arredondada em 4 casas no fim. A conta gravada é a do servidor; esta é a prévia da tela.
 */
export function requirement(line: Pick<BomLineDraft, 'quantity' | 'scrapPct' | 'unitId' | 'units'>, orderQuantity: number): number | null {
  if (!line.quantity) return null
  const factor = line.units.find((u) => u.unitId === line.unitId)?.toStock ?? 1
  return round4(Number(line.quantity) * orderQuantity * (1 + Number(line.scrapPct ?? 0) / 100) * factor)
}

/** Falta: o que a necessidade passa do disponível (null sem saldo do estoque). */
export function shortage(required: number | null, available: number | null): number | null {
  if (required === null || available === null) return null
  const missing = round4(required - Math.max(0, available))
  return missing > 0 ? missing : 0
}

/** Problemas antes de enviar (o servidor confere de novo): item, quantidade e item repetido (CA 2, 3). */
export function lineProblems(lines: BomLineDraft[]): Record<string, string> {
  const problems: Record<string, string> = {}
  const seen = new Map<string, number>()
  lines.forEach((l, i) => {
    if (!l.itemId) problems[`lines[${i}].itemId`] = 'Escolha o item.'
    else if (seen.has(l.itemId)) problems[`lines[${i}].itemId`] = `O item já está na linha ${seen.get(l.itemId)! + 1}.`
    else seen.set(l.itemId, i)
    if (!l.quantity || Number(l.quantity) <= 0) problems[`lines[${i}].quantity`] = 'Informe a quantidade por unidade do produto, maior que zero, com até 4 casas.'
    const scrap = Number(l.scrapPct ?? 0)
    if (scrap < 0 || scrap > 100) problems[`lines[${i}].scrapPct`] = 'Informe a perda entre 0 e 100%.'
  })
  return problems
}

/** Quantidade com até 4 casas e a unidade ("12,5 m"). */
export function qty(value: number | null | undefined, unit?: string): string {
  if (value === null || value === undefined) return '—'
  const text = formatBrDecimal(value.toFixed(4), 4).replace(/,?0+$/, '')
  return unit ? `${text} ${unit}` : text
}
