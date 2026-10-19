import { useSyncExternalStore, useMemo, type ReactNode } from 'react'
import { useGetMyPermissions } from '@/api/generated/bemaqui'
import { getAccessToken, onSessionChange } from '@/api/session'
import { buildNavigation, type PermissionModule } from './modules'
import { NavigationContext } from './navigation-context'

/**
 * Menu derivado de /me/permissions (FND-06d): nenhuma lista fixa no front. Quando o backend manda X-Perm-Version nova,
 * a consulta é invalidada e dock, paleta e rotas são remontados (CA 6). <c>modules</c> fixo só em testes e na galeria.
 */
export function NavigationProvider({ modules, tenantName = 'Empresa', tenantLogo, children }: { modules?: readonly PermissionModule[]; tenantName?: string; tenantLogo?: string | null; children: ReactNode }) {
  const token = useSyncExternalStore(onSessionChange, getAccessToken, () => null)
  const query = useGetMyPermissions({ query: { enabled: !modules && !!token, staleTime: 60_000 } })
  const source = modules ?? query.data?.modules
  const status = modules ? 'ready' : query.isError ? 'error' : source ? 'ready' : 'loading'

  const value = useMemo(() => {
    const navigation = buildNavigation(source ?? [])
    return { ...navigation, tenantName, tenantLogo, status, retry: () => void query.refetch() } as const
  }, [source, tenantName, tenantLogo, status, query])
  return <NavigationContext.Provider value={value}>{children}</NavigationContext.Provider>
}
