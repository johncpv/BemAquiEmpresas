import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/api/generated/bemaqui'
import type { AuthorizeInspection, ConnectedAppPage } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { OAuthAuthorizePage } from '@/auth/OAuthAuthorizePage'
import type { NavModule } from '@/navigation/modules'
import { NavigationContext, type NavigationValue } from '@/navigation/navigation-context'
import { renderWithRouter } from '@/test-utils'
import { ThemeProvider } from '@/theme/ThemeProvider'
import { authorizeErrorText, authorizeRequestFrom, browser, canGainText, connectionState, normalizePairing, returnedFromConecta } from './conecta'
import { ConectaPage } from './ConectaPage'
import { INTEGRATION_V1_ROUTES } from './integration-v1-routes'

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  inspectOAuthAuthorize: vi.fn(),
  consentOAuthAuthorize: vi.fn(),
  denyOAuthAuthorize: vi.fn(),
  startConectaConnection: vi.fn(),
  revokeConnectedApp: vi.fn(),
  useGetConectaConnection: vi.fn(),
  useGetTenantPublicContext: () => ({ data: { name: 'Empresa A' } }),
  useGetOutboundWebhookSummary: () => ({ isPending: true }),
}))

const module = (code: string, actions: string[]) =>
  ({ code, name: code, area: 'Sistema', released: true, enabled: true, actions, slug: code, aliases: [], path: '/', icon: () => null, tabs: [], locked: false }) as unknown as NavModule

const navigation: NavigationValue = {
  areas: [], all: [module('settings', ['view', 'edit']), module('conecta', ['view', 'edit'])], tenantName: 'Empresa A', status: 'ready', isOwner: true,
  retry: () => undefined, vocabulary: {}, areaLabels: {},
}

function renderPage(ui: () => ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return renderWithRouter(() => (
    <QueryClientProvider client={client}>
      <ThemeProvider>
        <NavigationContext.Provider value={navigation}>{ui()}</NavigationContext.Provider>
      </ThemeProvider>
    </QueryClientProvider>
  ))
}

const consent: AuthorizeInspection = {
  outcome: 'consent', errorCode: null, message: null, redirectTo: null, appName: 'Conecta', accountLabel: 'Loja do João', tenantName: 'Empresa A',
  granted: [{ scope: 'parties.view', text: 'Ver clientes (nome, telefone, e-mail, documento)' }, { scope: 'finance.view', text: 'Ver cobranças' }],
  notHeld: [{ scope: 'sales.view', text: 'Ver pedidos de venda' }],
}

describe('regras da tela do Conecta (INT-01)', () => {
  it('INT-04: código de pareamento no formato, volta do Conecta e o que pode ganhar', () => {
    expect(normalizePairing(' abcd2345 ')).toBe('ABCD-2345')
    expect(normalizePairing('ABCD-0000')).toBeNull()
    expect(normalizePairing('ABCI-2345')).toBeNull()
    expect(normalizePairing('ABC-2345')).toBeNull()
    expect(returnedFromConecta('?conectado=1')).toBe(true)
    expect(returnedFromConecta('')).toBe(false)
    expect(canGainText([])).toBeNull()
    expect(canGainText([{ text: 'A' }, { text: 'B' }])).toBe('O Conecta pode ganhar: A · B')
  })

  it('chip e ação por situação (CA 20)', () => {
    expect(connectionState(null)).toEqual({ tone: 'info', action: 'connect' })
    expect(connectionState({ status: 'Active', reason: 'None' })).toEqual({ tone: 'ok', action: 'disconnect' })
    expect(connectionState({ status: 'Suspended', reason: 'GrantorInactive' })).toEqual({ tone: 'warn', action: 'reauthorize' })
    expect(connectionState({ status: 'Revoked', reason: 'CredentialReuse' })).toEqual({ tone: 'crit', action: 'connect' })
    expect(connectionState({ status: 'Revoked', reason: 'DisconnectedByUser' })).toEqual({ tone: 'info', action: 'connect' })
  })

  it('o pedido vai ao ERP como chegou na URL e os erros têm texto próprio', () => {
    expect(authorizeRequestFrom(new URLSearchParams('response_type=code&client_id=conecta&state=a%20b&intent=xyz&account_label=Loja'))).toMatchObject({
      responseType: 'code', clientId: 'conecta', state: 'a b', intent: 'xyz', accountLabel: 'Loja', scope: null,
    })
    expect(authorizeErrorText('auth.2fa_invalid_code')).toBe('Código incorreto. Confira no aplicativo autenticador e tente de novo.')
    expect(authorizeErrorText('connect_intent_used')).toBe('Este pedido de conexão não foi iniciado por você neste ERP.')
  })
})

