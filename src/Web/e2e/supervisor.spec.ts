import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const SUP = '/api/v1/dashboard/supervisor'
const now = () => new Date().toISOString()
const supervisorActions = { actions: ['view', 'create', 'edit', 'approve', 'approve_quality'] }

const overview = (over: Record<string, unknown> = {}) => ({
  today: '2026-10-26', lines: ['Cabines', 'Portas'], blocks: ['quality', 'shortages', 'board', 'capacity', 'indicators'], canDecideQuality: true, canCreatePurchaseRequest: true,
  canViewPurchasing: true, ...over,
})

const board = (late: boolean) => ({
  openCount: 3, lateCount: late ? 1 : 0, atRiskCount: 1, updatedAt: now(),
  columns: [
    { key: 'C', code: 'C', name: 'Corte', count: 2, remainingHours: 5.5, lateCount: 0, atRiskCount: 1 },
    { key: 'Q', code: 'Q', name: 'Qualidade', count: late ? 1 : 0, remainingHours: 0.5, lateCount: late ? 1 : 0, atRiskCount: 0 },
    { key: 'pronta', code: null, name: 'Pronta para concluir', count: late ? 0 : 1, remainingHours: 0, lateCount: 0, atRiskCount: 0 },
  ],
  cards: [
    ...(late ? [{ orderId: 'of1', number: 'OS-2026-00041', productName: 'Cabina e portas', projectId: 'p1', projectNumber: 'PRJ-2026-0003', partyName: 'Condomínio Monte Castelo', columnKey: 'Q',
      stepCode: 'Q', stepName: 'Qualidade', codes: ['C', 'Q'], stepNames: ['Corte', 'Qualidade'], currentStepIndex: 1, promisedDate: '2026-10-23', risk: 'Late', delayDays: 3 }] : []),
    { orderId: 'of2', number: 'OS-2026-00042', productName: 'Elevador 6 paradas', projectId: 'p2', projectNumber: 'PRJ-2026-0004', partyName: 'Hospital São Lucas', columnKey: 'C',
      stepCode: 'C', stepName: 'Corte', codes: ['C', 'Q'], stepNames: ['Corte', 'Qualidade'], currentStepIndex: 0, promisedDate: '2026-11-05', risk: 'AtRisk', delayDays: null },
  ],
})

const cell = (week: string, percent: number | null, band: string) => ({ weekStart: week, committedHours: 30, capacityHours: 40, percent, band })
const weeks = ['2026-10-26', '2026-11-02', '2026-11-09', '2026-11-16', '2026-11-23']
const capacity = () => ({
  today: '2026-10-26', planComputedAt: now(), updatedAt: now(),
  weeks: weeks.map((start, i) => ({ start, end: start, isoWeek: 44 + i })),
  centers: [
    { workCenterId: 'w1', name: 'Corte', line: 'Cabines', busyUntil: '2026-11-19', cells: weeks.map((w, i) => cell(w, i === 0 ? 97.5 : 40, i === 0 ? 'Full' : 'Idle')) },
    { workCenterId: 'w2', name: 'Montagem', line: 'Cabines', busyUntil: null, cells: weeks.map((w) => cell(w, 75, 'Normal')) },
  ],
  factory: { workCenterId: null, name: 'Fábrica', line: null, busyUntil: '2026-11-19', cells: weeks.map((w) => cell(w, 70, 'Normal')) },
})

const qualityBlock = (decided: boolean, canDecide = true) => ({
  canDecide, pendingCount: decided ? 0 : 1, updatedAt: now(),
  steps: decided || !canDecide ? [] : [{ stepId: 'q1', orderId: 'of1', orderNumber: 'OS-2026-00041', stepCode: 'Q', stepName: 'Qualidade', productName: 'Cabina e portas', projectNumber: 'PRJ-2026-0003',
    promisedDate: '2026-10-23', attempt: 1, waitingSince: '2026-10-25T10:00:00Z', selfInspection: true, openNonConformities: 1 }],
  openNonConformities: 2,
  nonConformities: [
    { id: 'nc1', number: 'NC-2026-0007', orderId: 'of1', orderNumber: 'OS-2026-00041', stepCode: 'Q', stepName: 'Qualidade', responsibleName: 'Paula Lima', status: 'Open', ageDays: 5 },
    { id: 'nc2', number: 'NC-2026-0009', orderId: 'of3', orderNumber: 'OS-2026-00039', stepCode: 'Q', stepName: 'Qualidade', responsibleName: 'Rui Supervisor', status: 'InAction', ageDays: 1 },
  ],
})

