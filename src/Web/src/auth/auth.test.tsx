import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryHistory } from '@tanstack/react-router'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { getAccessToken, setAccessToken } from '@/api/session'
import { App } from '@/App'
import { createAppRouter } from '@/router'
import { ThemeProvider } from '@/theme/ThemeProvider'

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  refreshSession: vi.fn(),
  login: vi.fn(),
  verifyTwoFactor: vi.fn(),
  inspectInvite: vi.fn(),
  acceptInvite: vi.fn(),
  useGetTenantPublicContext: () => ({ data: { name: 'Konebras' } }),
  useGetCurrentSession: () => ({ data: { name: 'Ana Souza', email: 'ana@konebras.com.br' } }),
}))

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

const fill = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })

describe('telas de autenticação (FND-04, FND-05)', () => {
  afterEach(() => {
    vi.clearAllMocks()
    setAccessToken(null)
  })

  it('sem sessão, /app vai para o login guardando a rota', async () => {
    vi.mocked(api.refreshSession).mockRejectedValue(new ApiError(422, {}))
    const router = renderAt('/app/financeiro')
    await screen.findByRole('heading', { name: 'Entrar' })
    expect(router.state.location.pathname).toBe('/entrar')
    expect(router.state.location.search).toEqual({ volta: '/app/financeiro' })
  })

  it('erro de login mostra a mensagem uniforme do servidor', async () => {
    vi.mocked(api.refreshSession).mockRejectedValue(new ApiError(422, {}))
    vi.mocked(api.login).mockRejectedValue(new ApiError(422, { detail: 'E-mail ou senha inválidos.', code: 'auth.invalid_credentials' }))
    renderAt('/entrar')
    await screen.findByLabelText('E-mail')
    fill('E-mail', 'ana@konebras.com.br')
    fill('Senha', 'errada')
    fireEvent.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByText('E-mail ou senha inválidos.')).toBeDefined()
  })

  it('com 2FA, a senha leva ao desafio e o código à rota pedida, com o token só em memória', async () => {
    vi.mocked(api.login).mockResolvedValue({ accessToken: null, expiresAt: null, twoFactor: { challenge: 'c1', purpose: 'verify', expiresAt: '2026-10-18T10:00:00Z' } })
    vi.mocked(api.verifyTwoFactor).mockResolvedValue({ accessToken: 'tok', expiresAt: '2026-10-18T10:15:00Z' })
    const router = renderAt('/entrar?volta=%2Fapp%2Fconta')
    await screen.findByLabelText('E-mail')
    fill('E-mail', 'ana@konebras.com.br')
    fill('Senha', 'uma frase longa de teste')
    fireEvent.click(screen.getByRole('button', { name: 'Entrar' }))
    await screen.findByLabelText('Código do app autenticador')
    fill('Código do app autenticador', '123456')
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/app/conta'))
    expect(api.verifyTwoFactor).toHaveBeenCalledWith({ challenge: 'c1', code: '123456', recoveryCode: null, ephemeral: false })
    expect(api.login).toHaveBeenCalledWith({ email: 'ana@konebras.com.br', password: 'uma frase longa de teste', ephemeral: false })
    expect(getAccessToken()).toBe('tok')
    expect(JSON.stringify(localStorage)).not.toContain('tok')
    expect(JSON.stringify(sessionStorage)).not.toContain('tok')
  })

  it('login vindo do tablet da fábrica (aparelho=1) pede sessão sem refresh e marca a sessão como do tablet (PRD-03b CA 6)', async () => {
    vi.mocked(api.refreshSession).mockRejectedValue(new ApiError(422, {}))
    vi.mocked(api.login).mockResolvedValue({ accessToken: 'tok-tablet', expiresAt: '2026-10-18T10:15:00Z', twoFactor: null })
    renderAt('/entrar?volta=%2Fapp%2Fconta&aparelho=1')
    expect(await screen.findByRole('heading', { name: 'Entrar no tablet da fábrica' })).toBeDefined()
    fill('E-mail', 'ana@konebras.com.br')
    fill('Senha', 'uma frase longa de teste')
    fireEvent.click(screen.getByRole('button', { name: 'Entrar' }))
    await waitFor(() => expect(getAccessToken()).toBe('tok-tablet'))
    expect(api.login).toHaveBeenCalledWith({ email: 'ana@konebras.com.br', password: 'uma frase longa de teste', ephemeral: true })
    expect((await import('@/api/session')).isEphemeralSession()).toBe(true)
  })

  it('sem sessão, o /fab vai para a entrada do tablet, não para o login do backoffice (PRD-03b)', async () => {
    vi.mocked(api.refreshSession).mockRejectedValue(new ApiError(422, {}))
    const router = renderAt('/fab')
    await waitFor(() => expect(router.state.location.pathname).toBe('/fab/entrar'))
  })

  it('convite expirado explica e orienta (CA 18); válido cria o acesso e oferece o 2FA (CA 17)', async () => {
    vi.mocked(api.inspectInvite).mockResolvedValueOnce({ state: 'Expired', email: null, name: null, message: 'Este convite expirou. Peça um novo ao administrador da empresa.' })
    renderAt('/convite?token=x')
    expect(await screen.findByText('Este convite expirou. Peça um novo ao administrador da empresa.')).toBeDefined()
  })

  it('convite válido: nome pré-preenchido e oferta do 2FA ao concluir', async () => {
    vi.mocked(api.inspectInvite).mockResolvedValue({ state: 'Valid', email: 'bia@konebras.com.br', name: 'Bia', message: null })
    vi.mocked(api.acceptInvite).mockResolvedValue({ email: 'bia@konebras.com.br', offerTwoFactor: true, accessToken: 'tok2', expiresAt: '2026-10-18T10:15:00Z', twoFactor: null })
    renderAt('/convite?token=y')
    expect(((await screen.findByLabelText('Seu nome')) as HTMLInputElement).value).toBe('Bia')
    fill('Senha', 'uma frase longa de teste')
    fill('Repita a senha', 'uma frase longa de teste')
    fireEvent.click(screen.getByRole('button', { name: 'Criar acesso' }))
    expect(await screen.findByRole('link', { name: 'Configurar verificação em duas etapas' })).toBeDefined()
    expect(screen.getByRole('link', { name: 'Fazer depois' })).toBeDefined()
  })
})
