import { useRouterState } from '@tanstack/react-router'
import { type ReactNode, useMemo, useState } from 'react'
import { useListHelp } from '@/api/generated/bemaqui'
import { HelpContext } from './help-context'
import { HelpPanel } from './HelpPanel'
import { normalizeRoute, resolveHelp } from './resolve'

/**
 * Ajuda das telas dedicadas (PRD-03a CA 24, padrão 1): a mesma ajuda por rota e aba do backoffice, aberta só pelo botão "Ajuda"
 * do cabeçalho. No /fab ela nunca abre sozinha no meio do turno (a abertura automática fica para a entrada, /fab/entrar, PRD-03b).
 */
export function FloorHelp({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false)
  const [tab, setTab] = useState<string | null>(null)
  const routeId = useRouterState({ select: (s) => s.matches[s.matches.length - 1]?.routeId ?? '/' })
  const docs = useListHelp()
  const list = Array.isArray(docs.data) ? docs.data : undefined
  const current = useMemo(() => (list ? resolveHelp(list, normalizeRoute(routeId), tab) : undefined), [list, routeId, tab])
  const value = useMemo(() => ({ open, setOpen, tab, setTab }), [open, tab])
  return (
    <HelpContext.Provider value={value}>
      {children}
      <HelpPanel open={open} onOpenChange={setOpen} docs={list} current={current} status={docs.isPending ? 'loading' : docs.isError ? 'error' : 'ready'} retry={() => void docs.refetch()} />
    </HelpContext.Provider>
  )
}