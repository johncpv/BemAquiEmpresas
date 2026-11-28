import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/api/generated/bemaqui'
import type { FinanceLimits } from '@/api/generated/model'
import type { NavModule } from '@/navigation/modules'
import { NavigationContext, type NavigationValue } from '@/navigation/navigation-context'
import { renderWithRouter } from '@/test-utils'
import { FinanceLimitsSection } from './FinanceLimitsSection'

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  useGetFinanceLimits: vi.fn(),
  updateFinanceLimits: vi.fn(),
}))

afterEach(() => {
  cleanup()
  vi.mocked(api.updateFinanceLimits).mockReset()
})

const module = (code: string, actions: string[]) =>
  ({ code, name: code, area: 'Sistema', released: true, enabled: true, actions, slug: code, aliases: [], path: '/', icon: () => null, tabs: [], locked: false }) as unknown as NavModule

const nav = (isOwner: boolean): NavigationValue => ({
  areas: [], all: [module('finance', ['view']), module('settings', ['view', 'edit'])], tenantName: 'Empresa A', status: 'ready', isOwner,
  retry: () => undefined, vocabulary: {}, areaLabels: {},
})

const limits: FinanceLimits = {
  discountApprovalPct: 5, discountApprovalAmount: 500, cancelApprovalAmount: 1000, backdateDays: 7, ownerCanApproveOwn: false, paymentTolerance: 0,
  transferNotifyAmount: 10000, adjustedDefaultThresholdPct: 20, ofxBankEntryCap: 500, selfApprovalMode: 'Never',
}

function renderSection(isOwner: boolean) {
  vi.mocked(api.useGetFinanceLimits).mockReturnValue({ data: limits, isPending: false, isError: false, isSuccess: true, refetch: vi.fn() } as never)
  vi.mocked(api.updateFinanceLimits).mockResolvedValue(limits)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return renderWithRouter(() => (
    <QueryClientProvider client={client}>
      <NavigationContext.Provider value={nav(isOwner)}><FinanceLimitsSection canEdit /></NavigationContext.Provider>
    </QueryClientProvider>
  ))
}

describe('Quem solicitou a ordem pode aprová-la? (FIN-10a CA 3, 13 e 14)', () => {
  it('mostra as três opções com a atual marcada; o Proprietário troca e o código é pedido', async () => {
    renderSection(true)
    expect(await screen.findByText('Quem solicitou a ordem pode aprová-la?')).toBeTruthy()
    const never = screen.getByRole('radio', { name: /Nunca/ }) as HTMLInputElement
    expect(never.checked).toBe(true)
    expect(screen.getByRole('radio', { name: /Qualquer aprovador \(só no 1º nível\)/ })).toBeTruthy()
    fireEvent.click(screen.getByRole('radio', { name: /Só o Proprietário/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Salvar limites' }))
    expect(await screen.findByText('Confirme com o código do autenticador')).toBeTruthy()
    expect(api.updateFinanceLimits).not.toHaveBeenCalled()
  })

  it('salvar sem trocar o modo não pede código nem manda o modo', async () => {
    renderSection(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Salvar limites' }))
    await vi.waitFor(() => expect(api.updateFinanceLimits).toHaveBeenCalled())
    const body = vi.mocked(api.updateFinanceLimits).mock.calls[0][0]
    expect(body.selfApprovalMode).toBeUndefined()
    expect(body.code).toBeUndefined()
    expect(screen.queryByText('Confirme com o código do autenticador')).toBeNull()
  })

  it('quem não é do Proprietário vê o modo em leitura', async () => {
    renderSection(false)
    expect(await screen.findByText('Só o Proprietário muda esta regra.')).toBeTruthy()
    expect(screen.getByRole('radio', { name: /Só o Proprietário/ }).closest('fieldset')?.disabled).toBe(true)
  })
})