describe('Autorizar o Conecta (INT-01 CA 6 e 8)', () => {
  afterEach(() => vi.restoreAllMocks())

  it('mostra o que o Conecta poderá fazer e o que fica de fora; código errado volta o foco ao campo; sucesso volta ao Conecta', async () => {
    vi.spyOn(browser, 'search').mockReturnValue('?response_type=code&client_id=conecta&intent=i1')
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => undefined)
    vi.mocked(api.inspectOAuthAuthorize).mockResolvedValue(consent)
    vi.mocked(api.consentOAuthAuthorize)
      .mockRejectedValueOnce(new ApiError(422, { detail: 'x', code: 'auth.2fa_invalid_code' }))
      .mockResolvedValueOnce({ redirectTo: 'https://conecta.test/cb?code=c&state=s' })
    renderPage(() => <OAuthAuthorizePage />)

    expect(await screen.findByText('Ver cobranças')).toBeTruthy()
    expect(screen.getByText('O Conecta não vai receber, porque você não tem esse acesso')).toBeTruthy()
    expect(screen.getByText('Ver pedidos de venda')).toBeTruthy()
    const input = screen.getByLabelText('Código do aplicativo autenticador')
    expect((screen.getByRole('button', { name: 'Autorizar' }) as HTMLButtonElement).disabled).toBe(true)

    fireEvent.change(input, { target: { value: '000000' } })
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }))
    expect(await screen.findByText('Código incorreto. Confira no aplicativo autenticador e tente de novo.')).toBeTruthy()
    expect(document.activeElement).toBe(input)

    fireEvent.change(input, { target: { value: '123456' } })
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }))
    expect(await screen.findByText('Conectado. Voltando ao Conecta…')).toBeTruthy()
    expect(assign).toHaveBeenCalledWith('https://conecta.test/cb?code=c&state=s')
    expect(document.querySelector('meta[name="referrer"]')?.getAttribute('content')).toBe('no-referrer')
  })

  it('sem 2FA ativo oferece "Ativar 2FA"; link inválido leva a Integrações → Conecta', async () => {
    vi.spyOn(browser, 'search').mockReturnValue('?intent=i1')
    vi.mocked(api.inspectOAuthAuthorize).mockResolvedValue(consent)
    vi.mocked(api.consentOAuthAuthorize).mockRejectedValue(new ApiError(403, { detail: 'x', code: 'two_factor_required', enrolled: false }))
    const first = renderPage(() => <OAuthAuthorizePage />)
    fireEvent.change(await screen.findByLabelText('Código do aplicativo autenticador'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }))
    expect(await screen.findByRole('link', { name: 'Ativar 2FA' })).toBeTruthy()
    first.unmount()

    vi.mocked(api.inspectOAuthAuthorize).mockResolvedValue({ ...consent, outcome: 'error', message: 'Este pedido de conexão não foi iniciado por você neste ERP.', granted: [], notHeld: [] })
    renderPage(() => <OAuthAuthorizePage />)
    expect(await screen.findByText('Este pedido de conexão não foi iniciado por você neste ERP.')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Ir para Integrações → Conecta' })).toBeTruthy()
  })
})

const page = (connection: ConnectedAppPage['connection']): ConnectedAppPage => ({
  clientId: 'conecta', name: 'Conecta', configured: true, allowedScopes: [{ scope: 'sales.view', text: 'Ver pedidos de venda' }], connection,
})

const query = (value: Partial<ReturnType<typeof api.useGetConectaConnection>>) =>
  vi.mocked(api.useGetConectaConnection).mockReturnValue({ isPending: false, isError: false, refetch: vi.fn(), ...value } as unknown as ReturnType<typeof api.useGetConectaConnection>)

