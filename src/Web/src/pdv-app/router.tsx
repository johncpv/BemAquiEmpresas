import { createRootRoute, createRoute, createRouter, Outlet } from '@tanstack/react-router'
import { PdvPage } from '@/pages/pdv/PdvPage'
import { PdvNotFound } from './PdvNotFound'

const text = (v: unknown) => (typeof v === 'string' && v ? v : undefined)

// Uma rota só (o caixa). A barra no fim (trailingSlash 'always') mantém a página dentro do escopo /pdv/ do service worker.
const rootRoute = createRootRoute({ component: Outlet, notFoundComponent: PdvNotFound })
const pdvRoute = createRoute({ getParentRoute: () => rootRoute, path: '/pdv', component: PdvPage, validateSearch: (s: Record<string, unknown>): { registrar?: string } => ({ registrar: text(s.registrar) }) })

/** Roteador próprio do app do caixa (o do backoffice não entra no build do /pdv). */
export const pdvRouter = createRouter({ routeTree: rootRoute.addChildren([pdvRoute]), trailingSlash: 'always' })
