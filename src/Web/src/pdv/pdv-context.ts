import { createContext, useContext } from 'react'
import type { CatalogSyncSource } from './catalog-sync'
import type { LocalStore } from './local-store/local-store'

/**
 * O que o app do caixa (/pdv) dá para as telas: o `LocalStore` aberto (só por ele se lê e grava dado local), a fonte da descida do catálogo
 * (null até o adaptador do cliente Orval existir) e a ajuda. Fora do app do caixa (testes de tela, por exemplo) o contexto é null.
 */
export interface PdvContextValue {
  store: LocalStore
  /** Monta a fonte assinada pela chave do aparelho; null enquanto o adaptador do cliente Orval não existe (ver catalog-source.ts). */
  createCatalogSource: ((keys: CryptoKeyPair, store: LocalStore) => CatalogSyncSource) | null
  openHelp: () => void
  /** Avisa a faixa de estado que algo mudou (venda na fila, sincronização) para ela reler na hora. */
  refreshStatus: () => void
}

export const PdvContext = createContext<PdvContextValue | null>(null)

export const usePdv = () => useContext(PdvContext)
