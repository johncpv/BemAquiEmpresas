import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const RS = String.fromCharCode(0x1e)

const step = (id: string, code: string, name: string, over: Record<string, unknown> = {}) => ({
  stepId: id, sequence: 1, name, code, workCenterId: 'cut', workCenterName: 'Corte', state: 'Planned', standardHours: 6, remainingHours: 6, progress: 0,
  plannedStart: '2026-11-03', plannedEnd: '2026-11-04', actualStart: null, actualEnd: null, noForecastReason: null, drivesDelivery: false, ...over,
})

/** Dois projetos da fila: o primeiro com folga (uma etapa concluída e uma em andamento) e o segundo em risco, com a instalação digitada. */
function projectsView(atRiskOnly = false) {
  const p1 = {
    position: 1, projectId: 'p1', projectNumber: 'PRJ-2026-0001', projectName: 'Ed. Aurora', partyId: 'c1', partyName: 'Condomínio Aurora', start: '2026-10-28', forecastDate: '2026-11-10',
    promisedDate: '2026-11-13', slackDays: 3, risk: 'None', daysLate: null, noForecastReason: null,
    orders: [{
      orderId: 'o1', number: 'OS-2026-0001', kind: 'Fabrication', status: 'InProgress', start: '2026-10-28', end: '2026-11-05', steps: [
        step('s1', 'P', 'Projeto', { state: 'Done', actualStart: '2026-10-28', actualEnd: '2026-10-29', plannedStart: null, plannedEnd: null, remainingHours: 0, progress: 1 }),
        step('s2', 'C', 'Corte', { state: 'InProgress', remainingHours: 3, progress: 0.5, actualStart: '2026-10-30', plannedStart: '2026-10-30', plannedEnd: '2026-11-03' }),
        step('s3', 'M', 'Montagem', { workCenterId: 'asm', workCenterName: 'Montagem', plannedStart: '2026-11-04', plannedEnd: '2026-11-05' }),
      ],
    }],
  }
  const p2 = {
    position: 2, projectId: 'p2', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo', partyId: 'c2', partyName: 'Construtora Alfa', start: '2026-11-02', forecastDate: '2026-11-27',
    promisedDate: '2026-11-20', slackDays: -7, risk: 'AtRisk', daysLate: 7, noForecastReason: null,
    orders: [
      { orderId: 'o2', number: 'OS-2026-0002', kind: 'Fabrication', status: 'Open', start: '2026-11-02', end: '2026-11-20', steps: [
        step('s4', 'C', 'Corte', { plannedStart: '2026-11-02', plannedEnd: '2026-11-06' }),
        step('s5', 'M', 'Montagem', { workCenterId: 'asm', workCenterName: 'Montagem', plannedStart: '2026-11-09', plannedEnd: '2026-11-20', drivesDelivery: true }),
      ] },
      { orderId: 'i2', number: 'OSI-2026-0004', kind: 'Installation', status: 'Open', start: '2026-11-23', end: '2026-11-27', steps: [] },
    ],
  }
  return {
    today: '2026-10-30', from: '2026-10-23', to: '2027-01-22', planComputedAt: '2026-10-30T17:05:00Z', projects: atRiskOnly ? [p2] : [p1, p2],
    projectOptions: [{ id: 'p1', label: 'PRJ-2026-0001 · Ed. Aurora' }, { id: 'p2', label: 'PRJ-2026-0003 · Ed. Monte Castelo' }],
    partyOptions: [{ id: 'c1', label: 'Condomínio Aurora' }, { id: 'c2', label: 'Construtora Alfa' }], lines: ['Cabines'], centers: [{ id: 'cut', label: 'Corte' }, { id: 'asm', label: 'Montagem' }],
  }
}

