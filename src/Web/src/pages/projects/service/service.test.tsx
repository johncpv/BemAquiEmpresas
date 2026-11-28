import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/api/generated/bemaqui'
import type { MilestonePreview, ProjectDetail } from '@/api/generated/model'
import { StageIndicator } from '@/components/ui/stage-indicator'
import type { NavModule } from '@/navigation/modules'
import { NavigationContext, type NavigationValue } from '@/navigation/navigation-context'
import { renderWithRouter } from '@/test-utils'
import { awaits } from '../../finance/receivables/receivables'
import { PROJECT_LETTERS, PROJECT_STAGES, projectStages, SERVICE_LETTERS, SERVICE_STAGES, stageView } from '../../contracts/contracts'
import { eventLabel, eventsFor } from '../../quotes/milestones'
import { ProjectStageIndicator } from '../ProjectStageIndicator'
import { awaitingAcceptance } from '../projects'
import { dateError, nextStep, parcelLine, releasesText } from './service'
import { ParcelEffect, ServiceStepDialog } from './ServiceStepDialog'

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  useGetProjectMilestonePreview: vi.fn(),
  registerProjectDelivery: vi.fn(),
  revertProjectAcceptance: vi.fn(),
}))

const project = (over: Partial<ProjectDetail> = {}): ProjectDetail => ({
  id: 'p1', number: 'PRJ-2026-0007', name: 'Implantação', status: 'Planned', partyId: 'c1', partyName: 'Cliente', contractId: 'k1', contractNumber: 'CTR-2026-0007',
  siteAddressId: null, siteAddress: null, promisedDate: null, forecastDate: null, priority: null, managerUserId: null, managerName: null, createdAt: '2026-11-01T00:00:00Z',
  items: [], version: 3, contractStatus: 'Active', executionKind: 'Service', serviceExecution: true, acceptanceRequired: true,
  stage: { letters: ['P', 'I', 'E', 'A', 'C'], index: 0, label: 'Planejado', acceptanceWaived: false }, ...over,
})

const preview = (over: Partial<MilestonePreview> = {}): MilestonePreview => ({
  step: 'delivery', lines: [], total: 0, stillWaiting: [], amountsVisible: true, lockedReason: null, hasAcceptanceMilestone: true, ...over,
})

const all = () => true
const none = () => false

describe('próximo passo do projeto de serviço (EXE-01b CA 1 a 3)', () => {
  it('um botão por situação, com a linha de contexto', () => {
    expect(nextStep(project(), all)).toMatchObject({ primary: 'start', context: 'Projeto planejado. Quando começar o trabalho, inicie a execução.', more: ['delivery'] })
    expect(nextStep(project({ status: 'InProgress', startedOn: '2026-11-20' }), all)).toMatchObject({ primary: 'delivery', context: 'Em execução desde 20/11/2026.' })
    const delivered = project({ status: 'Delivered', deliveredOn: '2026-11-25' })
    expect(nextStep(delivered, all)).toMatchObject({ primary: 'acceptance', context: 'Entregue em 25/11/2026. Falta o aceite do cliente.', awaitingAcceptance: true })
    const withParcel = preview({ lines: [{ sequence: 3, description: 'Aceite', triggerEvent: 'TechnicalAcceptance', label: 'Aceite do cliente', compatibility: false, amount: 4000, dueDate: null }] })
    expect(nextStep(delivered, all, { acceptancePreview: withParcel }).context).toBe('Entregue em 25/11/2026. Falta o aceite do cliente; libera a parcela Aceite (R$ 4.000,00).')
    expect(nextStep(project({ status: 'Delivered', deliveredOn: '2026-11-25', acceptanceRequired: false }), all).awaitingAcceptance).toBe(false)
    const accepted = nextStep(project({ status: 'Delivered', acceptedOn: '2026-11-26' }), all)
    expect(accepted).toMatchObject({ primary: null, context: 'Aceito em 26/11/2026. O projeto encerra quando a última parcela for recebida.', more: ['revert'] })
    expect(nextStep(project({ status: 'Closed' }), all, { closedOn: '2026-11-27' })).toMatchObject({ primary: null, context: 'Encerrado em 27/11/2026.' })
  })

  it('sem a permissão, sem botão e com quem pode (CA 2)', () => {
    expect(nextStep(project(), none)).toMatchObject({ primary: null, more: [], context: 'Projeto planejado. Quando começar o trabalho, inicie a execução. Quem inicia: usuários com permissão de editar projetos.' })
    expect(nextStep(project({ status: 'InProgress', startedOn: '2026-11-20' }), none).context).toContain('Quem registra a entrega: usuários com permissão de aprovar projetos.')
    expect(nextStep(project({ status: 'Delivered', deliveredOn: '2026-11-25' }), none).context).toContain('Quem registra o aceite: usuários com a permissão Registrar aceite do cliente.')
  })

  it('contrato não ativo não oferece passo (CA 10)', () => {
    const step = nextStep(project({ contractStatus: 'Cancelled' }), all)
    expect(step.primary).toBeNull()
    expect(step.context).toContain('O contrato precisa estar ativo para registrar este passo.')
  })

  it('mensagens de data (CA 10)', () => {
    const p = { startedOn: '2026-11-20', deliveredOn: '2026-11-25' }
    expect(dateError('2026-11-29', '2026-11-28', 'start', p)).toBe('A data não pode ser depois de hoje.')
    expect(dateError('2026-11-19', '2026-11-28', 'delivery', p)).toBe('A data não pode ser antes do início (20/11/2026).')
    expect(dateError('2026-11-24', '2026-11-28', 'acceptance', p)).toBe('O aceite não pode ser antes da entrega (25/11/2026).')
    expect(dateError('2026-11-28', '2026-11-28', 'acceptance', p)).toBeNull()
  })
})

