import { expect, type Locator, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const RS = String.fromCharCode(0x1e)
const codes = ['C', 'M', 'Q']
const names = ['Corte', 'Montagem', 'Qualidade']

const queueStep = (id: string, over: Record<string, unknown> = {}) => ({
  stepId: id, orderId: 'of1', orderNumber: 'OS-2026-00041', stepCode: 'M', stepName: 'Montagem', stepIndex: 1, codes, stepNames: names, status: 'Ready',
  workCenterId: 'wc1', workCenterName: 'Montagem', isMyCenter: true, productName: 'Cabina e portas', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo',
  partyName: 'Condomínio Monte Castelo', promisedDate: '2026-12-20', standardHours: 18, appliedHours: 0, people: [], pausedReason: null, awaitingApproval: false, ...over,
})
const other = queueStep('s2', { orderId: 'of2', orderNumber: 'OS-2026-00042', stepCode: 'C', stepName: 'Corte', stepIndex: 0, workCenterId: 'wc2', workCenterName: 'Corte', isMyCenter: false })
const operator = { employeeId: 'e1', name: 'Diego Santos', firstName: 'Diego', workCenterId: 'wc1', workCenterName: 'Montagem', line: 'Linha 1', today: '2026-10-16' }

interface State { started: boolean; paused: boolean; reason: string | null; checklist: boolean[]; people: { name: string; isMe: boolean }[] }

function detail(state: State) {
  const startedAt = new Date(Date.now() - 2.5 * 3600 * 1000 - 30_000).toISOString()
  const me = state.started ? [{ name: 'Diego Santos', paused: state.paused, pauseReason: state.paused ? state.reason : null, startedAt, isMe: true }] : []
  return {
    step: queueStep('s1', { status: state.started ? (state.paused ? 'Paused' : 'InProgress') : 'Ready', people: [...me, ...state.people].map((p) => p.name), appliedHours: state.started ? 21 : 0 }),
    productCode: 'CAB-01', quantity: 1,
    checklist: ['Painéis laterais fixados', 'Operador de porta ajustado'].map((text, i) => ({ id: `c${i}`, text, done: state.checklist[i], markedAt: null, markedByName: state.checklist[i] ? 'Diego Santos' : null })),
    people: [...me, ...state.people.map((p) => ({ name: p.name, paused: false, pauseReason: null, startedAt, isMe: false }))],
    mine: state.started ? { entryId: 'le1', startedAt, closedPauseSeconds: 0, pauseStartedAt: state.paused ? new Date().toISOString() : null, pauseReason: state.paused ? state.reason : null, pauseNotes: null, netMinutes: 150 } : null,
    attachments: [{ id: 'a1', fileName: 'desenho-cabina.pdf', contentType: 'application/pdf', size: 1200, viewable: true }, { id: 'a2', fileName: 'planta.dwg', contentType: 'application/acad', size: 3000, viewable: false }],
    requiresApproval: false, isQualityStep: false, nextStepName: 'Qualidade', suggestedPauseReason: 'Material', canStart: !state.started, startBlockedReason: null, serverNow: new Date().toISOString(),
  }
}

async function fabApi(page: Page, state: State, overrides: Record<string, (r: Route) => unknown> = {}, modules = allModules()) {
  const result = (step: unknown, extra: Record<string, unknown> = {}) => ({ step, myHours: 0, nextInQueue: null, closedFor: [], ...extra })
  const posted: Record<string, unknown[]> = {}
  const record = (key: string, r: Route) => { (posted[key] ??= []).push({ body: r.request().postDataJSON(), key: r.request().headers()['idempotency-key'] }) }
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules }),
    'GET /api/v1/shopfloor/queue': (r: Route) => json(r, { operator, current: state.started ? detail(state).step : null, steps: state.started ? [other] : [queueStep('s1'), other], serverNow: new Date().toISOString() }),
    'GET /api/v1/shopfloor/steps/s1': (r: Route) => json(r, detail(state)),
    'POST /api/v1/shopfloor/steps/s1/start': (r: Route) => { record('start', r); state.started = true; return json(r, result(detail(state))) },
    'POST /api/v1/shopfloor/steps/s1/pause': (r: Route) => { record('pause', r); state.paused = true; state.reason = (r.request().postDataJSON() as { reason: string }).reason; return json(r, result(detail(state))) },
    'POST /api/v1/shopfloor/steps/s1/resume': (r: Route) => { record('resume', r); state.paused = false; return json(r, result(detail(state))) },
    'PUT /api/v1/shopfloor/steps/s1/checklist/c0': (r: Route) => { state.checklist[0] = (r.request().postDataJSON() as { done: boolean }).done; return json(r, detail(state)) },
    'PUT /api/v1/shopfloor/steps/s1/checklist/c1': (r: Route) => { state.checklist[1] = (r.request().postDataJSON() as { done: boolean }).done; return json(r, detail(state)) },
    'POST /api/v1/shopfloor/steps/s1/finish': (r: Route) => { record('finish', r); return json(r, result(null, { myHours: 2.5, nextInQueue: other, closedFor: state.people.map((p) => p.name) })) },
    'POST /api/v1/shopfloor/steps/s1/leave': (r: Route) => { record('leave', r); state.started = false; return json(r, result(detail(state), { myHours: 2.5 })) },
    'POST /api/v1/shopfloor/steps/s2/start': (r: Route) => { record('start-s2', r); return json(r, result(null)) },
    'GET /api/v1/shopfloor/steps/s2': (r: Route) => json(r, { ...detail({ ...state, started: true, paused: false }), step: { ...other, status: 'InProgress' } }),
    'GET /api/v1/shopfloor/orders': (r: Route) => (new URL(r.request().url()).searchParams.get('number') === '41' ? json(r, [queueStep('s1')]) : json(r, { code: 'order_not_found', detail: 'OS não encontrada.' }, 404)),
    ...overrides,
  })
  return posted
}

