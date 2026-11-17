import { useSyncExternalStore, useMemo, type ReactNode } from 'react'
import { useGetMyPermissions } from '@/api/generated/bemaqui'
import { getAccessToken, onSessionChange } from '@/api/session'
import { buildNavigation, type PermissionModule } from './modules'
import { NavigationContext } from './navigation-context'
import type { Vocabulary } from './vocabulary'

/**
 * Menu derivado de /me/permissions (FND-06d): nenhuma lista fixa no front. Quando o backend manda X-Perm-Version nova,
 * a consulta é invalidada e dock, paleta e rotas são remontados (CA 6). <c>modules</c> fixo só em testes e na galeria.
 */
const NONE: Readonly<Record<string, string>> = {}

export function NavigationProvider({ modules, isOwner: ownerOverride, profiles: profilesOverride, tenantName = 'Empresa', tenantLogo, areaLabels: areaLabelsOverride, vocabulary: vocabularyOverride, children }: { modules?: readonly PermissionModule[]; isOwner?: boolean; profiles?: readonly string[]; tenantName?: string; tenantLogo?: string | null; areaLabels?: Readonly<Record<string, string>>; vocabulary?: Vocabulary; children: ReactNode }) {
  const token = useSyncExternalStore(onSessionChange, getAccessToken, () => null)
  const query = useGetMyPermissions({ query: { enabled: !modules && !!token, staleTime: 60_000 } })
  const source = modules ?? query.data?.modules
  const status = modules ? 'ready' : query.isError ? 'error' : source ? 'ready' : 'loading'

  const value = useMemo(() => {
    const isOwner = ownerOverride ?? query.data?.isOwner ?? false
    const profiles = profilesOverride ?? query.data?.profiles ?? []
    // PERF-03: rótulos de área e vocabulário do perfil; vazios para a fábrica (e nos testes e na galeria).
    const areaLabels = (modules ? areaLabelsOverride : query.data?.areaLabels) ?? NONE
    const vocabulary = (modules ? vocabularyOverride : query.data?.vocabulary) ?? NONE
    const navigation = buildNavigation(source ?? [], isOwner, profiles, areaLabels)
    return { ...navigation, isOwner, profiles, tenantName, tenantLogo, status, areaLabels, vocabulary, retry: () => void query.refetch() } as const
  }, [source, modules, ownerOverride, profilesOverride, tenantName, tenantLogo, status, query, areaLabelsOverride, vocabularyOverride])
  return <NavigationContext.Provider value={value}>{children}</NavigationContext.Provider>
}
