import { createContext, useContext, useEffect } from 'react'

export interface HelpState {
  open: boolean
  setOpen: (open: boolean) => void
  tab: string | null
  setTab: (tab: string | null) => void
}

export const HelpContext = createContext<HelpState>({ open: false, setOpen: () => {}, tab: null, setTab: () => {} })

export const useHelp = () => useContext(HelpContext)

/** A tela informa a "aba" de ajuda dela quando não é a padrão da rota (ex.: módulo não liberado). */
export function useHelpTab(tab: string | null) {
  const { setTab } = useHelp()
  useEffect(() => {
    setTab(tab)
    return () => setTab(null)
  }, [tab, setTab])
}
