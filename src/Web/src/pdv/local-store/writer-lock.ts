/**
 * Um só escritor por aparelho (OFF-01 CA 6, medido no SPK-03): a primeira aba do /pdv pega o Web Lock e escreve; a segunda fica só de
 * leitura, mostra "O caixa já está aberto em outra janela." e assume sozinha quando a primeira fecha (o navegador solta o lock com a aba).
 */
export const SECOND_TAB_MESSAGE = 'O caixa já está aberto em outra janela.'

/** O pedaço do `navigator.locks` que o caixa usa (os testes passam um falso). */
export interface LockManagerLike {
  request(name: string, options: { ifAvailable?: boolean; signal?: AbortSignal }, callback: (lock: unknown) => Promise<unknown> | unknown): Promise<unknown>
}

export interface WriterLock {
  /** Esta aba é a que escreve? */
  readonly owned: boolean
  /** Resolve quando esta aba passa a ser a dona (na hora, se já é). */
  whenOwned(): Promise<void>
  /** Solta o lock (fechar o caixa) ou desiste de esperar por ele. */
  release(): void
}

/** Nome do lock: um por banco local (um caixa por aparelho; o nome do banco já separa os tenants). */
export const writerLockName = (dbName: string) => `bemaqui-pdv-writer:${dbName}`

/**
 * Pede o lock sem esperar; se outra aba já tem, devolve `owned: false` e continua na fila (`whenOwned` resolve quando a outra fechar).
 * O lock fica preso enquanto a promessa interna não resolve, ou seja, até `release()` ou o fechamento da aba.
 */
export async function acquireWriterLock(name: string, locks: LockManagerLike | undefined = defaultLocks()): Promise<WriterLock> {
  if (!locks) throw new Error('Este navegador não tem Web Locks.')
  let releaseHeld: (() => void) | null = null
  let owned = false
  const held = () => new Promise<void>((resolve) => { releaseHeld = resolve })

  const got = await new Promise<boolean>((resolve) => {
    void locks.request(name, { ifAvailable: true }, (lock) => {
      if (!lock) { resolve(false); return undefined }
      owned = true
      resolve(true)
      return held()
    })
  })

  if (got) {
    return { get owned() { return owned }, whenOwned: () => Promise.resolve(), release: () => { owned = false; releaseHeld?.() } }
  }

  // Na fila: o navegador entrega o lock quando a outra aba soltar.
  const abort = new AbortController()
  let resolveOwned!: () => void
  const ownedPromise = new Promise<void>((resolve) => { resolveOwned = resolve })
  void locks.request(name, { signal: abort.signal }, () => {
    owned = true
    resolveOwned()
    return held()
  }).catch(() => { /* desistiu de esperar (AbortError) */ })

  return {
    get owned() { return owned },
    whenOwned: () => ownedPromise,
    release: () => {
      if (owned) { owned = false; releaseHeld?.() } else abort.abort()
    },
  }
}

function defaultLocks(): LockManagerLike | undefined {
  return typeof navigator !== 'undefined' && 'locks' in navigator ? (navigator.locks as unknown as LockManagerLike) : undefined
}

/**
 * Lock em memória com a mesma semântica do Web Locks exclusivo (fila por nome, `ifAvailable`, `signal`), para os testes simularem duas abas.
 */
export class InMemoryLockManager implements LockManagerLike {
  private readonly held = new Set<string>()
  private readonly queue = new Map<string, (() => void)[]>()

  async request(name: string, options: { ifAvailable?: boolean; signal?: AbortSignal }, callback: (lock: unknown) => Promise<unknown> | unknown): Promise<unknown> {
    if (this.held.has(name)) {
      if (options.ifAvailable) return callback(null)
      await new Promise<void>((resolve, reject) => {
        const list = this.queue.get(name) ?? []
        list.push(resolve)
        this.queue.set(name, list)
        options.signal?.addEventListener('abort', () => {
          this.queue.set(name, (this.queue.get(name) ?? []).filter((r) => r !== resolve))
          reject(new DOMException('Aborted', 'AbortError'))
        })
      })
    }

    this.held.add(name)
    try {
      return await callback({ name })
    } finally {
      // Passa direto para o próximo da fila (o lock não fica livre no meio, como no navegador).
      const next = this.queue.get(name)?.shift()
      if (next) next()
      else this.held.delete(name)
    }
  }
}
