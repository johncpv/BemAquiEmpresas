import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/api/generated/bemaqui'
import type { ProjectDetail, ProjectStagesView, ProjectStageView } from '@/api/generated/model'
import type { NavModule } from '@/navigation/modules'
import { NavigationContext, type NavigationValue } from '@/navigation/navigation-context'
import { renderWithRouter } from '@/test-utils'
import { ProjectStagesTab } from './ProjectStagesTab'
import { ServiceProjectView } from './ServiceProjectView'
import { nextStep } from './service'
import { completionPermission, currentStageText, doneText, plannedText, stageAction, stageStepText } from './stages'

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  useGetProjectStages: vi.fn(),
  applyProjectStageTemplate: vi.fn(),
  markProjectStageCheck: vi.fn(),
  startProjectStage: vi.fn(),
  completeProjectStage: vi.fn(),
  approveProjectStage: vi.fn(),
  useGetProjectMilestonePreview: vi.fn(() => ({ isPending: true })),
  useGetContract: vi.fn(() => ({ isPending: true })),
  useGetProjectFinancial: vi.fn(() => ({ isPending: true })),
  useGetProjectCosts: vi.fn(() => ({ isPending: true })),
}))

const stage = (over: Partial<ProjectStageView> = {}): ProjectStageView => ({
  id: 's1', sequence: 1, code: 'K', name: 'Kickoff', role: 'Execution', status: 'Ready', plannedHours: 6, positionId: null, positionName: null, requiresApproval: false,
  responsibleUserId: null, responsibleName: null, plannedStart: null, plannedEnd: null, startedAt: null, completedOn: null, checklistDone: 0, checklistTotal: 0, checklist: [], ...over,
})

const view = (over: Partial<ProjectStagesView> = {}): ProjectStagesView => ({
  projectId: 'p1', stages: [], templateId: null, templateName: null, templateRevision: null, started: false, currentStage: null, canApplyTemplate: true,
  suggestedTemplateId: 't2', templates: [{ id: 't1', name: 'Consultoria', segment: null, isDefault: false }, { id: 't2', name: 'Implantação', segment: null, isDefault: true }], version: 7, ...over,
})

const project = (over: Partial<ProjectDetail> = {}): ProjectDetail => ({
  id: 'p1', number: 'PRJ-2026-0007', name: 'Implantação', status: 'InProgress', partyId: 'c1', partyName: 'Cliente', contractId: 'k1', contractNumber: 'CTR-2026-0007',
  siteAddressId: null, siteAddress: null, promisedDate: null, forecastDate: null, priority: null, managerUserId: null, managerName: null, createdAt: '2026-11-01T00:00:00Z',
  items: [], version: 3, contractStatus: 'Active', executionKind: 'Service', serviceExecution: true, acceptanceRequired: true, startedOn: '2026-11-20',
  stage: { letters: ['P', 'I', 'E', 'A', 'C'], index: 1, label: 'Em execução', acceptanceWaived: false }, ...over,
})

const module = (code: string, actions: string[]) =>
  ({ code, name: code, area: 'Comercial', released: true, enabled: true, actions, slug: code, aliases: [], path: '/', icon: () => null, tabs: [], locked: false }) as unknown as NavModule

