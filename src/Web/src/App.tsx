import { RouterProvider } from '@tanstack/react-router'
import { useGetTenantPublicContext } from '@/api/generated/bemaqui'
import { NavigationProvider } from '@/navigation/NavigationProvider'
import { router } from './router'

export function App({ appRouter = router }: { appRouter?: typeof router }) {
  // Nome do tenant pela rota pública de contexto (ADR 0009): marca na topbar e no título das telas.
  const tenant = useGetTenantPublicContext({ query: { retry: false, staleTime: Infinity } })
  return (
    <NavigationProvider tenantName={tenant.data?.name ?? 'Empresa'}>
      <RouterProvider router={appRouter} />
    </NavigationProvider>
  )
}
