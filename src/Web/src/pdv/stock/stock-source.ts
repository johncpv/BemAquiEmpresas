import { getGetPosStockUrl, getPosStock } from '@/api/generated/bemaqui'
import { signRequest } from '@/pdv/device-signing'
import type { StockSource } from './stock-sync'

/** Fonte real do saldo da loja (PDV-05): o cliente Orval de `GET /api/v1/pos/sync/stock`, assinado pelo aparelho (esquema `Device`). */
export function createStockSource(keys: CryptoKeyPair): StockSource {
  return async (since) => {
    const params = since === null ? undefined : { since }
    const url = getGetPosStockUrl(params)
    const body = await getPosStock(params, { headers: await signRequest(keys, 'GET', url) })
    return { sellWithoutStock: body.sellWithoutStock, full: body.full, cursor: body.cursor, levels: body.levels }
  }
}
