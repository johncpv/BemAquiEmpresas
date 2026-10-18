import { useMemo, type ReactNode } from 'react'
import { buildNavigation, type PermissionModule } from './modules'
import { NavigationContext } from './navigation-context'
import { SAMPLE_MODULES } from './sample'

/** Menu montado a partir das permissões do usuário (FND-09b; a FND-06d passa a ler /me/permissions). */
export function NavigationProvider({ modules = SAMPLE_MODULES, tenantName = 'Empresa', children }: { modules?: readonly PermissionModule[]; tenantName?: string; children: ReactNode }) {
  const value = useMemo(() => ({ areas: buildNavigation(modules), tenantName }), [modules, tenantName])
  return <NavigationContext.Provider value={value}>{children}</NavigationContext.Provider>
}