describe('indicador P · I · E · A · C (EXE-01b CA 5)', () => {
  it('letras e rótulos espelham a ProjectStageMap do back', () => {
    expect(projectStages('Service')).toEqual({ stages: SERVICE_STAGES, letters: SERVICE_LETTERS })
    expect([...SERVICE_LETTERS]).toEqual(['P', 'I', 'E', 'A', 'C'])
    expect([...SERVICE_STAGES]).toEqual(['Planejado', 'Em execução', 'Entregue', 'Aceito', 'Encerrado'])
    expect(projectStages('Fabrication')).toEqual({ stages: PROJECT_STAGES, letters: PROJECT_LETTERS })
  })

  it('cada letra acende na sua situação e o "A" dispensado vira traço', () => {
    const cases: [string, number, string][] = [['Planned', 0, 'P'], ['InProgress', 1, 'I'], ['Delivered', 2, 'E'], ['Closed', 4, 'C']]
    for (const [status, index, letter] of cases) {
      const s = stageView({ status, executionKind: 'Service', stage: { letters: [...SERVICE_LETTERS], index, label: SERVICE_STAGES[index], acceptanceWaived: true } })
      expect(s.letters[s.index]).toBe(letter)
      expect(s.waived).toBe(3)
    }
    expect(stageView({ status: 'Delivered', executionKind: 'Service', stage: { letters: [...SERVICE_LETTERS], index: 3, label: 'Aceito', acceptanceWaived: false } }).label).toBe('Aceito')

    render(<ProjectStageIndicator project={{ status: 'Closed', executionKind: 'Service', stage: { letters: [...SERVICE_LETTERS], index: 4, label: 'Encerrado', acceptanceWaived: true } }} showLabel />)
    const img = screen.getByRole('img')
    expect(img.getAttribute('aria-label')).toBe('Etapa Encerrado, 5 de 5. Aceite dispensado')
    expect(img.querySelector('[data-state="waived"]')).not.toBeNull()
  })

  it('fabricação: o mesmo indicador de antes (regressão Konebras)', () => {
    const { container: now } = render(<ProjectStageIndicator project={{ status: 'InProgress', executionKind: 'Fabrication' }} />)
    const { container: before } = render(<StageIndicator stages={PROJECT_STAGES} letters={PROJECT_LETTERS} current={1} />)
    expect(now.innerHTML).toBe(before.innerHTML)
    expect(screen.getAllByRole('img')[0].getAttribute('aria-label')).toBe('Etapa Em produção, 2 de 6')
  })

  it('faixa âmbar só no entregue com aceite exigido (CA 12)', () => {
    expect(awaitingAcceptance({ executionKind: 'Service', status: 'Delivered', stage: { letter: 'E', acceptanceWaived: false } })).toBe(true)
    expect(awaitingAcceptance({ executionKind: 'Service', status: 'Delivered', stage: { letter: 'E', acceptanceWaived: true } })).toBe(false)
    expect(awaitingAcceptance({ executionKind: 'Service', status: 'Delivered', stage: { letter: 'A', acceptanceWaived: false } })).toBe(false)
    expect(awaitingAcceptance({ executionKind: 'Fabrication', status: 'Delivered', stage: { letter: 'T' } })).toBe(false)
  })
})

