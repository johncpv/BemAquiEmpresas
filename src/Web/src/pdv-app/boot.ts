import { browserSupport } from '@/pdv/platform'
import { DexieLocalStore, PDV_DB_NAME } from '@/pdv/local-store/dexie-local-store'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { acquireWriterLock, type WriterLock, writerLockName } from '@/pdv/local-store/writer-lock'

export type Boot =
  | { kind: 'unsupported' }
  | { kind: 'storage-error'; message: string }
  | {
    kind: 'open'
    store: LocalStore
    lock: WriterLock
    /** Resolve quando esta aba é a dona do lock e já releu o banco (na hora, na primeira aba). */
    owned: Promise<void>
  }

/**
 * Abertura do app do caixa, uma vez por página (fora do React, para o StrictMode não pedir o lock duas vezes): confere o navegador (CA 4),
 * pega o Web Lock de escritor (CA 6; sem ele a aba abre só de leitura e espera) e abre o `LocalStore` com o catálogo em memória.
 */
export async function bootPdv(): Promise<Boot> {
  if (!browserSupport().supported) return { kind: 'unsupported' }
  try {
    const lock = await acquireWriterLock(writerLockName(PDV_DB_NAME))
    const store = await DexieLocalStore.open({ lock })
    // A segunda aba, quando vira dona, relê o que a primeira gravou antes de mostrar o caixa.
    const owned = lock.owned ? Promise.resolve() : lock.whenOwned().then(() => store.reload())
    return { kind: 'open', store, lock, owned }
  } catch (error) {
    return { kind: 'storage-error', message: error instanceof Error ? `${error.name}: ${error.message}` : String(error) }
  }
}
