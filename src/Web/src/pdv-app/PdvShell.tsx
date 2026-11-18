import { type ReactNode, useCallback, useEffect, useMemo, useState } from 'react'
import { onUpdatePending, UPDATE_PENDING_MESSAGE } from '@/pdv/app-update'
import { createCatalogSource } from '@/pdv/catalog-source'
import type { LocalStore, PendingSummary } from '@/pdv/local-store/local-store'
import { PdvContext, type PdvContextValue } from '@/pdv/pdv-context'
import { statusView } from '@/pdv/status-bar'
import { ensurePersistence, PERSIST_DENIED_MESSAGE } from '@/pdv/storage-persistence'
import { PdvHelp } from './PdvHelp'
import { PdvStatusBar } from './PdvStatusBar'
import { useOnline } from '@/pdv/use-online'

const POLL_MS = 15_000

/**
 * Casca do app do caixa: a faixa de estado fixa no topo (CA 13), o aviso de atualização que espera a troca de turno (CA 3), o aviso de
 * armazenamento não persistente (CA 5) e a ajuda. Dá o `LocalStore` para as telas pelo `PdvContext`.
 */
export function PdvShell({ store, children }: { store: LocalStore; children: ReactNode }) {
  const online = useOnline()
  const [pending, setPending] = useState<PendingSummary>({ count: 0, oldestAt: null })
  const [tick, setTick] = useState(0)
  const [now, setNow] = useState(() => new Date())
  const [helpOpen, setHelpOpen] = useState(false)
  const [updatePending, setUpdatePending] = useState(false)
  const [persistDenied, setPersistDenied] = useState(false)

  const refreshStatus = useCallback(() => { setNow(new Date()); setTick((t) => t + 1) }, [])

  useEffect(() => {
    let alive = true
    void store.pendingSummary().then((p) => { if (alive) setPending(p) }).catch(() => { /* a faixa fica com o último valor */ })
    return () => { alive = false }
  }, [store, tick, online])

  useEffect(() => {
    const timer = setInterval(refreshStatus, POLL_MS)
    return () => clearInterval(timer)
  }, [refreshStatus])

  useEffect(() => onUpdatePending(setUpdatePending), [])

  useEffect(() => {
    let alive = true
    void ensurePersistence(store).then((s) => { if (alive) setPersistDenied(s === 'denied') }).catch(() => { /* sem a API: nada a avisar */ })
    return () => { alive = false }
  }, [store])

  const catalog = store.catalogState()
  const view = statusView({ online, pendingCount: pending.count, oldestPendingAt: pending.oldestAt, lastSyncAt: catalog?.syncedAt ?? null, offlineWindowDays: catalog?.offlineWindowDays ?? null, now })

  const value = useMemo<PdvContextValue>(() => ({ store, createCatalogSource, openHelp: () => setHelpOpen(true), refreshStatus }), [store, refreshStatus])

  return (
    <PdvContext.Provider value={value}>
      <PdvStatusBar view={view} />
      {updatePending ? <p role="status" className="border-b border-info bg-info-soft px-4 py-2 text-info-ink text-center text-[16px] font-semibold">{UPDATE_PENDING_MESSAGE}</p> : null}
      {persistDenied ? <p role="alert" className="border-b border-warn bg-warn-soft px-4 py-2 text-warn-ink text-center text-[16px] font-semibold">{PERSIST_DENIED_MESSAGE}</p> : null}
      {children}
      <PdvHelp open={helpOpen} onOpenChange={setHelpOpen} />
    </PdvContext.Provider>
  )
}