describe('efeito nas parcelas (EXE-01b CA 7)', () => {
  const line = (sequence: number, description: string, amount: number | null, compatibility = false) => ({
    sequence, description, triggerEvent: (compatibility ? 'FabricationDone' : 'Delivered') as never, label: compatibility ? 'Conclusão da execução (antes: Fabricação concluída)' : 'Entrega',
    compatibility, amount, dueDate: amount === null ? null : '2026-12-03',
  })

  it('sem parcela, uma, várias e a de compatibilidade', () => {
    const { rerender } = render(<ParcelEffect step="start" preview={preview({ step: 'start' })} />)
    expect(screen.getByText('Nenhuma parcela é liberada por este passo.')).toBeTruthy()
    rerender(<ParcelEffect step="delivery" preview={preview({ lines: [line(2, 'Entrega', 3000)], total: 3000, stillWaiting: ['Aceite'] })} />)
    expect(screen.getByText('Ao confirmar, estas parcelas passam a valer para cobrança:')).toBeTruthy()
    expect(screen.getByText(/^Parcela 2, Entrega: R\$\s3\.000,00, vence 03\/12\/2026$/)).toBeTruthy()
    expect(screen.getByText('A parcela Aceite continua aguardando o aceite do cliente.')).toBeTruthy()
    rerender(<ParcelEffect step="delivery" preview={preview({ lines: [line(2, 'Entrega', 3000), line(3, 'Fabricação', 2000, true)], total: 5000 })} />)
    expect(within(screen.getByRole('list', { name: 'Parcelas liberadas' })).getAllByRole('listitem')).toHaveLength(2)
    expect(screen.getByText(/Parcela 3, Conclusão da execução \(antes: Fabricação concluída\)/)).toBeTruthy()
    expect(screen.getByText(/Total liberado/).textContent).toBe('Total liberado: R$ 5.000,00')
  })

  it('sem finance.view, só o marco; desfazer devolve a parcela', () => {
    expect(parcelLine(line(2, 'Entrega', null))).toBe('Parcela 2, Entrega')
    expect(releasesText(preview({ lines: [line(2, 'Entrega', null), line(3, 'Aceite', null)] }))).toBe('as parcelas Entrega e Aceite')
    render(<ParcelEffect step="revert" preview={preview({ step: 'revert', lines: [line(3, 'Aceite', 4000)] })} />)
    expect(screen.getByText('A parcela Aceite volta a aguardar o aceite.')).toBeTruthy()
  })
})

describe('rótulos de marco por tipo (EXE-01b CA 15)', () => {
  it('serviço troca o aceite e esconde a fabricação; fábrica igual', () => {
    expect(eventLabel('TechnicalAcceptance', 'Service')).toBe('Aceite do cliente')
    expect(eventLabel('FabricationDone', 'Service')).toBe('Conclusão da execução (antes: Fabricação concluída)')
    expect(eventLabel('TechnicalAcceptance', 'Fabrication')).toBe('Aceite técnico')
    expect(eventsFor('Service').map((e) => e.value)).not.toContain('FabricationDone')
    expect(eventsFor('Service', 'FabricationDone').map((e) => e.value)).toContain('FabricationDone')
    expect(eventsFor('Fabrication').map((e) => e.label)).toEqual(['Assinatura do contrato', 'Fabricação concluída', 'Entrega', 'Aceite técnico'])
    expect(awaits({ status: 'Planned', triggerEvent: 'TechnicalAcceptance', projectExecutionKind: 'Service' })).toBe('Aguarda: aceite do cliente')
    expect(awaits({ status: 'Planned', triggerEvent: 'TechnicalAcceptance' })).toBe('Aguarda: aceite técnico')
  })
})

const module = (code: string, actions: string[]) =>
  ({ code, name: code, area: 'Comercial', released: true, enabled: true, actions, slug: code, aliases: [], path: '/', icon: () => null, tabs: [], locked: false }) as unknown as NavModule

