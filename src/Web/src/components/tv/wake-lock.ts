import { useEffect } from 'react'

interface WakeLockSentinelLike {
  release: () => Promise<void>
  addEventListener?: (type: 'release', listener: () => void) => void
}

/** Mantém a tela da TV acesa (Screen Wake Lock, PNL-01a CA 18), quando o navegador permite; pede de novo ao voltar a ficar visível. */
export function useWakeLock(enabled = true) {
  useEffect(() => {
    const wakeLock = (navigator as Navigator & { wakeLock?: { request: (type: 'screen') => Promise<WakeLockSentinelLike> } }).wakeLock
    if (!enabled || !wakeLock) return
    let sentinel: WakeLockSentinelLike | null = null
    let disposed = false
    const acquire = async () => {
      if (disposed || document.visibilityState !== 'visible' || sentinel) return
      try {
        sentinel = await wakeLock.request('screen')
        sentinel.addEventListener?.('release', () => { sentinel = null })
      } catch {
        sentinel = null // sem permissão (bateria, política): a TV segue sem a trava
      }
    }
    const visible = () => void acquire()
    void acquire()
    document.addEventListener('visibilitychange', visible)
    return () => {
      disposed = true
      document.removeEventListener('visibilitychange', visible)
      void sentinel?.release().catch(() => undefined)
    }
  }, [enabled])
}
