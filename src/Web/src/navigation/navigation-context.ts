import { createContext, useContext } from 'react'
import type { NavArea, NavModule } from './modules'

export interface NavigationValue {
  areas: NavArea[]
  all: NavModule[]
  tenantName: string
  tenantLogo?: string | null
  status: 'loading' | 'error' | 'ready'
  /** Membro do grupo Proprietário (abas só dele, ex.: Privacidade, LGPD-01a CA 2). */
  isOwner: boolean
  retry: () => void
}

export const NavigationContext = createContext<NavigationValue | null>(null)

export function useNavigation(): NavigationValue {
  const value = useContext(NavigationContext)
  if (!value) throw new Error('useNavigation fora do NavigationProvider')
  return value
}