function renderDialog(ui: ReactNode, actions = ['view', 'edit', 'approve', 'register_acceptance']) {
  const navigation: NavigationValue = {
    areas: [], all: [module('projects', actions)], tenantName: 'Empresa', status: 'ready', isOwner: true, retry: () => undefined, vocabulary: {}, areaLabels: {},
  }
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return renderWithRouter(() => (
    <QueryClientProvider client={client}>
      <NavigationContext.Provider value={navigation}>{ui}</NavigationContext.Provider>
    </QueryClientProvider>
  ))
}

const mockPreview = (data: MilestonePreview) => vi.mocked(api.useGetProjectMilestonePreview).mockReturnValue({ data, isPending: false, isError: false } as never)

describe('diálogos do projeto de serviço (EXE-01b CA 8 a 10)', () => {
  afterEach(() => vi.clearAllMocks())

  it('"também aceitou" só sem marco de aceite, sempre desmarcada, e pede quem aceitou', async () => {
    mockPreview(preview({ hasAcceptanceMilestone: false }))
    vi.mocked(api.registerProjectDelivery).mockResolvedValue(project({ status: 'Delivered' }))
    const done = vi.fn().mockResolvedValue(undefined)
    renderDialog(<ServiceStepDialog project={project({ status: 'InProgress' })} step="delivery" open onOpenChange={() => undefined} onDone={done} onReload={async () => undefined} />)
    const box = await screen.findByRole('checkbox', { name: /Também registrar que o cliente aceitou/ })
    expect((box as HTMLInputElement).checked).toBe(false)
    fireEvent.click(box)
    expect(screen.getByLabelText(/Quem aceitou/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Registrar entrega' }))
    expect(await screen.findByText('Informe quem aceitou.')).toBeTruthy()
    expect(api.registerProjectDelivery).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText(/Quem aceitou/), { target: { value: 'Joana Prado' } })
    fireEvent.click(screen.getByRole('button', { name: 'Registrar entrega' }))
    await waitFor(() => expect(api.registerProjectDelivery).toHaveBeenCalled())
    expect(vi.mocked(api.registerProjectDelivery).mock.calls[0][1]).toMatchObject({ alsoAccept: true, acceptedByName: 'Joana Prado', expectedStatus: 'InProgress', version: 3 })
  })

  it('com marco de aceite, a caixa não aparece', async () => {
    mockPreview(preview({ hasAcceptanceMilestone: true }))
    renderDialog(<ServiceStepDialog project={project({ status: 'InProgress' })} step="delivery" open onOpenChange={() => undefined} onDone={async () => undefined} onReload={async () => undefined} />)
    await screen.findByRole('alertdialog')
    expect(screen.queryByRole('checkbox')).toBeNull()
  })

  it('desfazer travado: botão desabilitado, o motivo e "Fazer aditivo" (CA 9)', async () => {
    mockPreview(preview({ step: 'revert', lockedReason: 'A parcela já foi recebida ou cobrada. Para corrigir, use um aditivo.' }))
    const accepted = project({
      status: 'Delivered', acceptedOn: '2026-11-26',
      acceptance: { id: 'a1', version: 1, kind: 'Manual', occurredOn: '2026-11-26', recordedAt: '2026-11-26T12:00:00Z', recordedBy: null, recordedByName: null, acceptedByName: 'Joana Prado', acceptedByRole: null, attachmentId: null, backdateReason: null },
    })
    renderDialog(<ServiceStepDialog project={accepted} step="revert" open onOpenChange={() => undefined} onDone={async () => undefined} onReload={async () => undefined} />)
    expect(await screen.findByText('Desfazer o aceite de Joana Prado, registrado em 26/11/2026?')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Desfazer aceite' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('A parcela já foi recebida ou cobrada. Para corrigir, use um aditivo.')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Fazer aditivo' })).toBeTruthy()
    expect(screen.getByText('0/200')).toBeTruthy()
  })

  it('data anterior a hoje pede o motivo (CA 10)', async () => {
    mockPreview(preview({ step: 'start' }))
    renderDialog(<ServiceStepDialog project={project()} step="start" open onOpenChange={() => undefined} onDone={async () => undefined} onReload={async () => undefined} />)
    await screen.findByRole('alertdialog')
    expect(screen.queryByLabelText(/Por que a data é anterior/)).toBeNull()
    fireEvent.change(screen.getByLabelText(/Data do início/), { target: { value: '01/11/2026' } })
    expect(await screen.findByLabelText(/Por que a data é anterior/)).toBeTruthy()
  })
})
