import { createContext, useContext } from 'react'
import type { NavArea, NavModule } from './modules'

export interface NavigationValue {
  areas: NavArea[]
  all: NavModule[]
  tenantName: string
  status: 'loading' | 'error' | 'ready'
  retry: () => void
}

export const NavigationContext = createContext<NavigationValue | null>(null)

export function useNavigation(): NavigationValue {
  const value = useContext(NavigationContext)
  if (!value) throw new Error('useNavigation fora do NavigationProvider')
  return value
}