const shortages = () => ({
  total: 3, blocking: 2, canCreateRequest: true, updatedAt: now(),
  items: [
    { orderId: 'of1', orderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0003', itemId: 'i1', itemCode: 'MP-0101', itemName: 'Chapa de aço inox', unit: 'UN', shortQty: 6,
      neededOn: '2026-10-26', blocks: true, blocksInDays: 0, purchase: 'None', purchaseLabel: 'Sem solicitação', purchaseRequestId: null, purchaseOrderId: null },
    { orderId: 'of2', orderNumber: 'OS-2026-00042', projectId: 'p2', projectNumber: 'PRJ-2026-0004', itemId: 'i2', itemCode: 'MP-0202', itemName: 'Cabo de aço 8 mm', unit: 'M', shortQty: 2.5,
      neededOn: '2026-10-29', blocks: true, blocksInDays: 3, purchase: 'Requested', purchaseLabel: 'Solicitada SC-2026-0012', purchaseRequestId: 'sc1', purchaseOrderId: null },
    { orderId: 'of3', orderNumber: 'OS-2026-00039', projectId: 'p3', projectNumber: 'PRJ-2026-0001', itemId: 'i3', itemCode: 'MP-0303', itemName: 'Motor 7,5 cv', unit: 'UN', shortQty: 1,
      neededOn: '2026-12-04', blocks: false, blocksInDays: null, purchase: 'Ordered', purchaseLabel: 'Pedido PC-2026-0005 previsto para 02/11', purchaseRequestId: 'sc2', purchaseOrderId: 'pc5' },
  ],
})

const indicators = () => ({
  from: '2026-09-27', to: '2026-10-26', stoppedHours: 2.2, updatedAt: now(),
  stoppages: [
    { reason: 'Material', label: 'Material', hours: 1.5, pauses: 2, orders: [{ orderId: 'of2', orderNumber: 'OS-2026-00042', hours: 1.5 }] },
    { reason: 'Machine', label: 'Máquina', hours: 0.5, pauses: 1, orders: [{ orderId: 'of2', orderNumber: 'OS-2026-00042', hours: 0.5 }] },
    { reason: 'Other', label: 'Outro', hours: 0.2, pauses: 1, orders: [{ orderId: 'of1', orderNumber: 'OS-2026-00041', hours: 0.2 }] },
  ],
  qualityRates: [{ stepCode: 'Q', stepName: 'Qualidade', decisions: 4, rejections: 1, ratePercent: 25 }],
})

const stepDetail = (results: (string | null)[]) => ({
  step: { stepId: 'q1', orderId: 'of1', orderNumber: 'OS-2026-00041', stepCode: 'Q', stepName: 'Qualidade', stepIndex: 1, codes: ['C', 'Q'], stepNames: ['Corte', 'Qualidade'], status: 'Ready',
    productName: 'Cabina e portas', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo', partyName: 'Condomínio Monte Castelo', promisedDate: '2026-10-23', attempt: 1,
    waitingSince: '2026-10-25T10:00:00Z', canDecide: true, selfInspection: false, openNonConformities: 0 },
  productCode: 'CAB-01', quantity: 1,
  checklist: ['Painel riscado', 'Nivelamento da porta'].map((text, i) => ({ id: `c${i}`, text, result: results[i], markedAt: null, markedByName: results[i] ? 'Ana Souza' : null })),
  returnOptions: [{ stepId: 'c', code: 'C', name: 'Corte', suggested: true, waiting: 0, impact: 'Volta para Corte.' }],
  responsibles: [{ id: 'u1', name: 'Ana Souza' }], defaultResponsibleId: 'u1', nextStepName: null, approvalImpact: 'A OS fica pronta para concluir.', blockedReason: null,
  inspectionStarted: false, canStartInspection: false, version: 7, serverNow: now(),
})

interface Options { actions?: string[]; overview?: Record<string, unknown>; refresh?: number; capacityError?: boolean }

