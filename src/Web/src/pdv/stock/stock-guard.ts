import type { LocalStore } from '@/pdv/local-store/local-store'
import { checkStock, noStockMessage, pendingSold, STOCK_META, type StockLevels } from './stock-levels'

/** Conferência de saldo do caixa (PDV-05): devolve a recusa, ou null quando pode lançar. */
export type StockGuard = (productId: string, wantedMilli: number) => Promise<{ ok: true } | { ok: false; availableMilli: number }>

/** A conferência sobre o `LocalStore`: o último saldo recebido menos o que as vendas da fila de saída levaram. */
export function localStockGuard(store: Pick<LocalStore, 'getMeta' | 'outbox'>): StockGuard {
  return async (productId, wantedMilli) => {
    const stock = (await store.getMeta<StockLevels>(STOCK_META)) ?? null
    if (!stock || stock.sellWithoutStock || stock.levels[productId] === undefined) return { ok: true }
    return checkStock(stock, productId, wantedMilli, pendingSold(await store.outbox()))
  }
}

/** A mensagem da recusa para o operador, ou null quando pode lançar (sem conferência, também pode). */
export async function checkStockMessage(guard: StockGuard | undefined, item: { id: string; name: string; unit: string }, wantedMilli: number): Promise<string | null> {
  if (!guard) return null
  const result = await guard(item.id, wantedMilli)
  return result.ok ? null : noStockMessage(item.name, result.availableMilli, item.unit)
}
