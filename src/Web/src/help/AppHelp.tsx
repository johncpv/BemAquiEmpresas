import { useQueryClient } from '@tanstack/react-query'
import { useRouterState } from '@tanstack/react-router'
import { type ReactNode, useEffect, useMemo, useRef, useState } from 'react'
import { getListSeenHelpQueryKey, markHelpSeen, useListHelp, useListSeenHelp } from '@/api/generated/bemaqui'
import { HelpContext } from './help-context'
import { HelpPanel } from './HelpPanel'
import { normalizeRoute, resolveHelp } from './resolve'

/**
 * Ajuda do backoffice (FND-14): resolve a ajuda pela rota e pela aba, abre sozinha na primeira visita (uma vez por
 * usuário e tela, em qualquer dispositivo) e registra "já vista" no servidor.
 */
export function AppHelp({ children }: { children: ReactNode }) {
  const client = useQueryClient()
  const [open, setOpen] = useState(false)
  const [tab, setTab] = useState<string | null>(null)
  const routeId = useRouterState({ select: (s) => s.matches[s.matches.length - 1]?.routeId ?? '/' })
  const docs = useListHelp()
  const seen = useListSeenHelp()
  const list = Array.isArray(docs.data) ? docs.data : undefined
  const current = useMemo(() => (list ? resolveHelp(list, normalizeRoute(routeId), tab) : undefined), [list, routeId, tab])
  const marked = useRef(new Set<string>())

  useEffect(() => {
    if (!current || !Array.isArray(seen.data) || seen.data.includes(current.key) || marked.current.has(current.key)) return
    marked.current.add(current.key)
    setOpen(true)
    void markHelpSeen(current.key).then(() => client.invalidateQueries({ queryKey: getListSeenHelpQueryKey() }))
  }, [current, seen.data, client])

  const value = useMemo(() => ({ open, setOpen, tab, setTab }), [open, tab])
  return (
    <HelpContext.Provider value={value}>
      {children}
      <HelpPanel open={open} onOpenChange={setOpen} docs={list} current={current} status={docs.isPending ? 'loading' : docs.isError ? 'error' : 'ready'} retry={() => void docs.refetch()} />
    </HelpContext.Provider>
  )
}