async function supervisorApi(page: Page, options: Options = {}) {
  const state = { decided: false, results: [null, null] as (string | null)[], lines: [] as (string | null)[], refreshes: 0 }
  const lineOf = (r: Route) => new URL(r.request().url()).searchParams.get('line')
  const mark = (i: number) => (r: Route) => { state.results[i] = (r.request().postDataJSON() as { result: string | null }).result; return json(r, stepDetail(state.results)) }
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ production: { actions: options.actions ?? supervisorActions.actions } }) }),
    [`GET ${SUP}`]: (r: Route) => json(r, overview(options.overview)),
    [`GET ${SUP}/board`]: (r: Route) => { state.lines.push(lineOf(r)); return json(r, board(!state.decided)) },
    [`GET ${SUP}/capacity`]: (r: Route) => (options.capacityError ? json(r, { title: 'Erro' }, 500) : json(r, capacity())),
    [`GET ${SUP}/quality`]: (r: Route) => json(r, qualityBlock(state.decided, (options.overview?.canDecideQuality as boolean | undefined) ?? true)),
    [`GET ${SUP}/shortages`]: (r: Route) => json(r, shortages()),
    [`GET ${SUP}/indicators`]: (r: Route) => json(r, indicators()),
    [`POST ${SUP}/refresh`]: (r: Route) => {
      state.refreshes++
      return state.refreshes > (options.refresh ?? 1)
        ? json(r, { status: 429, code: 'dashboard_refresh_rate_limited', title: 'Aguarde alguns segundos para atualizar de novo.', detail: 'Aguarde alguns segundos para atualizar de novo.' }, 429)
        : r.fulfill({ status: 204 })
    },
    'GET /api/v1/production/quality/steps/q1': (r: Route) => json(r, stepDetail(state.results)),
    'PUT /api/v1/production/quality/steps/q1/checklist/c0': mark(0),
    'PUT /api/v1/production/quality/steps/q1/checklist/c1': mark(1),
    'POST /api/v1/production/quality/steps/q1/approve': (r: Route) => {
      state.decided = true
      return json(r, { orderId: 'of1', orderNumber: 'OS-2026-00041', result: 'Approved', attempt: 1, selfInspection: false, nextStepName: null, nextStepId: null, readyToComplete: true,
        canCompleteOrder: true, nonConformityId: null, nonConformityNumber: null, returnToStepName: null, message: 'Qualidade aprovada' })
    },
  })
  return state
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

const block = (page: Page, name: string) => page.getByRole('region', { name, exact: true })

