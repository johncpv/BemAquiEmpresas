import type { CapacityLine, CapacityReference, ProductionCapacityView } from '@/api/generated/model'
import { MAX_REQUEST_LINES, type PurchasePrefill } from '@/pages/purchasing/purchase-prefill'
import { formatQuantity } from './stock'

/** Capacidade de produzir no front (EST-06): rótulos, dicas e o atalho para a solicitação; a conta de verdade está no backend. */
export const CAPACITY_DICAS = {
  limita: 'O item que dá menos unidades com o disponível de hoje. Comprar dele é o que mais aumenta o que dá para produzir.',
  pedidosEmAberto: 'Quanto daria para produzir se todos os pedidos de compra em aberto chegassem. Não entra no total.',
  porUnidade: 'Quanto a BOM pede de cada item para uma unidade do produto, já com a perda e na unidade de estoque.',
  disponivel: 'Disponível é o físico de todos os locais menos o que já está reservado para OS abertas.',
  sugerido: 'A BOM liberada mais nova deste produto. Você pode trocar por outra OS.',
}

/** "3 unidades" / "1 unidade" / "0 unidades". */
export const unitsText = (value: number) => `${formatQuantity(value)} ${value === 1 ? 'unidade' : 'unidades'}`

/** "Produto ELEV-6 — Elevador 6 paradas" ou, sem produto na OS, só a OS. */
export const referenceLabel = (r: CapacityReference) =>
  r.productCode ? `${r.productCode} — ${r.productName ?? ''}`.trim() : `OS ${r.orderNumber}`

/** Opção da BOM de referência: "OS-2026-00041 · rev. 1". */
export const referenceOption = (r: CapacityReference) => `${r.orderNumber} · rev. ${r.revision}`

/** Produtos distintos das BOMs liberadas (para o seletor "Produto"), na ordem da mais nova. */
export function productOptions(references: CapacityReference[]): { id: string; label: string }[] {
  const seen = new Set<string>()
  const options: { id: string; label: string }[] = []
  for (const r of references) {
    if (!r.productId || seen.has(r.productId)) continue
    seen.add(r.productId)
    options.push({ id: r.productId, label: referenceLabel(r) })
  }
  return options
}

/** Linhas com falta (as que vão para a solicitação). */
export const shortageLines = (view: ProductionCapacityView): CapacityLine[] => view.lines.filter((l) => (l.shortage ?? 0) > 0)

/** "Faltam 2 itens" / "Falta 1 item" / "Nada falta". */
export function shortageSummary(count: number): string {
  if (count === 0) return 'Nada falta para esta quantidade.'
  return count === 1 ? 'Falta 1 item para esta quantidade.' : `Faltam ${count} itens para esta quantidade.`
}

/**
 * Solicitação de compra preenchida com as faltas (CA 4): item e quantidade na unidade de estoque, até o limite de linhas da
 * solicitação; o destino fica "Estoque geral" e a observação diz de onde veio. Nada é gravado aqui.
 */
export function purchasePrefill(view: ProductionCapacityView): PurchasePrefill {
  const lines = shortageLines(view).filter((l) => l.unitId)
  const product = view.reference.productCode ?? `OS ${view.reference.orderNumber}`
  const source = `Faltas para produzir ${formatQuantity(view.quantity)} × ${product}, BOM da ${view.reference.orderNumber} (rev. ${view.reference.revision})`
  return {
    source,
    notes: `${source}. Gerada pela Capacidade de produzir.`.slice(0, 500),
    lines: lines.slice(0, MAX_REQUEST_LINES).map((l) => ({ itemId: l.itemId, code: l.itemCode, name: l.itemName, unitId: l.unitId!, unitCode: l.unitCode, quantity: l.shortage! })),
    leftOut: Math.max(0, lines.length - MAX_REQUEST_LINES),
  }
}

/** Quantidade desejada da URL: número maior que zero, senão 1 (padrão do CA 2). */
export function parseQuantity(value: unknown): number {
  const n = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : NaN
  return Number.isFinite(n) && n > 0 && n <= 99_999 ? Math.round(n * 10_000) / 10_000 : 1
}
