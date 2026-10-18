import { RouterProvider } from '@tanstack/react-router'
import { useGetTenantPublicContext } from '@/api/generated/bemaqui'
import { NavigationProvider } from '@/navigation/NavigationProvider'
import { router } from './router'

export function App({ appRouter = router }: { appRouter?: typeof router }) {
  // Nome do tenant pela rota pública de contexto (ADR 0009): marca na topbar e no título das telas.
  // Na plataforma não há tenant: a marca é a do BemAqui (FND-13).
  const platform = window.location.pathname.startsWith('/plataforma')
  const tenant = useGetTenantPublicContext({ query: { retry: false, staleTime: Infinity, enabled: !platform } })
  return (
    <NavigationProvider tenantName={platform ? 'BemAqui Plataforma' : (tenant.data?.name ?? 'Empresa')}>
      <RouterProvider router={appRouter} />
    </NavigationProvider>
  )
}
