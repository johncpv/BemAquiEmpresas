import type { OutboxEntry } from '@/pdv/local-store/local-store'
import { toMilli } from '@/pdv/sale/quantity'

/**
 * Saldo da loja no aparelho (PDV-05, PP-02 de 18/11/2026). Com a opção "vender sem saldo" desligada (o padrão das Configurações da empresa), o
 * caixa não deixa lançar item sem saldo; sem rede, ele confere pelo **último saldo recebido**, descontando as vendas que ainda estão na fila de
 * saída. O saldo é só orientação: o servidor nunca recusa a venda concluída (a falta vira divergência no Estoque).
 *
 * Quantidades em milésimos inteiros (a mesma conta das linhas da venda), por produto (o id do item do catálogo do caixa).
 */
export const STOCK_META = 'pdv.stock'

export interface StockLevels {
  sellWithoutStock: boolean
  /** O próximo `since` pedido ao servidor (milissegundos Unix). */
  cursor: number
  receivedAt: string
  /** Disponível por produto, em milésimos; produto ausente = o servidor não mandou (sem conferência). */
  levels: Record<string, number>
}

/** O que o servidor manda (`GET /api/v1/pos/sync/stock`). */
export interface StockSnapshot {
  sellWithoutStock: boolean
  full: boolean
  cursor: number
  levels: readonly { productId: string; available: number }[]
}

/** A lista inteira troca a do aparelho; o delta escreve por cima. */
export function mergeStock(current: StockLevels | null, snapshot: StockSnapshot, receivedAt: string): StockLevels {
  const levels = snapshot.full || !current ? {} : { ...current.levels }
  for (const l of snapshot.levels) levels[l.productId] = Math.round(l.available * 1000)
  return { sellWithoutStock: snapshot.sellWithoutStock, cursor: snapshot.cursor, receivedAt, levels }
}

type JournalLike = { type: string; data?: { line?: { lineId: string; itemId: string; quantity: string }; lineId?: string; quantity?: string } }

/** Quanto as vendas concluídas que ainda esperam na fila de saída levaram de cada produto (o servidor ainda não baixou). */
export function pendingSold(outbox: readonly OutboxEntry[], saleKind = 'pos.sale'): Record<string, number> {
  const sold: Record<string, number> = {}
  for (const entry of outbox) {
    if (entry.kind !== saleKind) continue
    const payload = entry.payload as { status?: string; events?: JournalLike[] } | null
    if (!payload || payload.status === 'discarded' || !Array.isArray(payload.events)) continue
    const lines = new Map<string, { itemId: string; milli: number }>()
    for (const e of payload.events) {
      if (e.type === 'LineAdded' && e.data?.line) lines.set(e.data.line.lineId, { itemId: e.data.line.itemId, milli: toMilli(e.data.line.quantity) ?? 0 })
      else if (e.type === 'LineChanged' && e.data?.lineId && e.data.quantity !== undefined) {
        const line = lines.get(e.data.lineId)
        if (line) line.milli = toMilli(e.data.quantity) ?? line.milli
      } else if (e.type === 'LineRemoved' && e.data?.lineId) lines.delete(e.data.lineId)
    }
    for (const l of lines.values()) sold[l.itemId] = (sold[l.itemId] ?? 0) + l.milli
  }
  return sold
}

export type StockCheck = { ok: true } | { ok: false; availableMilli: number }

/**
 * Pode lançar `wantedMilli` do produto nesta venda (somando o que a venda já tem dele)? Opção ligada, sem saldo recebido ou produto que o
 * servidor não mandou: pode. Senão, o disponível é o último saldo menos o que as vendas da fila levaram.
 */
export function checkStock(stock: StockLevels | null, productId: string, wantedMilli: number, pending: Record<string, number>): StockCheck {
  if (!stock || stock.sellWithoutStock) return { ok: true }
  const level = stock.levels[productId]
  if (level === undefined) return { ok: true }
  const available = Math.max(0, level - (pending[productId] ?? 0))
  return wantedMilli <= available ? { ok: true } : { ok: false, availableMilli: available }
}

/** Mensagem da recusa para o operador. */
export function noStockMessage(name: string, availableMilli: number, unit: string): string {
  if (availableMilli <= 0) return `${name} está sem saldo nesta loja. A empresa não vende item sem saldo.`
  const qty = (availableMilli / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 3 })
  return `${name}: só ${qty} ${unit} em saldo nesta loja. A empresa não vende item sem saldo.`
}