describe('Configurações → Integrações → Conecta (INT-01 CA 20 e 21)', () => {
  afterEach(() => vi.restoreAllMocks())

  it('sem conexão: o que o Conecta faz e "Conectar", que leva ao Conecta', async () => {
    query({ data: page(null) })
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => undefined)
    vi.mocked(api.startConectaConnection).mockResolvedValue({ redirectUrl: 'https://conecta.test/conectar?erp=a&intent=x' })
    renderPage(() => <ConectaPage />)
    expect(await screen.findByText('Não conectado')).toBeTruthy()
    expect(screen.getByText('Ver pedidos de venda')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Conectar' }))
    expect(await screen.findByText('Você vai confirmar no Conecta com um usuário administrador.')).toBeTruthy()
    const confirm = () => screen.getAllByRole('button', { name: 'Conectar' }).at(-1)!
    expect((confirm() as HTMLButtonElement).disabled).toBe(true)
    fireEvent.change(screen.getByLabelText('Código de pareamento'), { target: { value: 'abcd2345' } })
    await waitFor(() => expect((confirm() as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(confirm())
    await waitFor(() => expect(api.startConectaConnection).toHaveBeenCalledWith({ pairingCode: 'ABCD-2345' }))
    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://conecta.test/conectar?erp=a&intent=x'))
  })

  it('INT-04: pode ganhar escopos novos com "Autorizar de novo" e, na volta do Conecta, mostra a conta e o próximo passo', async () => {
    vi.spyOn(browser, 'search').mockReturnValue('?conectado=1')
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => undefined)
    vi.mocked(api.startConectaConnection).mockResolvedValue({ redirectUrl: 'https://conecta.test/conectar?erp=a&intent=y' })
    query({
      data: page({
        id: 'c1', status: 'Active', reason: 'None', statusText: 'Conectado', reasonText: 'Conectado em 28/11 por Paula Lima', accountLabel: 'Loja do João',
        grantorUserId: 'u1', grantorName: 'Paula Lima', connectedAt: '2026-11-28T10:00:00Z', reconsentDueAt: '2027-11-28T10:00:00Z', lastUsedAt: null, revokedAt: null,
        scopes: [{ scope: 'finance.view', text: 'Ver cobranças' }],
        canGain: [{ scope: 'quotes.create', text: 'Abrir pedidos de orçamento' }, { scope: 'conecta.send_whatsapp', text: 'Mandar avisos do ERP pelo WhatsApp do Conecta' }],
      }),
    })
    renderPage(() => <ConectaPage />)
    expect(await screen.findByText('O Conecta pode ganhar: Abrir pedidos de orçamento · Mandar avisos do ERP pelo WhatsApp do Conecta')).toBeTruthy()
    expect(screen.getByText('Conectado à conta Loja do João do Conecta')).toBeTruthy()
    expect(screen.getByText('Próximo passo: abra uma conversa no Conecta. O painel do cliente mostra pedidos e cobranças.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar de novo' }))
    await waitFor(() => expect(api.startConectaConnection).toHaveBeenCalledWith({ pairingCode: null }))
    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://conecta.test/conectar?erp=a&intent=y'))
  })

  it('"Para desenvolvedores" traz as rotas da API v1 com o escopo, geradas do OpenAPI integration-v1 (INT-03)', async () => {
    query({ data: page(null) })
    renderPage(() => <ConectaPage />)
    fireEvent.click(await screen.findByText('Para desenvolvedores'))
    const routes = screen.getByRole('list', { name: 'Rotas da API de integração v1' })
    expect(within(routes).getByText('/api/v1/integration/orders')).toBeTruthy()
    expect(within(routes).getAllByText('sales.create').length).toBeGreaterThan(0)
    expect(INTEGRATION_V1_ROUTES.every((r) => r.path.startsWith('/api/v1/integration/'))).toBe(true)
  })

  it('conectado: chip, motivo e "Desconectar" com o impacto no diálogo', async () => {
    query({
      data: page({
        id: 'c1', status: 'Active', reason: 'None', statusText: 'Conectado', reasonText: 'Conectado em 27/11 por Paula Lima · conta Loja do João', accountLabel: 'Loja do João',
        grantorUserId: 'u1', grantorName: 'Paula Lima', connectedAt: '2026-11-27T10:00:00Z', reconsentDueAt: '2027-11-27T10:00:00Z', lastUsedAt: null, revokedAt: null,
        scopes: [{ scope: 'finance.view', text: 'Ver cobranças' }],
      }),
    })
    vi.mocked(api.revokeConnectedApp).mockResolvedValue(undefined as never)
    renderPage(() => <ConectaPage />)
    expect(await screen.findByText('Conectado em 27/11 por Paula Lima · conta Loja do João')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Desconectar' }))
    expect(await screen.findByText('O Conecta para de atender pedidos e cobranças por WhatsApp agora. Pedidos já criados continuam no ERP.')).toBeTruthy()
    const buttons = screen.getAllByRole('button', { name: 'Desconectar' })
    fireEvent.click(buttons[buttons.length - 1])
    await waitFor(() => expect(api.revokeConnectedApp).toHaveBeenCalledWith('c1'))
  })

  it('suspensa oferece "Autorizar de novo"; erro e módulo não liberado têm a tela própria', async () => {
    query({
      data: page({
        id: 'c1', status: 'Suspended', reason: 'GrantorInactive', statusText: 'Suspensa', reasonText: 'Quem autorizou perdeu o acesso', accountLabel: null,
        grantorUserId: 'u1', grantorName: 'Paula Lima', connectedAt: '2026-11-27T10:00:00Z', reconsentDueAt: '2027-11-27T10:00:00Z', lastUsedAt: null, revokedAt: null, scopes: [],
      }),
    })
    const first = renderPage(() => <ConectaPage />)
    expect(await screen.findByRole('button', { name: 'Autorizar de novo' })).toBeTruthy()
    first.unmount()

    query({ isError: true, error: new Error('x') as never })
    const second = renderPage(() => <ConectaPage />)
    expect(await screen.findByText('Não foi possível carregar a conexão.')).toBeTruthy()
    second.unmount()

    query({ isError: true, error: new ApiError(403, { code: 'module_not_released' }) as never })
    renderPage(() => <ConectaPage />)
    expect(await screen.findByText('Funcionalidade não liberada nessa versão do sistema.')).toBeTruthy()
  })
})
