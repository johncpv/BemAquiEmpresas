import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, screen, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/api/generated/bemaqui'
import type { NavModule } from '@/navigation/modules'
import { NavigationContext, type NavigationValue } from '@/navigation/navigation-context'
import { renderWithRouter } from '@/test-utils'
import { StageTemplateEditor } from './StageTemplateEditor'
import { StageTemplatesPage } from './StageTemplatesPage'
import { draftProblems, emptyDraft, milestoneSum, milestoneText, newStage, type TemplateDraft, toDraft, toInput } from './stage-templates'

vi.mock('@/api/generated/bemaqui', async (original) => ({
  ...(await original<typeof import('@/api/generated/bemaqui')>()),
  useListStageTemplates: vi.fn(),
  seedSuggestedStageTemplates: vi.fn(),
}))

const draft = (over: Partial<TemplateDraft> = {}): TemplateDraft => ({ ...emptyDraft(), name: 'Implantação', ...over })
const stage = (code: string, role: 'Execution' | 'Delivery' | 'Acceptance') => ({ ...newStage('Etapa ' + code, role), code })

describe('regras do modelo de etapas (EXE-02 CA 3 e 5)', () => {
  it('o modelo inicial passa: Execução, Entrega, Aceite e 50/50', () => {
    expect(draftProblems(draft())).toEqual({})
    expect(milestoneSum(draft().milestones)).toBe(100)
  })

  it('sem Execução, Aceite sem Entrega antes e Execução depois da Entrega caem na linha certa', () => {
    expect(draftProblems(draft({ steps: [stage('E', 'Delivery')], milestones: [] })).steps).toBe('Inclua ao menos uma etapa de Execução.')
    expect(draftProblems(draft({ steps: [stage('X', 'Execution'), stage('A', 'Acceptance')], milestones: [] }))['steps[1].role']).toBe('O Aceite do cliente vem depois de uma etapa de Entrega.')
    expect(draftProblems(draft({ steps: [stage('X', 'Execution'), stage('E', 'Delivery'), stage('Y', 'Execution')], milestones: [] }))['steps[2].role'])
      .toBe('Depois da Entrega só pode vir o Aceite do cliente.')
    expect(draftProblems(draft({ steps: [stage('X', 'Execution'), stage('E', 'Delivery'), stage('F', 'Delivery')], milestones: [] }))['steps[2].role'])
      .toBe('O modelo tem no máximo uma etapa de Entrega.')
  })

  it('marcos: soma 100% e o gatilho por etapa só aponta para Execução', () => {
    const steps = [stage('X', 'Execution'), stage('E', 'Delivery')]
    expect(draftProblems(draft({ steps, milestones: [{ percent: '50.00', trigger: 'Signature', stageCode: '', days: '0' }] })).suggestedMilestones)
      .toBe('A soma dos marcos sugeridos tem de dar 100%.')
    expect(draftProblems(draft({ steps, milestones: [{ percent: '100.00', trigger: 'StageCompleted', stageCode: 'E', days: '0' }] }))['suggestedMilestones[0].stageCode']).toBeDefined()
    expect(draftProblems(draft({ steps, milestones: [{ percent: '100.00', trigger: 'TechnicalAcceptance', stageCode: '', days: '0' }] }))['suggestedMilestones[0].trigger']).toBeDefined()
    expect(draftProblems(draft({ steps, milestones: [{ percent: '100.00', trigger: 'StageCompleted', stageCode: 'X', days: '0' }] }))).toEqual({})
  })

  it('o corpo leva a letra só no gatilho por etapa e o texto do marco é legível', () => {
    const input = toInput(draft({ milestones: [{ percent: '50.00', trigger: 'Signature', stageCode: 'X', days: '0' }, { percent: '50.00', trigger: 'StageCompleted', stageCode: 'X', days: '5' }] }))
    expect(input.suggestedMilestones.map((m) => m.stageCode)).toEqual([null, 'X'])
    expect(input.segment).toBeNull()
    expect(milestoneText({ percent: 50, trigger: 'StageCompleted', stageCode: 'X', daysAfterEvent: 5 })).toBe('50% ao concluir a etapa X, +5 dias')
  })
})

