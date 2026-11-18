import type { LocalStore } from '@/pdv/local-store/local-store'

/**
 * Armazenamento persistente (OFF-01 CA 5): no primeiro uso o caixa pede `navigator.storage.persist()`. Sem ele o navegador pode apagar os dados
 * do site sob pressão de disco (no Chrome, não por tempo; SPK-03), e com eles as vendas que ainda não subiram. Negado: a tela avisa e o fato fica
 * no `LocalStore` (chave `storage.persist`) para subir com a sincronização (OFF-02 manda junto no próximo lote).
 */
export const PERSIST_DENIED_MESSAGE = 'O navegador pode apagar dados deste caixa. Instale o aplicativo.'

export type PersistStatus = 'granted' | 'denied' | 'unavailable'

export interface PersistRecord {
  status: PersistStatus
  /** Instante (ISO) do último pedido. */
  at: string
  /** Já subiu para o servidor? (a OFF-02 marca true quando enviar) */
  reported: boolean
}

type StorageLike = Pick<StorageManager, 'persist' | 'persisted'>

/** Pede (ou confere) a persistência. Já concedida não pede de novo; sem a API devolve 'unavailable'. */
export async function requestPersistence(storage: StorageLike | undefined = typeof navigator !== 'undefined' ? navigator.storage : undefined): Promise<PersistStatus> {
  if (!storage?.persist || !storage.persisted) return 'unavailable'
  try {
    if (await storage.persisted()) return 'granted'
    return (await storage.persist()) ? 'granted' : 'denied'
  } catch {
    return 'denied'
  }
}

/** Pede e grava o resultado quando ele muda (só a aba dona grava). Devolve o status para a tela decidir o aviso. */
export async function ensurePersistence(store: LocalStore, storage?: StorageLike, now: () => Date = () => new Date()): Promise<PersistStatus> {
  const status = await requestPersistence(storage)
  const previous = await store.getMeta<PersistRecord>('storage.persist')
  if (store.writable && previous?.status !== status) {
    await store.setMeta('storage.persist', { status, at: now().toISOString(), reported: false } satisfies PersistRecord)
  }

  return status
}
