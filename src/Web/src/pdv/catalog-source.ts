import type { CatalogSyncSource } from './catalog-sync'

/**
 * Fonte real da descida do catálogo: o adaptador do cliente Orval de `GET /api/v1/pos/sync/snapshot` e `GET /api/v1/pos/sync/changes?cursor=`,
 * com a assinatura do aparelho (`signRequest`) e a conversão do decimal da API para centavos (`toCents`). **Ainda não ligado**: o backend da
 * OFF-01 está sendo feito em paralelo e o cliente gerado ainda não existe. Enquanto isso o app abre sem a etapa do catálogo (a tela de entrada
 * do PDV-01 continua igual). Para ligar: implementar `CatalogSyncSource` aqui e devolver a fábrica no lugar do null.
 */
export const createCatalogSource: ((keys: CryptoKeyPair) => CatalogSyncSource) | null = null