const module = (code: string, actions: string[]) =>
  ({ code, name: code, area: 'Comercial', released: true, enabled: true, actions, slug: code, aliases: [], path: '/', icon: () => null, tabs: [], locked: false }) as unknown as NavModule

function renderUi(ui: ReactNode, actions = ['view', 'edit', 'delete']) {
  const navigation: NavigationValue = { areas: [], all: [module('projects', actions)], tenantName: 'Empresa', status: 'ready', isOwner: true, retry: () => undefined, vocabulary: {}, areaLabels: {} }
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return renderWithRouter(() => (
    <QueryClientProvider client={client}>
      <NavigationContext.Provider value={navigation}>{ui}</NavigationContext.Provider>
    </QueryClientProvider>
  ))
}

describe('editor do modelo (EXE-02 CA 2, 4 e celular)', () => {
  it('"Mover para baixo" troca a ordem das etapas e a prévia acompanha', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    renderUi(<StageTemplateEditor initial={draft()} positions={[]} segments={[]} canEdit onSubmit={onSubmit} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Mover etapa 1 para baixo' }))
    expect((screen.getByLabelText('Letra da etapa 1') as HTMLInputElement).value).toBe('E')
    expect((screen.getByLabelText('Letra da etapa 2') as HTMLInputElement).value).toBe('X')
    // Entrega antes da Execução: o salvar mostra o erro na linha sem chamar o servidor.
    fireEvent.click(screen.getByRole('button', { name: 'Salvar modelo' }))
    expect(await screen.findByText('Depois da Entrega só pode vir o Aceite do cliente.')).toBeTruthy()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('cargo gravado e inativado aparece com o selo e o aviso', async () => {
    const initial = toDraft({
      name: 'Obra', segment: null, suggestedMilestones: [],
      steps: [{ id: 's1', name: 'Execução', code: 'X', role: 'Execution', standardHoursPerUnit: 40, positionId: 'p1', positionName: 'Eletricista', positionActive: false, requiresApproval: false, checklist: [] }],
    })
    renderUi(<StageTemplateEditor initial={initial} positions={[]} segments={[]} canEdit onSubmit={vi.fn()} />)
    expect(await screen.findByText('Cargo inativo')).toBeTruthy()
    expect(screen.getByText(/Escolha outro cargo para os próximos projetos/)).toBeTruthy()
  })

  it('modelo usado em projetos avisa que as mudanças valem só para os próximos', async () => {
    renderUi(<StageTemplateEditor initial={draft()} positions={[]} segments={[]} canEdit usageCount={3} onSubmit={vi.fn()} />)
    expect(await screen.findByText('Usado em 3 projetos. Mudanças valem só para os próximos.')).toBeTruthy()
  })
})

describe('lista de modelos (EXE-02 CA 1 e 11)', () => {
  afterEach(() => vi.clearAllMocks())

  it('vazio oferece "Criar modelo" e "Usar os sugeridos"', async () => {
    vi.mocked(api.useListStageTemplates).mockReturnValue({ data: { items: [], page: 1, pageSize: 20, total: 0 }, isPending: false, isError: false, isSuccess: true, refetch: vi.fn() } as never)
    vi.mocked(api.seedSuggestedStageTemplates).mockResolvedValue({ created: 4 })
    renderUi(<StageTemplatesPage />)
    const empty = await screen.findByText('Nenhum modelo de etapas ainda.')
    const box = empty.closest('div')?.parentElement ?? document.body
    expect(within(box).getByRole('link', { name: 'Criar modelo' })).toBeTruthy()
    fireEvent.click(within(box).getByRole('button', { name: 'Usar os sugeridos' }))
    expect(api.seedSuggestedStageTemplates).toHaveBeenCalledTimes(1)
  })
})
