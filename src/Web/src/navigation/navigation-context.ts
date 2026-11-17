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
  /** Perfis de negócio ativos do tenant (ADR 0046), para os recursos por perfil (adendo da PAG-01). */
  profiles?: readonly string[]
}

export const NavigationContext = createContext<NavigationValue | null>(null)

/** O tenant tem algum dos perfis (recurso por perfil dentro de um módulo comum, adendo da ADR 0046). */
export function useHasProfile(profiles: readonly string[]): boolean {
  const { profiles: mine = [] } = useNavigation()
  return profiles.some((p) => mine.includes(p))
}

export function useNavigation(): NavigationValue {
  const value = useContext(NavigationContext)
  if (!value) throw new Error('useNavigation fora do NavigationProvider')
  return value
}
