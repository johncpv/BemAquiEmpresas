import { createContext, useContext } from 'react'
import type { NavArea } from './modules'

export interface NavigationValue {
  areas: NavArea[]
  tenantName: string
}

export const NavigationContext = createContext<NavigationValue | null>(null)

export function useNavigation(): NavigationValue {
  const value = useContext(NavigationContext)
  if (!value) throw new Error('useNavigation fora do NavigationProvider')
  return value
}
