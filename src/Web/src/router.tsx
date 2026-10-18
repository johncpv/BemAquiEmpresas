import { createRootRoute, createRoute, createRouter, Outlet, redirect } from '@tanstack/react-router'
import { lazy } from 'react'
import { ShopfloorLayout, TvLayout } from '@/pages/DedicatedLayouts'
import { Inicio } from '@/pages/Inicio'
import { ModulePage, NotFound } from '@/pages/ModulePage'
import { AppShell } from '@/shell/AppShell'

const Gallery = import.meta.env.DEV ? lazy(() => import('./dev/Gallery')) : null

const rootRoute = createRootRoute({ component: Outlet, notFoundComponent: NotFound })
const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/app' })
  },
})

// Superfícies com layout próprio (02-arquitetura §4): /app backoffice, /fab operador, /tv painel. /portal reservado.
const appRoute = createRoute({ getParentRoute: () => rootRoute, path: '/app', component: AppShell })
const appIndexRoute = createRoute({ getParentRoute: () => appRoute, path: '/', component: Inicio })
const moduleRoute = createRoute({ getParentRoute: () => appRoute, path: '$module', component: ModulePage })
const moduleTabRoute = createRoute({ getParentRoute: () => appRoute, path: '$module/$tab', component: ModulePage })
const fabRoute = createRoute({ getParentRoute: () => rootRoute, path: '/fab', component: ShopfloorLayout })
const tvRoute = createRoute({ getParentRoute: () => rootRoute, path: '/tv/$token', component: TvLayout })
const galleryRoute = createRoute({ getParentRoute: () => rootRoute, path: '/dev/galeria', component: Gallery ?? NotFound })

export const routeTree = rootRoute.addChildren([
  indexRoute,
  appRoute.addChildren([appIndexRoute, moduleRoute, moduleTabRoute]),
  fabRoute,
  tvRoute,
  galleryRoute,
])

export function createAppRouter(history?: Parameters<typeof createRouter>[0]['history']) {
  return createRouter({ routeTree, history, defaultPreload: 'intent' })
}

export const router = createAppRouter()

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
