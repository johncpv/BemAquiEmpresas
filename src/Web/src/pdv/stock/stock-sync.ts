import type { LocalStore } from '@/pdv/local-store/local-store'
import { mergeStock, STOCK_META, type StockLevels, type StockSnapshot } from './stock-levels'

/** Porta da descida do saldo (`GET /api/v1/pos/sync/stock`): `since` nulo pede a lista inteira. */
export type StockSource = (since: number | null) => Promise<StockSnapshot>

/** Baixa o saldo da loja (inteiro na primeira vez, depois só o que mudou) e guarda no aparelho com a opção "vender sem saldo". */
export async function syncStock(store: Pick<LocalStore, 'getMeta' | 'setMeta'>, source: StockSource, now: () => Date = () => new Date()): Promise<StockLevels> {
  const current = (await store.getMeta<StockLevels>(STOCK_META)) ?? null
  const snapshot = await source(current ? current.cursor : null)
  const next = mergeStock(current, snapshot, now().toISOString())
  await store.setMeta(STOCK_META, next)
  return next
}