test.describe('Painel do supervisor (DSH-03)', () => {
  test('abre os blocos, decide a qualidade pelo bloco e o Quadro muda', async ({ page }) => {
    const state = await supervisorApi(page)
    await page.goto('/app/producao/supervisor')
    await expect(page.getByRole('heading', { name: 'Produção: Painel do supervisor' })).toBeVisible()
    for (const name of ['Quadro', 'Qualidade', 'Faltas de material', 'Capacidade', 'Indicadores']) {
      await expect(block(page, name)).toBeVisible()
      await expect(block(page, name).getByText(/Atualizado às \d\d:\d\d/)).toBeVisible()
      await expect(block(page, name).getByRole('button', { name: 'Abrir' })).toBeVisible()
    }

    // Quadro: chips que abrem o Kanban filtrado e só as atrasadas e em risco.
    const quadro = block(page, 'Quadro')
    await expect(quadro.getByRole('link', { name: '1 atrasada' })).toHaveAttribute('href', /\/app\/producao\/kanban\?atrasadas=(1|%221%22)/)
    await expect(quadro.getByRole('link', { name: '1 em risco' })).toHaveAttribute('href', /risco=(1|%221%22)/)
    await expect(quadro.getByText('Atrasada 3 d')).toBeVisible()
    await expect(quadro.getByText('Em risco, prometido 05/11/2026')).toBeVisible()

    // Qualidade: "Aprovar" abre a inspeção da PRD-05; a decisão recarrega a fila e o quadro.
    const qualidade = block(page, 'Qualidade')
    await expect(qualidade.getByText('Autoinspeção', { exact: true })).toBeVisible()
    await expect(qualidade.getByRole('link', { name: 'NC-2026-0007' })).toHaveAttribute('href', '/app/producao/nao-conformidades/nc1')
    await qualidade.getByRole('button', { name: 'Aprovar', exact: true }).click()
    const panel = page.getByRole('dialog', { name: 'Aprovar qualidade · OS-2026-00041' })
    await expect(panel).toBeVisible()
    for (const name of ['Painel riscado', 'Nivelamento da porta']) await panel.getByRole('group', { name: `Resultado: ${name}` }).getByRole('button', { name: 'Conforme', exact: true }).click()
    await panel.getByRole('button', { name: 'Aprovar qualidade' }).click()
    const confirm = page.getByRole('alertdialog', { name: 'Aprovar qualidade · OS-2026-00041' })
    await confirm.getByRole('button', { name: 'Aprovar qualidade' }).click()
    await expect(page.getByText('Qualidade aprovada').first()).toBeVisible()
    expect(state.decided).toBe(true)
    await panel.getByRole('button', { name: 'Fechar' }).click()
    await expect(quadro.getByRole('link', { name: '0 atrasadas' })).toBeVisible()
    await expect(quadro.getByText('Atrasada 3 d')).toHaveCount(0)
    await expect(qualidade.getByText('Nenhuma etapa aguardando a qualidade.')).toBeVisible()
  })

  test('faltas que bloqueiam primeiro, situação da compra e gerar solicitação; indicadores com dica', async ({ page }) => {
    await supervisorApi(page)
    await page.goto('/app/producao/supervisor')
    const faltas = block(page, 'Faltas de material')
    await expect(faltas.getByText('2 bloqueiam nos próximos 5 dias úteis')).toBeVisible()
    const rows = faltas.getByRole('listitem')
    await expect(rows.nth(0)).toContainText('Bloqueia hoje')
    await expect(rows.nth(1)).toContainText('Bloqueia em 3 dias')
    await expect(rows.nth(1)).toContainText('Solicitada SC-2026-0012')
    await expect(rows.nth(2)).toContainText('Pedido PC-2026-0005 previsto para 02/11')
    await expect(rows.nth(0).getByRole('link', { name: 'Gerar solicitação' })).toHaveAttribute('href', '/app/compras/solicitacoes/nova?os=of1&item=i1')
    await expect(faltas.getByRole('link', { name: 'Gerar solicitação' })).toHaveCount(1)

    const indicadores = block(page, 'Indicadores')
    await expect(indicadores.getByText('2,2 h')).toBeVisible()
    await expect(indicadores.getByText('25,0%')).toBeVisible()
    await expect(indicadores.getByRole('button', { name: /Taxa de NC/ })).toBeVisible()
    await indicadores.locator('summary', { hasText: 'Material' }).click()
    await expect(indicadores.getByRole('link', { name: 'OS-2026-00042' }).first()).toHaveAttribute('href', /\/app\/producao\/os\/of2\?aba=apontamentos/)

    const capacidade = block(page, 'Capacidade')
    await expect(capacidade.getByText('Lotado').first()).toBeVisible()
    await expect(capacidade.getByText('ocupado até 19/11').first()).toBeVisible()
  })

  test('filtro de linha na URL vale para os blocos; Atualizar com limite; erro por bloco', async ({ page }) => {
    const state = await supervisorApi(page, { capacityError: true })
    await page.goto('/app/producao/supervisor')
    await expect(block(page, 'Capacidade').getByText('Não foi possível carregar capacidade.')).toBeVisible()
    await expect(block(page, 'Capacidade').getByRole('button', { name: 'Tentar de novo' })).toBeVisible()
    await expect(block(page, 'Quadro').getByText('Atrasada 3 d')).toBeVisible()

    await page.getByLabel('Linha').selectOption('Cabines')
    await expect(page).toHaveURL(/linha=Cabines/)
    await expect.poll(() => state.lines.includes('Cabines')).toBe(true)
    await expect(block(page, 'Quadro').getByRole('link', { name: '1 atrasada' })).toHaveAttribute('href', /linha=Cabines/)

    await page.getByRole('button', { name: 'Atualizar' }).click()
    await expect(page.getByRole('button', { name: 'Atualizar' })).toBeEnabled()
    await page.getByRole('button', { name: 'Atualizar' }).click()
    await expect(page.getByText('Aguarde alguns segundos para atualizar de novo.')).toBeVisible()
  })

  test('sem approve_quality a fila vira contagem; sem Estoque o bloco de faltas não aparece', async ({ page }) => {
    await supervisorApi(page, { actions: ['view'], overview: { blocks: ['quality', 'board', 'capacity', 'indicators'], canDecideQuality: false, canCreatePurchaseRequest: false } })
    await page.goto('/app/producao/supervisor')
    const qualidade = block(page, 'Qualidade')
    await expect(qualidade.getByText('1 etapa aguarda a qualidade')).toBeVisible()
    await expect(qualidade.getByRole('button', { name: 'Aprovar', exact: true })).toHaveCount(0)
    await expect(qualidade.getByRole('link', { name: 'NC-2026-0009' })).toBeVisible()
    await expect(block(page, 'Faltas de material')).toHaveCount(0)
    // Quem só vê a produção encontra o painel no fim das abas.
    await expect(page.getByRole('navigation', { name: 'Produção' }).getByRole('link').last()).toHaveText('Painel do supervisor')
  })

  test('a 400 px os blocos empilham na ordem Qualidade, Faltas, Quadro, Capacidade, Indicadores sem rolagem horizontal', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular', 'só no celular')
    await supervisorApi(page)
    await page.goto('/app/producao/supervisor')
    await expect(block(page, 'Indicadores')).toBeVisible()
    const tops: number[] = []
    for (const name of ['Qualidade', 'Faltas de material', 'Quadro', 'Capacidade', 'Indicadores']) tops.push((await block(page, name).boundingBox())!.y)
    expect([...tops].sort((a, b) => a - b)).toEqual(tops)
    await expect(block(page, 'Quadro').getByRole('list', { name: 'Colunas do quadro' })).toBeHidden()
    await expect(block(page, 'Quadro').getByText('Atrasada 3 d')).toBeVisible()
    await expectNoHorizontalScroll(page)
    const heights = await page.locator('main [data-block] button:visible:not([aria-label^="Dica"]), main [data-block] a[class*="min-h"]:visible').evaluateAll((els) => els.map((e) => Math.round(e.getBoundingClientRect().height)))
    expect(heights.length).toBeGreaterThan(0)
    for (const h of heights) expect(h).toBeGreaterThanOrEqual(44)
  })
})
