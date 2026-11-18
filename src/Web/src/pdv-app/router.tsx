import { createRootRoute, createRoute, createRouter, Outlet } from '@tanstack/react-router'
import { FloorState } from '@/components/shopfloor/kit'
import { PdvPage } from '@/pages/pdv/PdvPage'
import { Centered } from './Centered'

const text = (v: unknown) => (typeof v === 'string' && v ? v : undefined)

function NotFound() {
  return (
    <Centered>
      <FloorState title="Esta página não existe no caixa." action={<a href="/pdv/" className="inline-flex min-h-[56px] items-center rounded-md bg-primary px-5 text-[18px] font-semibold text-primary-foreground">Voltar ao caixa</a>} />
    </Centered>
  )
}

// Uma rota só (o caixa). A barra no fim (trailingSlash 'always') mantém a página dentro do escopo /pdv/ do service worker.
const rootRoute = createRootRoute({ component: Outlet, notFoundComponent: NotFound })
const pdvRoute = createRoute({ getParentRoute: () => rootRoute, path: '/pdv', component: PdvPage, validateSearch: (s: Record<string, unknown>): { registrar?: string } => ({ registrar: text(s.registrar) }) })

/** Roteador próprio do app do caixa (o do backoffice não entra no build do /pdv). */
export const pdvRouter = createRouter({ routeTree: rootRoute.addChildren([pdvRoute]), trailingSlash: 'always' })