const centersView = {
  today: '2026-10-30', from: '2026-10-23', to: '2027-01-22', planComputedAt: '2026-10-30T17:05:00Z', lines: ['Cabines'], activeCenters: 2,
  centers: [
    {
      workCenterId: 'cut', name: 'Corte', line: 'Cabines', lanes: 1,
      bars: [{ stepId: 's2', stepName: 'Corte', code: 'C', orderId: 'o1', orderNumber: 'OS-2026-0001', projectId: 'p1', projectNumber: 'PRJ-2026-0001', projectName: 'Ed. Aurora', state: 'InProgress', standardHours: 6, remainingHours: 3, progress: 0.5, plannedStart: '2026-10-30', plannedEnd: '2026-11-03', actualStart: '2026-10-30', lane: 0 }],
      days: [
        { date: '2026-10-30', allocatedHours: 8, capacityHours: 8, percent: 100, band: 'Full', exceptionType: null, exceptionName: null },
        { date: '2026-10-31', allocatedHours: 0, capacityHours: 0, percent: null, band: 'NoCapacity', exceptionType: null, exceptionName: null },
        { date: '2026-11-02', allocatedHours: 0, capacityHours: 0, percent: null, band: 'NoCapacity', exceptionType: 'Holiday', exceptionName: 'Finados' },
      ],
    },
    {
      workCenterId: 'asm', name: 'Montagem', line: 'Cabines', lanes: 2,
      bars: [
        { stepId: 's3', stepName: 'Montagem', code: 'M', orderId: 'o1', orderNumber: 'OS-2026-0001', projectId: 'p1', projectNumber: 'PRJ-2026-0001', projectName: 'Ed. Aurora', state: 'Planned', standardHours: 20, remainingHours: 20, progress: 0, plannedStart: '2026-11-04', plannedEnd: '2026-11-05', actualStart: null, lane: 0 },
        { stepId: 's5', stepName: 'Montagem', code: 'M', orderId: 'o2', orderNumber: 'OS-2026-0002', projectId: 'p2', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo', state: 'Planned', standardHours: 30, remainingHours: 30, progress: 0, plannedStart: '2026-11-05', plannedEnd: '2026-11-20', actualStart: null, lane: 1 },
      ],
      days: [{ date: '2026-11-02', allocatedHours: 0, capacityHours: 0, percent: null, band: 'NoCapacity', exceptionType: 'Holiday', exceptionName: 'Finados' }],
    },
  ],
}

/** 300 OS com 5 etapas (1.500 barras), uma OS por projeto (CA 13). */
function bigView() {
  const view = projectsView()
  const projects = Array.from({ length: 300 }, (_, i) => {
    const start = `2026-11-${String((i % 25) + 1).padStart(2, '0')}`
    return {
      ...view.projects[0], position: i + 1, projectId: `bp${i}`, projectNumber: `PRJ-2026-${String(i + 1).padStart(4, '0')}`, start, forecastDate: '2026-12-20',
      orders: [{ orderId: `bo${i}`, number: `OS-2026-${String(i + 1).padStart(4, '0')}`, kind: 'Fabrication', status: 'Open', start, end: '2026-12-15',
        steps: Array.from({ length: 5 }, (_, k) => step(`b${i}-${k}`, 'ABCDE'[k], `Etapa ${k + 1}`, { plannedStart: start, plannedEnd: '2026-12-15' })) }],
    }
  })
  return { ...view, projects }
}

async function ganttApi(page: Page, options: { actions?: string[]; calls?: string[]; empty?: boolean; big?: boolean; projectsModule?: boolean } = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ production: { actions: options.actions ?? ALL } as never, projects: { actions: ['view'] } as never }) }),
    'GET /api/v1/production/schedule': (r: Route) => json(r, { lastRun: null, lastSuccess: null, pending: false, openOrders: 3, warnings: [] }),
    'GET /api/v1/production/planning/queue': (r: Route) => json(r, { version: 1, reorderedAt: null, reorderedBy: null, rows: [] }),
    'GET /api/v1/production/planning/gantt/projects': (r: Route) => {
      const url = r.request().url()
      options.calls?.push(url)
      if (options.empty) return json(r, { ...projectsView(), projects: [] })
      if (options.big) return json(r, bigView())
      return json(r, projectsView(url.includes('atRisk=true')))
    },
    'GET /api/v1/production/planning/gantt/centers': (r: Route) => {
      options.calls?.push(r.request().url())
      return json(r, centersView)
    },
  })
}

