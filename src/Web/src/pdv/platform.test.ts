import { describe, expect, it } from 'vitest'
import { decideUpdate, hasOpenCashSession } from './app-update'
import { catalogScreen } from './catalog-screen'
import { InMemoryLockManager } from './local-store/writer-lock'
import { browserFromUserAgent, browserSupport } from './platform'
import { ensurePersistence, requestPersistence } from './storage-persistence'
import type { LocalStore } from './local-store/local-store'

const UA = {
  chromeWindows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36',
  edgeWindows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36 Edg/156.0.0.0',
  chromeAndroid: 'Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Mobile Safari/537.36',
  firefoxWindows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0',
  operaWindows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36 OPR/120.0.0.0',
  samsungAndroid: 'Mozilla/5.0 (Linux; Android 14; SM-A146B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/27.0 Chrome/125.0.0.0 Mobile Safari/537.36',
  edgeAndroid: 'Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Mobile Safari/537.36 EdgA/156.0.0.0',
  webviewAndroid: 'Mozilla/5.0 (Linux; Android 13; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/125.0.0.0 Mobile Safari/537.36',
  chromeIphone: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/156.0 Mobile/15E148 Safari/604.1',
  chromeMac: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36',
  chromeLinux: 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36',
}

const env = (userAgent: string, over: Partial<Parameters<typeof browserSupport>[0]> = {}) =>
  ({ userAgent, hasIndexedDb: true, hasLocks: true, hasServiceWorker: true, hasSubtleCrypto: true, ...over })

/** OFF-01 CA 4 (PP-09): só Chrome ou Edge no Windows e Chrome no Android. */
describe('navegadores do caixa', () => {
  it.each([
    [UA.chromeWindows, 'Windows com Google Chrome'],
    [UA.edgeWindows, 'Windows com Microsoft Edge'],
    [UA.chromeAndroid, 'Android com Google Chrome'],
  ])('aceita %s', (ua, label) => expect(browserSupport(env(ua), true)).toEqual({ supported: true, label }))

  it.each([UA.firefoxWindows, UA.operaWindows, UA.samsungAndroid, UA.edgeAndroid, UA.webviewAndroid, UA.chromeIphone, UA.chromeMac, UA.chromeLinux])('recusa %s', (ua) => {
    expect(browserFromUserAgent(ua)).toBeNull()
    expect(browserSupport(env(ua), true)).toEqual({ supported: false, reason: 'browser' })
  })

  it('recusa quem se diz Chrome sem as APIs do caixa (Web Locks, IndexedDB, WebCrypto; service worker no build)', () => {
    expect(browserSupport(env(UA.chromeWindows, { hasLocks: false }), true)).toEqual({ supported: false, reason: 'features' })
    expect(browserSupport(env(UA.chromeWindows, { hasIndexedDb: false }), true).supported).toBe(false)
    expect(browserSupport(env(UA.chromeWindows, { hasServiceWorker: false }), true).supported).toBe(false)
    expect(browserSupport(env(UA.chromeWindows, { hasServiceWorker: false }), false).supported).toBe(true)
  })
})

/** OFF-01 CA 3: com sessão de caixa aberta a versão nova espera a troca de turno. */
describe('troca de versão', () => {
  it('sem versão nova não faz nada; sem sessão aplica; com sessão espera', () => {
    expect(decideUpdate(false, true)).toBe('none')
    expect(decideUpdate(true, false)).toBe('apply-now')
    expect(decideUpdate(true, true)).toBe('wait-for-shift-change')
  })

  it('ponto de extensão da CX-01: enquanto não há turno, nunca há sessão aberta', () => {
    expect(hasOpenCashSession()).toBe(false)
  })
})

/** Estados da tela inicial (carregando com progresso, vazio, primeira abertura sem rede). */
describe('tela inicial do caixa e o catálogo', () => {
  const base = { enabled: true, itemCount: null, online: true, syncing: false, progress: 0, error: null }
  it('sem fonte ligada a etapa não existe', () => expect(catalogScreen({ ...base, enabled: false, online: false })).toEqual({ kind: 'ready' }))
  it('primeira vez baixando: progresso em %', () => expect(catalogScreen({ ...base, syncing: true, progress: 0.42 })).toEqual({ kind: 'loading', percent: 42 }))
  it('primeira vez sem rede', () => expect(catalogScreen({ ...base, online: false })).toEqual({ kind: 'offline-first' }))
  it('erro com rede', () => expect(catalogScreen({ ...base, error: 'Servidor fora' })).toEqual({ kind: 'error', message: 'Servidor fora' }))
  it('catálogo vazio', () => expect(catalogScreen({ ...base, itemCount: 0 })).toEqual({ kind: 'empty' }))
  it('com catálogo guardado abre na hora, com ou sem rede, mesmo sincronizando', () => {
    expect(catalogScreen({ ...base, itemCount: 10, online: false })).toEqual({ kind: 'ready' })
    expect(catalogScreen({ ...base, itemCount: 10, syncing: true })).toEqual({ kind: 'ready' })
  })
})

/** OFF-01 CA 5: armazenamento persistente pedido e o resultado guardado para subir. */
describe('armazenamento persistente', () => {
  const memoryStore = (writable = true) => {
    const meta = new Map<string, unknown>()
    return {
      meta,
      store: { writable, getMeta: async (k: string) => meta.get(k), setMeta: async (k: string, v: unknown) => { meta.set(k, v) } } as unknown as LocalStore,
    }
  }

  it('concedido, negado e sem a API', async () => {
    expect(await requestPersistence({ persisted: async () => true, persist: async () => false })).toBe('granted')
    expect(await requestPersistence({ persisted: async () => false, persist: async () => true })).toBe('granted')
    expect(await requestPersistence({ persisted: async () => false, persist: async () => false })).toBe('denied')
    expect(await requestPersistence({} as StorageManager)).toBe('unavailable')
  })

  it('negado fica gravado para a sincronização (uma vez por mudança)', async () => {
    const { store, meta } = memoryStore()
    const storage = { persisted: async () => false, persist: async () => false }
    expect(await ensurePersistence(store, storage, () => new Date('2026-11-18T12:00:00Z'))).toBe('denied')
    expect(meta.get('storage.persist')).toEqual({ status: 'denied', at: '2026-11-18T12:00:00.000Z', reported: false })
    await ensurePersistence(store, storage, () => new Date('2026-11-18T13:00:00Z'))
    expect((meta.get('storage.persist') as { at: string }).at).toBe('2026-11-18T12:00:00.000Z')
  })

  it('a segunda aba não grava', async () => {
    const { store, meta } = memoryStore(false)
    await ensurePersistence(store, { persisted: async () => false, persist: async () => false })
    expect(meta.size).toBe(0)
  })
})

describe('lock em memória dos testes', () => {
  it('segura e passa para o próximo da fila', async () => {
    const locks = new InMemoryLockManager()
    const order: string[] = []
    let release!: () => void
    void locks.request('x', {}, () => new Promise<void>((r) => { order.push('a'); release = r }))
    expect(await locks.request('x', { ifAvailable: true }, (l) => l)).toBeNull()
    const second = locks.request('x', {}, () => { order.push('b') })
    release()
    await second
    expect(order).toEqual(['a', 'b'])
  })
})
