import { createMemoryHistory, createRootRoute, createRouter, RouterProvider } from '@tanstack/react-router'
import { render } from '@testing-library/react'
import type { ReactNode } from 'react'

/** Renderiza dentro de um roteador de memória (componentes que leem e escrevem a busca da URL). */
export function renderWithRouter(ui: () => ReactNode, path = '/') {
  const rootRoute = createRootRoute({ component: ui })
  const router = createRouter({ routeTree: rootRoute, history: createMemoryHistory({ initialEntries: [path] }) })
  return { router, ...render(<RouterProvider router={router} />) }
}