/** Hub simulado por long polling (como o do Kanban), com o sinal ScheduleChanged empurrado pelo teste. */
async function mockHub(page: Page) {
  const outbox: string[] = []
  let polls = 0
  await page.route('**/hubs/production/negotiate**', (r) => json(r, {
    negotiateVersion: 1, connectionId: 'c1', connectionToken: 't1', availableTransports: [{ transport: 'LongPolling', transferFormats: ['Text', 'Binary'] }],
  }))
  await page.route(/\/hubs\/production\?/, async (route) => {
    const method = route.request().method()
    if (method === 'POST') {
      if ((route.request().postData() ?? '').includes('"protocol"')) outbox.push('{}' + RS)
      return route.fulfill({ status: 200, contentType: 'text/plain', body: '' })
    }

    if (method === 'DELETE') return route.fulfill({ status: 202, body: '' })
    polls++
    if (polls > 1 && outbox.length === 0) await new Promise((resolve) => setTimeout(resolve, 250))
    const body = polls > 1 ? outbox.splice(0).join('') : ''
    return route.fulfill({ status: 200, contentType: 'text/plain', body }).catch(() => undefined)
  })
  return {
    schedule: () => outbox.push(JSON.stringify({ type: 1, target: 'ScheduleChanged', arguments: [] }) + RS),
    board: (id: string) => outbox.push(JSON.stringify({ type: 1, target: 'OrderBoardChanged', arguments: [{ productionOrderId: id }] }) + RS),
  }
}

