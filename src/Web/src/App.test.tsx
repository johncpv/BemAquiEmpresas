import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryHistory } from '@tanstack/react-router'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setAccessToken } from './api/session'
import { PAGE_WIDTH_CLASS, widthOfMatches } from './components/layout/page-width'
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
    const body = (url: string) => (url.startsWith('/api/v1/me/permissions') ? { permissions: [], isOwner: true, version: '1', modules: SAMPLE_MODULES } : url.startsWith('/api/v1/ajuda') || url.startsWith('/api/v1/assets/sites') ? [] : {})
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

  it('abas agrupadas na barra do topo, sem barra de rolagem nativa; módulo sem grupos não ganha rótulo', async () => {
    const router = renderAt('/app/estoque/itens')
    const tabs = await screen.findByRole('navigation', { name: 'Telas de Estoque' })
    expect(tabs.classList.contains('scrollbar-none')).toBe(true)
    const groups = within(tabs).getAllByRole('group')
    expect(groups.map((g) => g.getAttribute('aria-label'))).toEqual(['Operação', 'Cadastros'])
    expect(within(groups[0]).getByRole('link', { name: 'Saldos' })).toBeDefined()
    expect(within(groups[1]).getByRole('link', { name: 'Itens' }).getAttribute('aria-current')).toBe('page')
    await act(() => router.navigate({ to: '/app/acessos/grupos' }))
    const access = await screen.findByRole('navigation', { name: 'Telas de Usuários e acessos' })
    expect(within(access).queryAllByRole('group')).toEqual([])
  })

  it('título da página e foco no h1 na troca de rota (CA 11)', async () => {
    const router = renderAt('/app')
    await screen.findByRole('heading', { level: 1, name: 'Início' })
    await act(() => router.navigate({ to: '/app/patrimonio/locais' }))
    const heading = await screen.findByRole('heading', { level: 1, name: 'Patrimônio: Locais' })
    await waitFor(() => expect(document.title).toBe('Patrimônio: Locais · Empresa · BemAqui Empresas'))
    expect(document.activeElement).toBe(heading)
  })

  it('largura pelo tipo de tela: full no quadro e nos painéis, form nos cadastros, default sem declarar (ADR 0044)', async () => {
    // Declaração nas rotas (a rota mais profunda manda; sem declarar, default).
    const router = createAppRouter(createMemoryHistory({ initialEntries: ['/app'] }))
    const widthAt = (path: string) => widthOfMatches(router.matchRoutes(path, {}))
    for (const path of ['/app/producao/kanban', '/app/producao/planejamento', '/app/painel', '/app/painel/resultado-por-projeto', '/app/painel/resultado-por-projeto/p1', '/app/financeiro/dre', '/app/financeiro/fluxo-de-caixa', '/app/producao/supervisor', '/app/estoque/saldos', '/app/estoque/rastreabilidade', '/app/compras/cotacoes/c1']) expect(widthAt(path), path).toBe('full')
    for (const path of ['/app/configuracoes/empresa', '/app/configuracoes/parametros', '/app/producao/modelo-instalacao', '/app/financeiro/pagar/novo', '/app/financeiro/receber/novo', '/app/rh/funcionarios/novo', '/app/cadastros/terceiros/novo', '/app/estoque/itens/novo', '/app/producao/expedicao/novo', '/app/compras/pedidos/novo', '/app/compras/pedidos/p1/editar']) expect(widthAt(path), path).toBe('form')
    for (const path of ['/app', '/app/acessos/grupos', '/app/rh/funcionarios', '/app/rh/funcionarios/e1', '/app/financeiro/receber', '/app/producao/os']) expect(widthAt(path), path).toBe('default')

    // O shell aplica a classe do tipo num contêiner em volta do Outlet; o main fica fluido.
    const router2 = renderAt('/app')
    await screen.findByRole('heading', { level: 1, name: 'Início' })
    const container = document.querySelector('#conteudo > [data-page-width]')!
    expect(container.getAttribute('data-page-width')).toBe('default')
    expect(container.className).toBe(PAGE_WIDTH_CLASS.default)
    expect(document.getElementById('conteudo')!.className).not.toContain('max-w-')
    // Tela de formulário: o conteúdo fica à esquerda, limitado à coluna do formulário + painel (sem centralizar de novo).
    await act(() => router2.navigate({ to: '/app/conta' }))
    await waitFor(() => expect(document.querySelector('#conteudo > [data-page-width]')?.getAttribute('data-page-width')).toBe('form'))
    expect(document.querySelector('#conteudo > [data-page-width="form"] > .max-w-\\[77\\.5rem\\]')).not.toBeNull()
    expect(PAGE_WIDTH_CLASS.full).toContain('max-w-[1920px]')
    expect(PAGE_WIDTH_CLASS.default).toContain('max-w-screen-2xl')
    expect(PAGE_WIDTH_CLASS.form).toContain('max-w-screen-2xl')
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
    // PNL-01a CA 17: /tv/{token} é o link antigo, desligado, sem chamar a API.
    renderAt('/tv/abc')
    const notice = await screen.findByText('Este link antigo foi desligado. Peça um novo link a quem cuida do painel.')
    expect(notice.closest('.theme-tv')).not.toBeNull()
    expect(screen.queryByRole('navigation', { name: 'Navegação principal' })).toBeNull()
    expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).startsWith('/api/v1/kiosk'))).toBe(false)
  })

  it('/tv: Painel da TV escuro, sem dock nem barra do backoffice e sem Referer (PNL-01a CA 5 e 18)', async () => {
    renderAt('/tv')
    expect(await screen.findByRole('alert')).toBeTruthy()
    const surface = document.querySelector('[data-surface="tv"]')!
    expect(surface.classList.contains('theme-tv')).toBe(true)
    expect(document.querySelector('meta[name="referrer"]')?.getAttribute('content')).toBe('no-referrer')
    expect(screen.queryByRole('navigation', { name: 'Navegação principal' })).toBeNull()
  })
})
