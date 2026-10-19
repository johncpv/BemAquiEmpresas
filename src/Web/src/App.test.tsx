import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryHistory } from '@tanstack/react-router'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setAccessToken } from './api/session'
import { SAMPLE_MODULES } from './navigation/sample'
import { App } from './App'
import { createAppRouter } from './router'
import { ThemeProvider } from './theme/ThemeProvider'

function renderAt(path: string) {
  const router = createAppRouter(createMemoryHistory({ initialEntries: [path] }))
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ThemeProvider>
        <App appRouter={router} />
      </ThemeProvider>
    </QueryClientProvider>,
  )
  return router
}

describe('shell do backoffice (FND-09b)', () => {
  // Sessão em memória e API respondendo vazio: o guarda de /app deixa passar.
  beforeEach(() => {
    setAccessToken('token-de-teste')
    const body = (url: string) => (url.startsWith('/api/v1/me/permissions') ? { permissions: [], isOwner: true, version: '1', modules: SAMPLE_MODULES } : url.startsWith('/api/v1/ajuda') ? [] : {})
    vi.stubGlobal('fetch', vi.fn(async (url: string) => new Response(JSON.stringify(body(url)), { status: 200, headers: { 'Content-Type': 'application/json' } })))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('dock com as 7 áreas na ordem, dentro de nav rotulado (CA 1)', async () => {
    renderAt('/app')
    const nav = await screen.findByRole('navigation', { name: 'Navegação principal' })
    const areas = [...nav.querySelectorAll('button')].map((b) => b.textContent)
    expect(areas).toEqual(['Visão geral', 'Comercial', 'Fábrica', 'Financeiro', 'Pessoas', 'Telas', 'Sistema'])
  })

  it('módulo aberto marca a área no dock e mostra as abas e a trilha (CA 3, 8)', async () => {
    renderAt('/app/acessos/grupos')
    const sistema = await screen.findByRole('button', { name: 'Sistema' })
    expect(sistema.getAttribute('aria-current')).toBe('page')
    const tabs = screen.getByRole('navigation', { name: 'Telas de Usuários e acessos' })
    expect(tabs.querySelector('[aria-current="page"]')?.textContent).toBe('Grupos')
    expect(screen.getByRole('navigation', { name: 'Trilha' }).textContent).toContain('Usuários e acessos')
  })

  it('título da página e foco no h1 na troca de rota (CA 11)', async () => {
    const router = renderAt('/app')
    await screen.findByRole('heading', { level: 1, name: 'Início' })
    await act(() => router.navigate({ to: '/app/$module', params: { module: 'compras' } }))
    const heading = await screen.findByRole('heading', { level: 1, name: 'Compras' })
    await waitFor(() => expect(document.title).toBe('Compras · Empresa · BemAqui Empresas'))
    expect(document.activeElement).toBe(heading)
  })

  it('primeiro item focável é "Pular para o conteúdo" (CA 12)', async () => {
    renderAt('/app')
    await screen.findByRole('heading', { level: 1 })
    const first = document.querySelector('a, button')
    expect(first?.textContent).toBe('Pular para o conteúdo')
    expect(first?.getAttribute('href')).toBe('#conteudo')
  })

  it('Ctrl K abre a paleta e a busca acha sub-tela (CA 9)', async () => {
    renderAt('/app')
    await screen.findByRole('heading', { level: 1 })
    act(() => {
      fireEvent.keyDown(window, { key: 'k', ctrlKey: true })
    })
    const input = await screen.findByPlaceholderText('Buscar tela ou módulo…')
    fireEvent.change(input, { target: { value: 'Grupos' } })
    expect(await screen.findByText('Usuários e acessos › Grupos')).toBeDefined()
  })

  it('/tv tem layout próprio, escuro e sem dock (CA 13)', async () => {
    renderAt('/tv/abc')
    const heading = await screen.findByRole('heading', { name: 'Painel da fábrica' })
    expect(heading.closest('.theme-tv')).not.toBeNull()
    expect(screen.queryByRole('navigation', { name: 'Navegação principal' })).toBeNull()
  })
})