test.describe('Cronograma (PLN-03)', () => {
  test('por projeto: resumo, prometido, folga, real × previsto, "Define a entrega", instalação e teclado até a OS', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px há teste próprio')
    await ganttApi(page)
    await page.goto('/app/producao/planejamento?aba=cronograma&visao=projeto')
    await expect(page.getByRole('tab', { name: 'Cronograma' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByTestId('gantt-resumo')).toHaveText('2 projetos no período; 1 em risco. O mais atrasado é PRJ-2026-0003 · Ed. Monte Castelo, 7 dias depois do prometido.')
    await expect(page.getByRole('img', { name: 'Cronograma por projeto' })).toBeVisible()
    await expect(page.getByText('Plano de 14:05')).toBeVisible()

    // CA 1 e 3: o chip de folga, o prometido, a faixa do risco e a etapa que define a entrega.
    await expect(page.getByText('+3 dias').first()).toBeVisible()
    await expect(page.getByText('−7 dias').first()).toBeVisible()
    await expect(page.locator('[data-marker="Prometido 20/11"]')).toBeAttached()
    await expect(page.locator('[data-row="p:p2"]')).toHaveClass(/border-l-warn/)
    const drive = page.getByRole('button', { name: /^Etapa M · Montagem, Montagem, OS OS-2026-0002, .*define a entrega$/ })
    await expect(drive).toHaveAttribute('data-drive', '')
    await expect(page.getByText('Define a entrega', { exact: true }).first()).toBeVisible()

    // CA 2 e 4: concluída contínua com as datas reais, em andamento com a parte feita, instalação hachurada só de leitura.
    await expect(page.getByRole('button', { name: /^Etapa P · Projeto, .*realizado de 28\/10\/2026 a 29\/10\/2026, concluída$/ })).toHaveAttribute('data-kind', 'actual')
    await expect(page.getByRole('button', { name: /^Etapa C · Corte, .*em andamento, 50% feito$/ })).toHaveAttribute('data-kind', 'progress')
    await expect(page.getByRole('button', { name: /^Instalação OSI-2026-0004, .*só leitura$/ })).toHaveAttribute('data-kind', 'readonly')

    // CA 8: foco pelo teclado mostra o detalhe; setas andam; Enter abre a OS.
    const first = page.getByRole('button', { name: /^Etapa P · Projeto/ })
    await first.focus()
    await expect(page.getByRole('tooltip')).toContainText('Concluída: realizado de 28/10/2026 a 29/10/2026')
    await page.keyboard.press('ArrowDown')
    await expect(page.getByRole('button', { name: /^Etapa C · Corte, Corte, OS OS-2026-0001/ })).toBeFocused()
    await expect(page.getByRole('tooltip')).toContainText('Horas restantes: 3 de 6 h')
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/app\/producao\/os\/o1$/)
  })

  test('escala, Ir para hoje, filtros na URL, tabela equivalente e a visão por centro', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px há teste próprio')
    const calls: string[] = []
    await ganttApi(page, { calls })
    await page.goto('/app/producao/planejamento?aba=cronograma&visao=projeto')
    await expect(page.getByTestId('gantt-resumo')).toBeVisible()

    // CA 6: a escala muda o eixo e vai para a URL; "Hoje" fica visível.
    await page.getByRole('button', { name: 'Dia', exact: true }).click()
    await expect(page).toHaveURL(/escala=dia/)
    await page.getByRole('button', { name: 'Ir para hoje' }).click()
    const today = page.getByTestId('gantt-scroller').getByText('Hoje', { exact: true })
    await expect(today).toBeInViewport()
    await page.getByRole('button', { name: 'Mês', exact: true }).click()
    await expect(page).toHaveURL(/escala=mes/)
    const fits = await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)
    expect(fits, 'a rolagem para o lado fica dentro do cronograma').toBe(true)

    // CA 9: "Só em risco" e o projeto filtram e ficam na URL.
    await page.getByRole('checkbox', { name: 'Só em risco' }).check()
    await expect(page).toHaveURL(/risco=sim/)
    await expect.poll(() => calls.at(-1)).toContain('atRisk=true')
    await expect(page.getByTestId('gantt-resumo')).toHaveText(/^1 projeto no período; 1 em risco\./)
    await page.getByRole('combobox', { name: /^Projeto/ }).selectOption('p2')
    await expect(page).toHaveURL(/projeto=p2/)
    await expect.poll(() => calls.at(-1)).toContain('projectId=p2')
    await page.reload()
    await expect(page.getByRole('checkbox', { name: 'Só em risco' })).toBeChecked()

    // CA 10: a tabela equivalente, ligada ao SVG e navegável pelas setas (treegrid).
    await page.getByRole('button', { name: 'Ver como tabela' }).click()
    const grid = page.getByRole('treegrid', { name: 'Cronograma em tabela' })
    await expect(grid).toBeVisible()
    const describedBy = await page.getByRole('img', { name: 'Cronograma por projeto' }).getAttribute('aria-describedby')
    await expect(page.locator(`[id="${describedBy}"]`)).toContainText('PRJ-2026-0003 · Ed. Monte Castelo')
    const row = grid.getByRole('row', { name: /PRJ-2026-0003 · Ed. Monte Castelo/ })
    await expect(row).toContainText('20/11/2026')
    await expect(row).toContainText('−7 dias')
    await row.focus()
    await page.keyboard.press('ArrowDown')
    await expect(grid.getByRole('row', { name: /OS-2026-0002/ })).toBeFocused()
    await page.keyboard.press('ArrowRight')
    await expect(grid.getByRole('row', { name: /M · Montagem/ })).toBeVisible()
    await expect(grid.getByRole('row', { name: /M · Montagem/ })).toContainText('Define a entrega')

    // CA 5: por centro, com as sub-linhas, o dia lotado e o feriado com o nome.
    await page.getByRole('button', { name: 'Por centro' }).click()
    await expect(page).toHaveURL(/visao=centro/)
    await expect(page.getByTestId('gantt-resumo')).toHaveText('2 centros; 3 etapas no período. Mais ocupado: Corte, lotado em 1 dia.')
    await expect(page.locator('[data-row="w:asm"]')).toContainText('2 etapas')
    await expect(page.locator('title', { hasText: '02/11: Feriado Finados' }).first()).toBeAttached()
    await expect(page.locator('title', { hasText: '30/10: 8 de 8 h, 100%, Lotado' })).toBeAttached()
    await page.getByRole('button', { name: /^Etapa M · Montagem, Montagem, OS OS-2026-0002/ }).click()
    await expect(page).toHaveURL(/\/app\/producao\/os\/o2$/)
  })

  test('recarrega ao chegar o sinal do hub, agrupado a cada 5 s, sem piscar', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'o mesmo comportamento a 400 px')
    const calls: string[] = []
    await ganttApi(page, { calls })
    const hub = await mockHub(page)
    await page.goto('/app/producao/planejamento?aba=cronograma&visao=projeto')
    await expect(page.getByTestId('gantt-resumo')).toBeVisible()
    await expect.poll(() => calls.length).toBeGreaterThan(0)
    await page.waitForTimeout(1000)
    const before = calls.length
    hub.schedule()
    hub.board('o1')
    hub.schedule()
    await page.waitForTimeout(2000)
    expect(calls.length, 'agrupa: nada antes dos 5 s').toBe(before)
    await expect(page.getByTestId('gantt-resumo')).toBeVisible()
    await expect.poll(() => calls.length, { timeout: 10_000 }).toBe(before + 1)
    await expect(page.getByTestId('gantt-resumo')).toBeVisible()
  })

  test('vazio leva à fila; 300 OS com 5 etapas ficam utilizáveis em menos de 1,5 s', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px há teste próprio')
    await ganttApi(page, { empty: true })
    await page.goto('/app/producao/planejamento?aba=cronograma')
    await expect(page.getByText('Nenhuma etapa planejada no período.')).toBeVisible()
    await expect(page.getByRole('tabpanel', { name: 'Cronograma' }).getByRole('link', { name: 'Ver a fila' })).toBeVisible()

    await page.unrouteAll({ behavior: 'ignoreErrors' })
    await ganttApi(page, { big: true })
    await page.goto('/app/producao/planejamento?aba=fila')
    await expect(page.getByRole('tab', { name: 'Fila' })).toHaveAttribute('aria-selected', 'true')
    const started = Date.now()
    await page.getByRole('tab', { name: 'Cronograma' }).click()
    await expect(page.getByTestId('gantt-resumo')).toHaveText(/^300 projetos no período/)
    const firstBar = page.getByRole('button', { name: /^Projeto PRJ-2026-0001/ })
    await firstBar.focus()
    await expect(firstBar).toBeFocused()
    const elapsed = Date.now() - started
    expect(elapsed, 'CA 13: utilizável em menos de 1,5 s').toBeLessThan(1500)
    expect(await page.locator('[data-bar]').count(), 'linhas virtualizadas e projetos recolhidos').toBeLessThan(120)
    await page.getByRole('button', { name: 'Abrir PRJ-2026-0001 · Ed. Aurora' }).click()
    await expect(page.locator('[data-row="o:bo0"]')).toBeVisible()
  })

  test('a 400 px: lista por projeto, um centro por vez, alvos de 44 px e sem rolagem lateral', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 860 })
    await ganttApi(page)
    await page.goto('/app/producao/planejamento?aba=cronograma&visao=projeto')
    const items = page.getByTestId('gantt-projeto')
    await expect(items).toHaveCount(2)
    await expect(page.getByRole('img', { name: 'Cronograma por projeto' })).toBeHidden()
    await expect(page.getByTestId('gantt-resumo-lista')).toHaveText(/^2 projetos no período; 1 em risco\./)
    expect((await items.nth(1).boundingBox())!.height).toBeGreaterThanOrEqual(44)
    await expect(items.nth(1)).toContainText('Previsto 27/11 · Prometido 20/11')
    await items.nth(1).click()
    const steps = page.getByRole('list', { name: 'Etapas de PRJ-2026-0003 · Ed. Monte Castelo' })
    await expect(steps).toContainText('Previsto 09/11/2026 → 20/11/2026')
    await expect(steps).toContainText('Define a entrega')
    await expect(steps).toContainText('OSI-2026-0004 · Instalação (datas da OS)')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), 'sem rolagem horizontal').toBe(true)

    await page.getByRole('button', { name: 'Por centro' }).click()
    const list = page.getByTestId('gantt-centro-lista')
    await expect(list).toContainText('OS-2026-0001 · C · Corte')
    await expect(list).toContainText('Feriado Finados')
    const next = page.getByRole('button', { name: 'Próximo centro' })
    expect((await next.boundingBox())!.height).toBeGreaterThanOrEqual(44)
    await next.click()
    await expect(list).toContainText('OS-2026-0002 · M · Montagem')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), 'sem rolagem horizontal').toBe(true)
  })
})

