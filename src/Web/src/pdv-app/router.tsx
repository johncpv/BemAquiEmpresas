import { createRootRoute, createRoute, createRouter, Outlet } from '@tanstack/react-router'
import { PdvPage } from '@/pages/pdv/PdvPage'
import { PdvPrinterSetup } from '@/pages/pdv/print/PdvPrinterSetup'
import { PdvNotFound } from './PdvNotFound'

const text = (v: unknown) => (typeof v === 'string' && v ? v : undefined)

// O caixa e o assistente da impressora (IMP-01 CA 6). A barra no fim (trailingSlash 'always') mantém a página dentro do escopo /pdv/ do service worker.
const rootRoute = createRootRoute({ component: Outlet, notFoundComponent: PdvNotFound })
const pdvRoute = createRoute({ getParentRoute: () => rootRoute, path: '/pdv', component: PdvPage, validateSearch: (s: Record<string, unknown>): { registrar?: string } => ({ registrar: text(s.registrar) }) })
const printerRoute = createRoute({ getParentRoute: () => rootRoute, path: '/pdv/config/impressora', component: PdvPrinterSetup })

/** Roteador próprio do app do caixa (o do backoffice não entra no build do /pdv). */
export const pdvRouter = createRouter({ routeTree: rootRoute.addChildren([pdvRoute, printerRoute]), trailingSlash: 'always' })