function renderIn(ui: ReactNode, actions = ['view', 'edit']) {
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

const mockStages = (result: object) => vi.mocked(api.useGetProjectStages).mockReturnValue(result as never)

describe('aba Etapas do projeto de serviço (EXE-03a CA 7)', () => {
  afterEach(() => vi.clearAllMocks())

  it('carregando e erro com "Tentar de novo"', async () => {
    mockStages({ isPending: true })
    const { unmount } = renderIn(<ProjectStagesTab projectId="p1" />)
    await waitFor(() => expect(document.querySelector('.animate-pulse, [aria-busy="true"]')).not.toBeNull())
    unmount()
    const refetch = vi.fn()
    mockStages({ isPending: false, isError: true, refetch })
    renderIn(<ProjectStagesTab projectId="p1" />)
    expect(await screen.findByText('Não foi possível carregar as etapas.')).not.toBeNull()
  })

  it('vazio oferece "Usar um modelo de etapas" com o sugerido marcado e copia', async () => {
    mockStages({ isPending: false, isError: false, data: view() })
    vi.mocked(api.applyProjectStageTemplate).mockResolvedValue(view() as never)
    renderIn(<ProjectStagesTab projectId="p1" />)
    expect(await screen.findByText('Este projeto ainda não tem etapas.')).not.toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Usar um modelo de etapas' }))
    const dialog = await screen.findByRole('alertdialog')
    expect((within(dialog).getByRole('radio', { name: /Implantação/ }) as HTMLInputElement).checked).toBe(true)
    expect(within(dialog).getByText('Sugerido')).not.toBeNull()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Usar o modelo' }))
    await waitFor(() => expect(api.applyProjectStageTemplate).toHaveBeenCalledWith('p1', { templateId: 't2', version: 7 }))
  })

  it('sem projects.edit, vazio sem a ação', async () => {
    mockStages({ isPending: false, isError: false, data: view() })
    renderIn(<ProjectStagesTab projectId="p1" />, ['view'])
    expect(await screen.findByText('Este projeto ainda não tem etapas.')).not.toBeNull()
    expect(screen.queryByRole('button', { name: 'Usar um modelo de etapas' })).toBeNull()
  })

  it('lista letra, nome, papel, situação, responsável, previsto, realizado e checklist', async () => {
    mockStages({
      isPending: false, isError: false, data: view({
        templateId: 't2', templateName: 'Implantação', templateRevision: 3, started: true, canApplyTemplate: false,
        stages: [
          stage({ status: 'Done', completedOn: '2026-11-21', responsibleName: 'Ana', checklistDone: 4, checklistTotal: 6 }),
          stage({ id: 's2', sequence: 2, code: 'T', name: 'Treinamento', status: 'Waiting', plannedStart: '2026-12-02', plannedEnd: '2026-12-05' }),
        ],
      }),
    })
    renderIn(<ProjectStagesTab projectId="p1" />)
    expect(await screen.findByText('Modelo Implantação (revisão 3)')).not.toBeNull()
    const table = screen.getByRole('table')
    expect(within(table).getByText('Treinamento')).not.toBeNull()
    expect(within(table).getByText('4/6')).not.toBeNull()
    expect(within(table).getByText('Concluída')).not.toBeNull()
    expect(within(table).getByText('Ana')).not.toBeNull()
    expect(within(table).getByText('02/12/2026 a 05/12/2026')).not.toBeNull()
    expect(within(table).getByText('Concluída em 21/11/2026')).not.toBeNull()
    expect(screen.queryByRole('button', { name: 'Trocar o modelo' })).toBeNull()
  })

  it('textos de apoio', () => {
    expect(currentStageText({ code: 'T', name: 'Treinamento', sequence: 2, total: 5 })).toBe('Etapa atual: T Treinamento')
    expect(currentStageText(null)).toBeNull()
    expect(plannedText(null, null)).toBe('—')
    expect(plannedText(null, '2026-12-05')).toBe('até 05/12/2026')
    expect(doneText(null, '2026-11-20T13:00:00Z')).toBe('Iniciada em 20/11/2026')
  })
})

describe('cabeçalho do projeto de serviço com a etapa atual (EXE-03a CA 8)', () => {
  afterEach(() => vi.clearAllMocks())

  it('mostra "Etapa atual" ao lado do indicador, que continua; sem etapas, não mostra', async () => {
    const { unmount } = renderIn(<ServiceProjectView project={project({ currentStage: { code: 'T', name: 'Treinamento', sequence: 2, total: 5 } })} refresh={async () => undefined} />)
    expect((await screen.findByTestId('etapa-atual')).textContent).toContain('Etapa atual: T Treinamento')
    expect(screen.getByRole('img', { name: /Etapa Em execução/ })).not.toBeNull()
    expect(screen.getByRole('tab', { name: 'Etapas' })).not.toBeNull()
    unmount()
    renderIn(<ServiceProjectView project={project()} refresh={async () => undefined} />)
    await screen.findByTestId('proximo-passo')
    expect(screen.queryByTestId('etapa-atual')).toBeNull()
  })
})

describe('andamento da etapa (EXE-03b)', () => {
  afterEach(() => vi.clearAllMocks())

  const checklist = [
    { id: 'c1', sequence: 1, text: 'Lista de presença', result: null },
    { id: 'c2', sequence: 2, text: 'Material', result: 'NotApplicable' as const, markedByName: 'Ana' },
  ]

  it('painel marca o checklist e "Concluir etapa" pede o checklist completo', async () => {
    mockStages({ isPending: false, isError: false, data: view({ started: true, canApplyTemplate: false, stages: [stage({ status: 'InProgress', checklist, checklistDone: 1, checklistTotal: 2 })] }) })
    vi.mocked(api.markProjectStageCheck).mockResolvedValue(view() as never)
    renderIn(<ProjectStagesTab projectId="p1" />)
    const panel = await screen.findByTestId('painel-etapa')
    expect(within(panel).getByText('Não se aplica · Ana')).not.toBeNull()
    fireEvent.click(within(panel).getAllByRole('button', { name: 'Feito' })[0])
    await waitFor(() => expect(api.markProjectStageCheck).toHaveBeenCalledWith('p1', 's1', 1, { result: 'Done', version: 7 }, expect.anything()))
    fireEvent.click(within(panel).getAllByRole('button', { name: 'Concluir etapa' })[0])
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText(/Marque todos os itens do checklist/)).not.toBeNull()
  })

  it('Entrega sem projects.approve mostra quem conclui; aguardando aprovação com o selo e "Aprovar etapa"', async () => {
    mockStages({ isPending: false, isError: false, data: view({ started: true, canApplyTemplate: false, stages: [stage({ code: 'G', name: 'Go-live', role: 'Delivery', status: 'InProgress' })] }) })
    const { unmount } = renderIn(<ProjectStagesTab projectId="p1" />)
    const panel = await screen.findByTestId('painel-etapa')
    expect(within(panel).queryByRole('button', { name: 'Concluir etapa' })).toBeNull()
    expect(within(panel).getByText(/quem conclui são usuários com permissão de aprovar projetos/)).not.toBeNull()
    unmount()
    mockStages({
      isPending: false, isError: false, data: view({
        started: true, canApplyTemplate: false,
        stages: [stage({ status: 'Done', requiresApproval: true, selfApproved: true }), stage({ id: 's2', sequence: 2, code: 'T', name: 'Treinamento', status: 'AwaitingApproval', requiresApproval: true })],
      }),
    })
    renderIn(<ProjectStagesTab projectId="p1" />, ['view', 'edit', 'approve'])
    expect((await screen.findAllByText('Autoaprovada')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Exige aprovação').length).toBeGreaterThan(0)
    expect(within(screen.getByTestId('painel-etapa')).getAllByRole('button', { name: 'Aprovar etapa' }).length).toBeGreaterThan(0)
  })

  it('próximo passo do cabeçalho: com etapas, sem os botões soltos e com o verbo da etapa atual', async () => {
    const all = () => true
    const withStages = project({ status: 'Planned', currentStage: { code: 'K', name: 'Kickoff', sequence: 1, total: 4 } })
    expect(nextStep(withStages, all).primary).toBeNull()
    expect(nextStep(withStages, all).more).not.toContain('delivery')
    expect(nextStep(project({ status: 'Planned' }), all).primary).toBe('start')
    expect(nextStep(project({ status: 'InProgress', currentStage: { code: 'T', name: 'Treinamento', sequence: 2, total: 4 } }), all).primary).toBeNull()

    mockStages({ isPending: false, isError: false, data: view({ stages: [stage({ status: 'Done' }), stage({ id: 's2', sequence: 2, code: 'T', name: 'Treinamento', status: 'InProgress' })] }) })
    renderIn(<ServiceProjectView project={project({ currentStage: { code: 'T', name: 'Treinamento', sequence: 2, total: 4 } })} refresh={async () => undefined} />, ['view', 'edit', 'approve', 'register_acceptance'])
    expect((await screen.findAllByRole('button', { name: 'Concluir Treinamento' })).length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: 'Registrar entrega' })).toBeNull()
    expect(screen.getByTestId('proximo-passo').textContent).toContain('Próximo passo: Concluir Treinamento.')
  })

  it('regras do painel', () => {
    expect(completionPermission({ role: 'Execution', hasMilestone: true })).toBe('projects.approve')
    expect(completionPermission({ role: 'Acceptance', hasMilestone: false })).toBe('projects.register_acceptance')
    const only = (p: string) => (perm: string) => perm === p
    expect(stageAction({ status: 'InProgress', role: 'Execution', hasMilestone: false, checklistDone: 0, checklistTotal: 0 }, only('projects.edit'))).toBe('complete')
    expect(stageAction({ status: 'InProgress', role: 'Execution', hasMilestone: true, checklistDone: 0, checklistTotal: 0 }, only('projects.edit'))).toBeNull()
    expect(stageAction({ status: 'Ready', role: 'Delivery', hasMilestone: false, checklistDone: 0, checklistTotal: 0 }, only('projects.edit'))).toBe('start')
    expect(stageStepText({ status: 'AwaitingApproval', name: 'Treinamento' })).toBe('Aprovar Treinamento')
    expect(stageStepText({ status: 'Done', name: 'Treinamento' })).toBeNull()
  })
})
