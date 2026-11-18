import { useCallback, useEffect, useRef, useState } from 'react'
import { CatalogSchemaError, syncCatalog } from './catalog-sync'
import { type CatalogScreen, catalogScreen } from './catalog-screen'
import { usePdv } from './pdv-context'

const DELTA_EVERY_MS = 5 * 60_000

/**
 * Descida do catálogo na tela do caixa: sem catálogo guardado, baixa a foto com progresso (a tela espera); com catálogo, a tela abre na hora e o
 * delta roda por trás ao abrir, quando a rede volta e a cada 5 minutos (local-first). Só a aba dona do lock sincroniza.
 */
export function useCatalogSync(keys: CryptoKeyPair | null, online: boolean): { screen: CatalogScreen; retry: () => void } {
  const pdv = usePdv()
  const enabled = !!pdv?.createCatalogSource && !!keys
  const [syncing, setSyncing] = useState(false)
  const [progress, setProgress] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const running = useRef(false)

  const run = useCallback(async () => {
    if (!pdv?.createCatalogSource || !keys || !pdv.store.writable || running.current) return
    running.current = true
    setSyncing(true)
    setError(null)
    try {
      await syncCatalog(pdv.store, pdv.createCatalogSource(keys), { onProgress: (p) => setProgress(p.fraction) })
    } catch (e) {
      setError(e instanceof CatalogSchemaError || e instanceof Error ? e.message : 'Não foi possível baixar os produtos.')
    } finally {
      running.current = false
      setSyncing(false)
      setProgress(0)
      pdv.refreshStatus()
    }
  }, [pdv, keys])

  useEffect(() => {
    if (!enabled || !online) return
    void run()
    const timer = setInterval(() => void run(), DELTA_EVERY_MS)
    return () => clearInterval(timer)
  }, [enabled, online, run])

  const state = pdv?.store.catalogState() ?? null
  return {
    screen: catalogScreen({ enabled, itemCount: state ? state.counts.items : null, online, syncing, progress, error }),
    retry: () => void run(),
  }
}
