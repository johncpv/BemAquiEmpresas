import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { FabricationOrderDetail, FabricationStepView } from '@/api/generated/model'
import { renderWithRouter } from '@/test-utils'
import { FabricationSteps } from './FabricationSteps'
import { RECORDED_NOTICE, STEP_IDLE, stepChip } from './fabrication'
import { LaborEntriesTab } from './LaborEntriesTab'

const mocks = vi.hoisted(() => ({ perms: new Set<string>(), record: vi.fn() }))

vi.mock('@/pages/use-can', () => ({ useCan: (perm: string) => mocks.perms.has(perm) }))
vi.mock('./labor-cost', async (original) => ({ ...(await original<typeof import('./labor-cost')>()), useOrderLaborCosts: () => ({ canSee: false, query: {} }) }))
vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  useListWorkCenters: () => ({ data: undefined, isPending: false, isError: false }),
  useListOrderLaborEntries: () => ({
    data: { orderId: 'of1', orderNumber: 'OS-2026-00007', entries: [], totalHours: 0, awaitingApprovalStepId: null, awaitingApprovalStepName: null, version: 1 },
    isPending: false, isError: false, refetch: vi.fn(),
  }),
  useListLaborEmployees: () => ({ data: [{ id: 'e1', name: 'Ana Souza', registration: null, positionName: 'Montadora' }], isPending: false }),
  recordLaborEntry: mocks.record,
}))

const step = (over: Partial<FabricationStepView> = {}): FabricationStepView => ({
  id: 's1', sequence: 1, code: 'C', name: 'Corte', workCenterId: 'wc1', workCenterName: 'Corte', line: 'Linha 1', standardHours: 2, status: 'Paused',
  isQualityStep: false, requiresApproval: false, checklist: [], appliedHours: 1, pausedReason: null, ...over,
})

const order = (over: Partial<FabricationOrderDetail> = {}): FabricationOrderDetail => ({
  id: 'of1', number: 'OS-2026-00007', status: 'Paused', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Aurora', partyId: 'c1', partyName: 'Condomínio Aurora',
  promisedDate: null, projectItemId: 'pi1', itemDescription: 'Elevador', productId: 'i1', productCode: 'EL-6', productName: 'Elevador 6 paradas', quantity: 1, priority: 0,
  routingTemplateId: 'rt1', routingTemplateName: 'Padrão', steps: [step(), step({ id: 's2', sequence: 2, code: 'M', name: 'Montagem', status: 'Waiting', appliedHours: 0 })],
  totalStandardHours: 4, currentStepIndex: 0, started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-11-01T10:00:00Z', version: 5, ...over,
})

function render(ui: () => ReactNode) {
  const client = new QueryClient()
  return renderWithRouter(() => <QueryClientProvider client={client}>{ui()}</QueryClientProvider>)
}

beforeEach(() => {
  mocks.perms.clear()
  mocks.record.mockReset()
})
afterEach(cleanup)

describe('Chip da etapa', () => {
  it('pausa aberta mostra o motivo; iniciada sem ninguém explica; em andamento segue igual', () => {
    expect(stepChip({ status: 'Paused', pausedReason: 'Falta de material' })).toEqual({ label: 'Pausada: Falta de material', tone: 'warn', idle: false })
    expect(stepChip({ status: 'Paused', pausedReason: null })).toEqual({ label: STEP_IDLE, tone: 'warn', idle: true })
    expect(STEP_IDLE).toBe('Iniciada · ninguém trabalhando agora')
    expect(stepChip({ status: 'InProgress' })).toEqual({ label: 'Em andamento', tone: 'info', idle: false })
  })

  it('na aba Etapas, a etapa sem ninguém ganha a dica de como seguir', async () => {
    render(() => <FabricationSteps order={order()} onSaved={async () => undefined} />)
    expect(await screen.findByText(STEP_IDLE)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Dica: Etapa iniciada' })).toBeTruthy()
    cleanup()

    render(() => <FabricationSteps order={order({ steps: [step({ pausedReason: 'Máquina parada' })] })} onSaved={async () => undefined} />)
    expect(await screen.findByText('Pausada: Máquina parada')).toBeTruthy()
    expect(screen.queryByText(STEP_IDLE)).toBeNull()
  })
})

describe('"Concluir etapa" na aba Etapas', () => {
  it('aparece na etapa atual iniciada com production.approve e abre o diálogo com motivo', async () => {
    mocks.perms.add('production.approve')
    render(() => <FabricationSteps order={order()} onSaved={async () => undefined} />)
    const button = await screen.findByRole('button', { name: 'Concluir etapa C Corte da OS-2026-00007' })
    expect(screen.getAllByRole('button', { name: /^Concluir etapa/ })).toHaveLength(1)
    fireEvent.click(button)
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText('Concluir etapa C Corte da OS-2026-00007?')).toBeTruthy()
    expect(within(dialog).getByText('Informe o motivo.')).toBeTruthy()
  })

  it('não aparece sem production.approve', async () => {
    render(() => <FabricationSteps order={order()} onSaved={async () => undefined} />)
    await screen.findByText(STEP_IDLE)
    expect(screen.queryByRole('button', { name: /^Concluir etapa/ })).toBeNull()
  })
})

describe('"Apontar por funcionário" avisa que a etapa continua iniciada', () => {
  async function recordOne() {
    mocks.record.mockResolvedValue({})
    render(() => <LaborEntriesTab order={order()} onChanged={async () => undefined} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Apontar por funcionário' }))
    const dialog = await screen.findByRole('alertdialog')
    fireEvent.change(within(dialog).getByLabelText('Funcionário'), { target: { value: 'e1' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apontar por funcionário' }))
    await waitFor(() => expect(mocks.record).toHaveBeenCalledOnce())
    return await screen.findByText(RECORDED_NOTICE)
  }

  it('com production.approve, oferece "Concluir a etapa agora", que abre o mesmo diálogo, sem concluir sozinho', async () => {
    mocks.perms.add('production.edit').add('shopfloor.create').add('production.approve')
    await recordOne()
    expect(RECORDED_NOTICE).toBe('Horas registradas. A etapa fica iniciada até alguém finalizar ou o supervisor concluir.')
    fireEvent.click(screen.getByRole('button', { name: 'Concluir a etapa agora' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText('Concluir etapa C Corte da OS-2026-00007?')).toBeTruthy()
    expect(within(dialog).getByRole('button', { name: 'Concluir etapa' }).hasAttribute('disabled')).toBe(true)
  })

  it('sem production.approve, só o aviso', async () => {
    mocks.perms.add('production.edit').add('shopfloor.create')
    await recordOne()
    expect(screen.queryByRole('button', { name: 'Concluir a etapa agora' })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Concluir etapa/ })).toBeNull()
  })
})