test.describe('Cronograma no Projeto 360 (PLN-03 CA 11)', () => {
  const project = {
    id: 'p2', number: 'PRJ-2026-0003', name: 'Ed. Monte Castelo', status: 'InProgress', partyId: 'c2', partyName: 'Construtora Alfa', contractId: 'k1', contractNumber: 'CTR-2026-0001',
    siteAddressId: null, siteAddress: null, promisedDate: '2026-11-20', forecastDate: '2026-11-27', priority: null, managerUserId: 'u1', managerName: 'Ana Souza', createdAt: '2026-09-01T12:00:00Z',
    items: [], version: 1, fabricatedOn: null, deliveredOn: null, deliveredManually: false, hasFabricationMilestone: true, contractStatus: 'Active', isOnboarding: false, fabricationOrders: 1, openFabricationOrders: 1,
  }

  async function projectApi(page: Page, production: boolean) {
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, {
        permissions: [], isOwner: false, version: '1',
        modules: allModules({ projects: { actions: ['view'] } as never, production: production ? { actions: ['view'] } as never : { enabled: false, actions: [] } as never }),
      }),
      'GET /api/v1/projects/p2': (r: Route) => json(r, project),
      'GET /api/v1/contracts/k1': (r: Route) => json(r, { id: 'k1', number: 'CTR-2026-0001', quoteId: 'q1', quoteNumber: 'ORC-2026-0009', totalValue: 100000, milestones: [], status: 'Active' }),
      'GET /api/v1/production/planning/projects/p2/gantt': (r: Route) => {
        const view = projectsView(true)
        return json(r, { ...view, projects: [view.projects[0]], from: '2026-10-26', to: '2026-12-04' })
      },
    })
  }

  test('com production.view a aba Cronograma mostra só o projeto; sem, a aba não aparece', async ({ page }, info) => {
    await projectApi(page, true)
    await page.goto('/app/projetos/p2?aba=cronograma')
    await expect(page.getByRole('tab', { name: 'Cronograma' })).toHaveAttribute('aria-selected', 'true')
    if (info.project.name === 'celular') {
      await expect(page.getByTestId('gantt-projeto')).toHaveCount(1)
      await expect(page.getByRole('list', { name: 'Etapas de PRJ-2026-0003 · Ed. Monte Castelo' })).toContainText('Define a entrega')
    } else {
      await expect(page.getByRole('img', { name: 'Cronograma do projeto' })).toBeVisible()
      await expect(page.getByTestId('gantt-resumo')).toHaveText('1 projeto no período; 1 em risco. O mais atrasado é PRJ-2026-0003 · Ed. Monte Castelo, 7 dias depois do prometido.')
      await expect(page.getByRole('button', { name: /define a entrega$/ })).toBeVisible()
    }

    await page.unrouteAll({ behavior: 'ignoreErrors' })
    await projectApi(page, false)
    await page.reload()
    await expect(page.getByRole('tab', { name: 'Resumo' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByRole('tab', { name: 'Cronograma' })).toHaveCount(0)
  })
})
