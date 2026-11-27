import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import * as api from '@/api/generated/bemaqui'
import type { OutboundDeliveryList, OutboundWebhookSummary } from '@/api/generated/model'
import type { NavModule } from '@/navigation/modules'
import { NavigationContext, type NavigationValue } from '@/navigation/navigation-context'
import { renderWithRouter } from '@/test-utils'
import { ThemeProvider } from '@/theme/ThemeProvider'
import { healthState } from './outbound'
import { OutboundHealthSection, SentEventsTab } from './OutboundWebhooks'

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  useGetOutboundWebhookSummary: vi.fn(),
  useListOutboundDeliveries: vi.fn(),
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

const summary = (patch: Partial<OutboundWebhookSummary>): OutboundWebhookSummary => ({
  connected: true, subscriptionId: '01938a4c-0000-7000-8000-000000000001', url: 'https://conecta.test/avisos', status: 'Active', healthy: true, failingSince: null,
  deadLettered: 0, lastSuccessAt: '2026-11-27T13:00:00Z', secretRotatedAt: null, previousValidUntil: null, events: [], canEdit: true, ...patch,
})

const mockSummary = (data: OutboundWebhookSummary) =>
  vi.mocked(api.useGetOutboundWebhookSummary).mockReturnValue({ data, isPending: false, isError: false, isSuccess: true, refetch: vi.fn() } as never)

const list = (rows: OutboundDeliveryList['page']['items'], deadLettered = 0): OutboundDeliveryList => ({
  page: { items: rows, page: 1, pageSize: 20, total: rows.length }, types: [{ type: 'billing_slip.paid', label: 'Boleto pago', subscribed: false }], deadLettered,
})

const mockList = (data: OutboundDeliveryList) =>
  vi.mocked(api.useListOutboundDeliveries).mockReturnValue({ data, isPending: false, isError: false, isSuccess: true, refetch: vi.fn() } as never)

describe('resumo de saúde dos avisos (INT-02 CA 19)', () => {
  it('regras do chip', () => {
    expect(healthState({ connected: false, healthy: true })).toBeNull()
    expect(healthState({ connected: true, healthy: true })).toEqual({ tone: 'ok', text: 'Os avisos estão chegando ao Conecta' })
    expect(healthState({ connected: true, healthy: false, failingSince: '2026-11-25T15:00:00Z' })).toEqual({ tone: 'crit', text: 'O Conecta não está recebendo os avisos desde 25/11' })
  })

  it('avisos chegando: chip ok e o link para o Avançado', async () => {
    mockSummary(summary({}))
    renderPage(() => <OutboundHealthSection />)
    expect(await screen.findByText('Os avisos estão chegando ao Conecta')).toBeTruthy()
    expect(screen.getByText('Funcionando')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Avançado' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Reenviar os parados' })).toBeNull()
  })

  it('avisos parados: chip crítico com a data e "Reenviar os parados"', async () => {
    mockSummary(summary({ healthy: false, failingSince: '2026-11-25T15:00:00Z', deadLettered: 2 }))
    renderPage(() => <OutboundHealthSection />)
    expect(await screen.findByText('O Conecta não está recebendo os avisos desde 25/11')).toBeTruthy()
    expect(screen.getByText('Parado')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Reenviar os parados' })).toBeTruthy()
  })
})

describe('aba "Enviados" (INT-02)', () => {
  it('vazia: a mensagem do estado vazio e o "Enviar teste" no cabeçalho', async () => {
    mockSummary(summary({}))
    mockList(list([]))
    renderPage(() => <SentEventsTab />)
    expect(await screen.findByText('Nenhum aviso enviado ainda.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Enviar teste' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Trocar segredo' })).toBeTruthy()
  })

  it('sem conexão: leva ao Conecta', async () => {
    mockSummary(summary({ connected: false, subscriptionId: null }))
    mockList(list([]))
    renderPage(() => <SentEventsTab />)
    expect(await screen.findByText('Conecte o Conecta para enviar avisos')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Abrir o Conecta' })).toBeTruthy()
  })

  it('com parado: chip crítico com texto, "Reenviar" e o conteúdo apagado desabilita', async () => {
    mockSummary(summary({ healthy: false, deadLettered: 1 }))
    mockList(list([
      { id: 'd1', eventId: 'e1', type: 'billing_slip.paid', typeLabel: 'Boleto pago', createdAt: '2026-11-27T13:00:00Z', status: 'DeadLettered', attempts: 8, lastStatusCode: 500,
        lastError: 'Resposta HTTP 500', nextAttemptAt: null, canResend: true, contentPurged: false },
      { id: 'd2', eventId: 'e2', type: 'billing_slip.issued', typeLabel: 'Boleto emitido', createdAt: '2026-08-01T13:00:00Z', status: 'Delivered', attempts: 1, lastStatusCode: 200,
        lastError: null, nextAttemptAt: null, canResend: false, contentPurged: true },
    ], 1))
    renderPage(() => <SentEventsTab />)
    expect((await screen.findAllByText('Parado')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Entregue').length).toBeGreaterThan(0)
    const resend = screen.getAllByRole('button', { name: 'Reenviar' })
    expect(resend.some((b) => !(b as HTMLButtonElement).disabled)).toBe(true)
    expect(resend.some((b) => (b as HTMLButtonElement).disabled)).toBe(true)
  })
})