/** Todo alvo de toque visível tem 56 × 56 px ou mais (CA 24). */
async function expectTargets(scope: Locator) {
  const boxes = await scope.locator('button:visible, a:visible, label[data-floor-target]:visible').evaluateAll((els) =>
    els.map((e) => { const r = e.getBoundingClientRect(); return { w: Math.round(r.width), h: Math.round(r.height), text: (e.textContent ?? e.getAttribute('aria-label') ?? '').trim().slice(0, 40) } }))
  expect(boxes.length).toBeGreaterThan(0)
  for (const b of boxes) {
    expect(b.h, `altura de "${b.text}"`).toBeGreaterThanOrEqual(56)
    expect(b.w, `largura de "${b.text}"`).toBeGreaterThanOrEqual(56)
  }
}

/** Distância entre dois alvos (a maior entre a horizontal e a vertical). */
async function gap(a: Locator, b: Locator) {
  const [x, y] = [await a.boundingBox(), await b.boundingBox()]
  return Math.round(Math.max(y!.x - (x!.x + x!.width), x!.x - (y!.x + y!.width), y!.y - (x!.y + x!.height), x!.y - (y!.y + y!.height)))
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

test.describe('App do operador (PRD-03a)', () => {
  test('entra, liga "Só meu centro", inicia, pausa por falta de material, retoma e finaliza pela folha de tela cheia', async ({ page }) => {
    const state: State = { started: false, paused: false, reason: null, checklist: [false, false], people: [] }
    const posted = await fabApi(page, state)

    await page.goto('/fab')
    await expect(page.getByText('Olá, Diego')).toBeVisible()
    await expect(page.getByText('Linha 1, sexta 16/10')).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Navegação principal' })).toHaveCount(0, { timeout: 1000 })
    await expect(page.getByRole('link', { name: /OS-2026-00042, etapa Corte/ })).toBeVisible()
    await page.getByRole('button', { name: 'Só meu centro', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Só meu centro', exact: true })).toHaveAttribute('aria-pressed', 'true')
    await expect(page.getByRole('link', { name: /OS-2026-00042/ })).toHaveCount(0)
    await page.reload()
    await expect(page.getByRole('button', { name: 'Só meu centro', exact: true })).toHaveAttribute('aria-pressed', 'true', { timeout: 10_000 })

    await page.getByRole('link', { name: /OS-2026-00041, etapa Montagem/ }).click()
    await expect(page.getByText('Cabina e portas', { exact: false })).toBeVisible()
    await expect(page.getByRole('img', { name: 'Etapa Montagem, 2 de 3' })).toBeVisible()
    await page.getByRole('button', { name: 'Iniciar etapa' }).click()
    await expect(page.getByText('Etapa iniciada')).toBeVisible()
    await expect(page.getByText(/^02:30:\d\d$/)).toBeVisible()
    await expect(page.getByText(/Em andamento desde \d\d:\d\d\. Padrão da etapa: 18 h, apontadas 21 h\./)).toBeVisible()
    expect(posted.start[0]).toMatchObject({ key: expect.stringMatching(/[0-9a-f-]{36}/) })

    // Pausa com motivo (o mais usado vem primeiro) e "Retomar".
    await page.getByRole('button', { name: 'Pausar' }).click()
    const sheet = page.getByRole('dialog', { name: 'Pausar a etapa' })
    await expect(sheet.getByRole('radio').first()).toBeChecked()
    await expect(sheet.getByText('Falta de material')).toBeVisible()
    await sheet.getByRole('button', { name: 'Pausar' }).click()
    await expect(page.getByText('Etapa pausada')).toBeVisible()
    expect(posted.pause[0]).toMatchObject({ body: { reason: 'Material', notes: null } })
    await expect(page.getByText(/Pausada: Falta de material/)).toBeVisible()
    await page.getByRole('button', { name: 'Retomar' }).click()
    await expect(page.getByText('Etapa retomada')).toBeVisible()

    // Checklist obrigatório (CA 13) e a confirmação em folha de tela cheia (padrão 4).
    await expect(page.getByRole('button', { name: 'Finalizar etapa' })).toBeDisabled()
    await expect(page.getByText('Marque os 2 itens que faltam')).toBeVisible()
    await page.getByLabel('Painéis laterais fixados').check()
    await page.getByLabel('Operador de porta ajustado').check()
    await page.getByRole('button', { name: 'Finalizar etapa' }).click()
    const confirm = page.getByRole('alertdialog', { name: 'Finalizar etapa · 2,5 h apontadas' })
    await expect(confirm.getByText('Montagem: Em andamento → Concluída')).toBeVisible()
    await expect(confirm.getByText('Próxima: Qualidade fica pronta')).toBeVisible()
    expect(await gap(confirm.getByRole('button', { name: 'Voltar' }), confirm.getByRole('button', { name: 'Finalizar etapa' }))).toBeGreaterThanOrEqual(16)
    await confirm.getByRole('button', { name: 'Finalizar etapa' }).click()
    await expect(page.getByText('Etapa finalizada: 2,5 h apontadas').first()).toBeVisible()
    expect(posted.finish[0]).toMatchObject({ body: { forAll: false } })
    await page.getByRole('button', { name: 'Iniciar próxima: Corte da OS-2026-00042' }).click()
    await expect(page).toHaveURL(/\/fab\/etapa\/s2$/)
  })

  test('dois operadores: "Sair da etapa (só eu)" e "Concluir etapa para todos" com os nomes de quem será encerrado', async ({ page }) => {
    const state: State = { started: true, paused: false, reason: null, checklist: [true, true], people: [{ name: 'Rafael Alves', isMe: false }] }
    const posted = await fabApi(page, state)

    await page.goto('/fab/etapa/s1')
    await expect(page.getByText('Você, Rafael Alves')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Finalizar etapa' })).toHaveCount(0)
    await page.getByRole('button', { name: 'Concluir etapa para todos' }).click()
    const all = page.getByRole('alertdialog', { name: /Concluir etapa para todos · 2,5 h apontadas/ })
    await expect(all.getByText('Encerra também: Rafael Alves')).toBeVisible()
    await all.getByRole('button', { name: 'Voltar' }).click()

    await page.getByRole('button', { name: 'Sair da etapa (só eu)' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Sair da etapa' }).click()
    await expect(page.getByText('Você saiu da etapa')).toBeVisible()
    expect(posted.leave).toHaveLength(1)
  })

  test('tablet 800 × 1280 e celular 400 px: alvos de 56 px, 8 e 16 px entre alvos, texto de 18 px e sem rolagem lateral', async ({ page }, info) => {
    if (info.project.name === 'desktop') await page.setViewportSize({ width: 800, height: 1280 })
    const state: State = { started: false, paused: false, reason: null, checklist: [false, false], people: [] }
    await fabApi(page, state)

    await page.goto('/fab')
    await expect(page.getByText('Olá, Diego')).toBeVisible()
    await expectTargets(page.locator('main'))
    await expectNoHorizontalScroll(page)
    expect(await gap(page.getByRole('button', { name: 'Só meu centro', exact: true }), page.getByRole('button', { name: /Dica: Só meu centro/ }))).toBeGreaterThanOrEqual(8)
    const size = await page.locator('main').evaluate((m) => parseFloat(getComputedStyle(m).fontSize))
    expect(size).toBeGreaterThanOrEqual(18)

    await page.getByRole('button', { name: 'Buscar OS pelo número' }).click()
    const search = page.getByRole('dialog', { name: 'Buscar OS pelo número' })
    await expectTargets(search)
    await search.getByRole('button', { name: '4', exact: true }).click()
    await search.getByRole('button', { name: '2', exact: true }).click()
    await search.getByRole('button', { name: 'Buscar' }).click()
    await expect(search.getByText('OS não encontrada.')).toBeVisible()
    await search.getByRole('button', { name: 'Apagar o último número' }).click()
    await search.getByRole('button', { name: '1', exact: true }).click()
    await search.getByRole('button', { name: 'Buscar' }).click()
    await search.getByRole('link', { name: /OS-2026-00041/ }).click()

    await page.getByRole('button', { name: 'Iniciar etapa' }).click()
    await expect(page.getByRole('button', { name: 'Pausar' })).toBeVisible()
    await expectTargets(page.locator('main'))
    await expectNoHorizontalScroll(page)
    expect(await gap(page.getByRole('button', { name: 'Pausar' }), page.getByRole('button', { name: 'Finalizar etapa' }))).toBeGreaterThanOrEqual(16)
    const timer = page.getByText(/^\d\d:\d\d:\d\d$/)
    expect(await timer.evaluate((t) => getComputedStyle(t).fontVariantNumeric)).toContain('tabular-nums')
    await page.getByRole('button', { name: 'Pausar' }).click()
    await expectTargets(page.getByRole('dialog', { name: 'Pausar a etapa' }))
  })

  test('anexos: PDF abre na tela, DWG só baixa', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'mesma tela')
    const state: State = { started: true, paused: false, reason: null, checklist: [false, false], people: [] }
    await fabApi(page, state, {
      'GET /api/v1/shopfloor/steps/s1/attachments/a1': (r: Route) => r.fulfill({ status: 200, contentType: 'application/pdf', body: '%PDF-1.4\n%%EOF' }),
    })
    await page.goto('/fab/etapa/s1')
    const row = (name: string) => page.getByRole('listitem').filter({ hasText: name })
    await expect(row('planta.dwg').getByRole('button', { name: 'Abrir' })).toHaveCount(0)
    await expect(row('planta.dwg').getByRole('button', { name: 'Baixar' })).toBeVisible()
    await row('desenho-cabina.pdf').getByRole('button', { name: 'Abrir' }).click()
    await expect(page.getByRole('dialog', { name: 'desenho-cabina.pdf' }).locator('iframe')).toBeVisible()
  })

  test('sem funcionário ativo mostra o estado; sem módulo liberado, a tela padrão', async ({ page }) => {
    const state: State = { started: false, paused: false, reason: null, checklist: [false, false], people: [] }
    await fabApi(page, state, {
      'GET /api/v1/shopfloor/queue': (r: Route) => json(r, { code: 'employee_not_active', detail: 'Seu usuário não está ligado a um funcionário ativo. Fale com o supervisor.' }, 403),
    })
    await page.goto('/fab')
    await expect(page.getByText('Seu usuário não está ligado a um funcionário ativo. Fale com o supervisor.')).toBeVisible()

    const locked = await page.context().newPage()
    await fabApi(locked, state, {}, allModules({ shopfloor: { released: false } }))
    await locked.goto('/fab')
    await expect(locked.getByText('Funcionalidade não liberada nessa versão do sistema.')).toBeVisible()
  })

  test('dock leva ao /fab e o tema segue o sistema (automático)', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'dock no desktop')
    const state: State = { started: false, paused: false, reason: null, checklist: [false, false], people: [] }
    await fabApi(page, state)
    await page.emulateMedia({ colorScheme: 'dark' })
    await page.goto('/app')
    await page.getByRole('button', { name: 'Telas' }).click()
    await page.getByLabel('Telas dedicadas').getByRole('link', { name: 'App do operador' }).click()
    await expect(page).toHaveURL(/\/fab$/)
    await expect(page.getByText('Olá, Diego')).toBeVisible()
    const background = await page.locator('main').evaluate((m) => getComputedStyle(m).backgroundColor)
    expect(background).toBe('rgb(14, 20, 25)')
  })

  test('o Kanban em outra aba atualiza quando o operador inicia a etapa', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'quadro no desktop')
    const state: State = { started: false, paused: false, reason: null, checklist: [false, false], people: [] }
    await fabApi(page, state)
    const kanban = await page.context().newPage()
    const card = (inExecution: boolean) => ({
      id: 'of1', number: 'OS-2026-00041', status: inExecution ? 'InProgress' : 'Open', productName: 'Cabina e portas', quantity: 1, projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo',
      partyId: 'cli1', partyName: 'Condomínio', columnKey: 'M', currentStepId: 's1', currentStepCode: 'M', currentStepName: 'Montagem', currentStepStatus: inExecution ? 'InProgress' : 'Ready',
      currentStepIsQuality: false, standardHours: 18, appliedHours: 0, line: 'Linha 1', codes, stepNames: names, currentStepIndex: 1, promisedDate: '2026-12-20', risk: 'None', delayDays: null,
      readyToComplete: false, noRouting: false, inExecution, pausedReason: null, operatorInitials: inExecution ? 'DS' : null, routingTemplateId: 'rt1', version: 1,
    })
    const board = () => ({
      columns: names.map((n, i) => ({ key: codes[i], kind: 'Step', code: codes[i], name: n, count: i === 1 ? 1 : 0, remainingHours: 0 })), cards: [card(state.started)], lateCount: 0, atRiskCount: 0, openCount: 1,
      projects: [], parties: [], routings: [], lines: [],
    })
    await mockApi(kanban, { 'GET /api/v1/production/board': (r: Route) => json(r, board()) })
    const outbox: string[] = []
    let polls = 0
    await kanban.route('**/hubs/production/negotiate**', (r) => json(r, { negotiateVersion: 1, connectionId: 'c1', connectionToken: 't1', availableTransports: [{ transport: 'LongPolling', transferFormats: ['Text', 'Binary'] }] }))
    await kanban.route(/\/hubs\/production\?/, async (route) => {
      const method = route.request().method()
      if (method === 'POST') {
        if ((route.request().postData() ?? '').includes('"protocol"')) outbox.push('{}' + RS)
        return route.fulfill({ status: 200, contentType: 'text/plain', body: '' })
      }
      if (method === 'DELETE') return route.fulfill({ status: 202, body: '' })
      polls++
      if (polls > 1 && outbox.length === 0) await new Promise((resolve) => setTimeout(resolve, 250))
      return route.fulfill({ status: 200, contentType: 'text/plain', body: polls > 1 ? outbox.splice(0).join('') : '' }).catch(() => undefined)
    })
    await kanban.goto('/app/producao/kanban')
    await expect(kanban.getByText('Ao vivo')).toBeVisible()
    await expect(kanban.getByText('Em execução')).toHaveCount(0)

    await page.goto('/fab/etapa/s1')
    await page.getByRole('button', { name: 'Iniciar etapa' }).click()
    await expect(page.getByText('Etapa iniciada')).toBeVisible()
    outbox.push(JSON.stringify({ type: 1, target: 'OrderBoardChanged', arguments: [{ productionOrderId: 'of1' }] }) + RS)
    await expect(kanban.getByText('Em execução')).toBeVisible({ timeout: 5000 })
  })
})