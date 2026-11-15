import { QueryClientProvider, useQueryClient } from '@tanstack/react-query'
import { type ReactNode, useEffect, useMemo, useSyncExternalStore } from 'react'
import { getGetMyPermissionsQueryKey, saveMyPreferences, useGetMyPreferences } from '@/api/generated/bemaqui'
import { renewSession } from '@/api/http'
import { apiNotificationsSource } from '@/api/notifications-source'
import { queryClient } from '@/api/query-client'
import { getAccessToken, isPinSession, onSessionChange, setPermVersionListener } from '@/api/session'
import { StepUpHost } from '@/components/ui/step-up-dialog'
import { NotificationsContext } from '@/shell/notifications'
import { isThemePreference } from '@/theme/theme'
import { ThemeProvider } from '@/theme/ThemeProvider'

const emptySource = { list: async () => ({ items: [], unread: 0 }), markRead: async () => {} }

/** Liga sessão, tema do perfil, sino e versão de permissões aos dados da API (FND-09a CA 9, FND-06c CA 15). */
function SessionBridge({ children }: { children: ReactNode }) {
  const client = useQueryClient()
  const token = useSyncExternalStore(onSessionChange, getAccessToken, () => null)
  // Sessão por PIN (ADR 0026 §4): só a lista permitida; preferências e sino ficam fora (o tema do /fab é do aparelho).
  const pin = isPinSession(token)
  const preferences = useGetMyPreferences({ query: { enabled: !!token && !pin } })
  const theme = preferences.data?.theme

  useEffect(() => {
    // Retoma a sessão pelo cookie de refresh (HttpOnly) ao abrir o app; sem cookie, segue sem sessão.
    // Mesma promessa do guarda de rota (renewSession): uma carga de página faz um único POST /auth/refresh.
    if (!getAccessToken() && !window.location.pathname.startsWith('/plataforma')) void renewSession()
  }, [])

  useEffect(() => {
    setPermVersionListener(() => client.invalidateQueries({ queryKey: getGetMyPermissionsQueryKey() }))
    return () => setPermVersionListener(null)
  }, [client])

  const source = useMemo(() => (token && !pin ? apiNotificationsSource : emptySource), [token, pin])
  return (
    <ThemeProvider profileTheme={isThemePreference(theme) ? theme : null} onPersist={(t) => token && !pin && saveMyPreferences({ theme: t }).catch(() => {})}>
      <NotificationsContext.Provider value={source}>{children}</NotificationsContext.Provider>
    </ThemeProvider>
  )
}

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <SessionBridge>{children}</SessionBridge>
      {/* Passo "Confirme o código" do 2FA de novo, único no app (BNK-01a CA 1). */}
      <StepUpHost />
    </QueryClientProvider>
  )
}
