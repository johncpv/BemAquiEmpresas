import { expect, type Locator, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const codes = ['C', 'M', 'Q']
const names = ['Corte', 'Montagem', 'Qualidade']
const quality = { actions: ['view', 'create', 'edit', 'approve', 'delete', 'approve_quality'] }

const card = (over: Record<string, unknown> = {}) => ({
  stepId: 'q1', orderId: 'of1', orderNumber: 'OS-2026-00041', stepCode: 'Q', stepName: 'Qualidade', stepIndex: 2, codes, stepNames: names, status: 'Ready',
  productName: 'Cabina e portas', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo', partyName: 'Condomínio Monte Castelo', promisedDate: '2026-12-20',
  attempt: 1, waitingSince: '2026-10-22T10:00:00Z', canDecide: true, selfInspection: false, openNonConformities: 0, ...over,
})

interface State { attempt: number; results: (string | null)[]; blocked: boolean }

const detail = (s: State) => ({
  step: card({ attempt: s.attempt, openNonConformities: s.attempt > 1 ? 1 : 0, canDecide: !s.blocked }), productCode: 'CAB-01', quantity: 1,
  checklist: ['Painel riscado', 'Nivelamento da porta'].map((text, i) => ({ id: `c${i}`, text, result: s.results[i], markedAt: null, markedByName: s.results[i] ? 'Paula Lima' : null })),
  returnOptions: [
    { stepId: 'm', code: 'M', name: 'Montagem', suggested: true, waiting: 0, impact: 'Volta para Montagem.' },
    { stepId: 'c', code: 'C', name: 'Corte', suggested: false, waiting: 1, impact: 'Volta para Corte; 1 etapa volta a esperar.' },
  ],
  responsibles: [{ id: 'u1', name: 'Paula Lima' }, { id: 'u2', name: 'Rui Supervisor' }], defaultResponsibleId: 'u1', nextStepName: null, approvalImpact: 'A OS fica pronta para concluir.',
  blockedReason: s.blocked ? 'Quem fez uma etapa desta tentativa não aprova a qualidade dela.' : null, inspectionStarted: false, canStartInspection: false, version: 7, serverNow: new Date().toISOString(),
})

async function qualityApi(page: Page, state: State, overrides: Record<string, (r: Route) => unknown> = {}) {
  const posted: Record<string, unknown[]> = {}
  const record = (key: string, r: Route) => { (posted[key] ??= []).push({ body: r.request().postDataJSON(), key: r.request().headers()['idempotency-key'] }) }
  const mark = (i: number) => (r: Route) => { state.results[i] = (r.request().postDataJSON() as { result: string | null }).result; return json(r, detail(state)) }
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ production: quality }) }),
    'GET /api/v1/production/quality/queue': (r: Route) => json(r, { items: [card({ attempt: state.attempt })], mineCount: 1, totalCount: 1 }),
    'GET /api/v1/production/quality/steps/q1': (r: Route) => json(r, detail(state)),
    'PUT /api/v1/production/quality/steps/q1/checklist/c0': mark(0),
    'PUT /api/v1/production/quality/steps/q1/checklist/c1': mark(1),
    'POST /api/v1/production/quality/steps/q1/reject': (r: Route) => {
      record('reject', r)
      return json(r, { orderId: 'of1', orderNumber: 'OS-2026-00041', result: 'Rejected', attempt: 1, selfInspection: false, nextStepName: 'Montagem', nextStepId: 'm', readyToComplete: false,
        canCompleteOrder: false, nonConformityId: 'nc1', nonConformityNumber: 'NC-2026-0001', returnToStepName: 'Montagem', message: 'NC NC-2026-0001 aberta' })
    },
    'POST /api/v1/production/non-conformities/nc1/attachments': (r: Route) => { (posted.upload ??= []).push(r.request().headers()['content-type']); return json(r, { id: 'a1', fileName: 'defeito.jpg', contentType: 'image/jpeg', size: 10, scanStatus: 1, createdAt: new Date().toISOString() }, 201) },
    'POST /api/v1/production/quality/steps/q1/approve': (r: Route) => {
      record('approve', r)
      return json(r, { orderId: 'of1', orderNumber: 'OS-2026-00041', result: 'Approved', attempt: 2, selfInspection: false, nextStepName: null, nextStepId: null, readyToComplete: true,
        canCompleteOrder: false, nonConformityId: null, nonConformityNumber: null, returnToStepName: null, message: 'Qualidade aprovada' })
    },
    ...overrides,
  })
  return posted
}

/** Todo alvo de toque visível tem 56 × 56 px ou mais (padrão 1 da Fase 3). */
async function expectTargets(scope: Locator) {
  const boxes = await scope.locator('button:visible, a:visible, label[data-floor-target]:visible').evaluateAll((els) =>
    els.map((e) => { const r = e.getBoundingClientRect(); return { w: Math.round(r.width), h: Math.round(r.height), text: (e.textContent ?? e.getAttribute('aria-label') ?? '').trim().slice(0, 40) } }))
  expect(boxes.length).toBeGreaterThan(0)
  for (const b of boxes) {
    expect(b.h, `altura de "${b.text}"`).toBeGreaterThanOrEqual(56)
    expect(b.w, `largura de "${b.text}"`).toBeGreaterThanOrEqual(56)
  }
}

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

test.describe('Qualidade e não conformidade (PRD-05)', () => {
  test('no tablet: reprova com foto devolvendo para Montagem e aprova a segunda tentativa', async ({ page }, info) => {
    if (info.project.name === 'desktop') await page.setViewportSize({ width: 800, height: 1280 })
    const state: State = { attempt: 1, results: [null, null], blocked: false }
    const posted = await qualityApi(page, state)

    await page.goto('/fab/qualidade')
    await expect(page.getByRole('button', { name: 'Aguardando você (1)' })).toHaveAttribute('aria-pressed', 'true')
    await expectTargets(page.locator('main'))
    await page.getByRole('link', { name: /OS-2026-00041, Qualidade, tentativa 1/ }).click()
    await expect(page.getByRole('img', { name: 'Etapa Qualidade, 3 de 3' })).toBeVisible()

    // Checklist: um "Não conforme" bloqueia o aprovar e vira o rascunho da descrição.
    await page.getByRole('group', { name: 'Resultado: Painel riscado' }).getByRole('button', { name: 'Não conforme' }).click()
    await page.getByRole('group', { name: 'Resultado: Nivelamento da porta' }).getByRole('button', { name: 'Conforme', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Aprovar qualidade' })).toBeDisabled()
    await expect(page.getByText('Há item não conforme: reprove para abrir a não conformidade.')).toBeVisible()
    await expectTargets(page.locator('main'))
    await expectNoHorizontalScroll(page)

    await page.getByRole('button', { name: 'Reprovar qualidade' }).click()
    const sheet = page.getByRole('alertdialog', { name: 'Reprovar qualidade · OS-2026-00041' })
    await expect(sheet.getByRole('textbox').first()).toHaveValue('Não conforme: Painel riscado.')
    await expect(sheet.getByRole('radio', { name: /M · Montagem/ })).toBeChecked()
    await expect(sheet.getByText('Sugerido')).toBeVisible()
    await expect(sheet.getByRole('status').filter({ hasText: 'Volta para Montagem.' })).toBeVisible()
    await sheet.locator('#foto-nc').setInputFiles({ name: 'defeito.jpg', mimeType: 'image/jpeg', buffer: Buffer.from([0xff, 0xd8, 0xff, 0xe0]) })
    await expect(sheet.getByText('defeito.jpg')).toBeVisible()
    await expectTargets(sheet)
    await sheet.getByRole('button', { name: 'Reprovar qualidade' }).click()
    await expect(page.getByText('NC NC-2026-0001 aberta').first()).toBeVisible()
    expect(posted.reject[0]).toMatchObject({ body: { description: 'Não conforme: Painel riscado.', responsibleUserId: 'u1', returnToStepId: 'm', version: 7 }, key: expect.stringMatching(/[0-9a-f-]{36}/) })
    expect(posted.upload).toEqual([expect.stringContaining('multipart/form-data')])
    await expect(page.getByText('A OS voltou para Montagem.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Abrir a NC-2026-0001' })).toBeVisible()

    // A Montagem foi refeita: a mesma etapa de qualidade volta na tentativa 2, com o checklist zerado; outro inspetor aprova.
    state.attempt = 2
    state.results = [null, null]
    await page.goto('/fab/qualidade/q1')
    await expect(page.getByText('Tentativa 2')).toBeVisible()
    await expect(page.getByText('NC aberta')).toBeVisible()
    await expect(page.getByText('Marque os 2 itens que faltam.')).toBeVisible()
    for (const name of ['Painel riscado', 'Nivelamento da porta']) await page.getByRole('group', { name: `Resultado: ${name}` }).getByRole('button', { name: 'Conforme', exact: true }).click()
    await page.getByRole('button', { name: 'Aprovar qualidade' }).click()
    const confirm = page.getByRole('alertdialog', { name: 'Aprovar qualidade · OS-2026-00041' })
    await expect(confirm.getByText('A OS fica pronta para concluir.')).toBeVisible()
    await confirm.getByRole('button', { name: 'Aprovar qualidade' }).click()
    await expect(page.getByText('Qualidade aprovada').first()).toBeVisible()
    await expect(page.getByText('A OS está pronta para concluir. O supervisor conclui.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Concluir OS' })).toHaveCount(0)
    expect(posted.approve[0]).toMatchObject({ body: { version: 7 } })
  })

  test('quem fez parte do trabalho não aprova, mas pode reprovar', async ({ page }) => {
    const state: State = { attempt: 1, results: ['Done', 'Done'], blocked: true }
    await qualityApi(page, state)
    await page.goto('/fab/qualidade/q1')
    await expect(page.getByText(/Quem fez uma etapa desta tentativa não aprova a qualidade dela\./)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Aprovar qualidade' })).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Reprovar qualidade' })).toBeEnabled()
  })

  test('backoffice: fila vazia, lista de NC "Aguardando você" e o detalhe com "Iniciar ação"', async ({ page }) => {
    const nc = {
      id: 'nc1', number: 'NC-2026-0001', status: 'Open', orderId: 'of1', orderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Ed. Monte Castelo',
      partyName: 'Condomínio Monte Castelo', stepId: 'q1', stepCode: 'Q', stepName: 'Qualidade', returnToStepId: 'm', returnToStepName: 'Montagem', attempt: 1, description: 'Painel riscado na lateral',
      cause: null, action: null, responsibleUserId: 'u1', responsibleName: 'Paula Lima', cost: null, costVisible: false, result: null, openedByName: 'Paula Lima', openedAt: '2026-10-20T12:00:00Z',
      actionStartedByName: null, actionStartedAt: null, closedByName: null, closedAt: null, ageDays: 2, responsibles: [{ id: 'u1', name: 'Paula Lima' }], version: 3,
    }
    const started: unknown[] = []
    await mockApi(page, {
      'GET /api/v1/production/quality/queue': (r: Route) => json(r, { items: [], mineCount: 0, totalCount: 0 }),
      'GET /api/v1/production/non-conformities': (r: Route) => json(r, { items: [{ id: 'nc1', number: 'NC-2026-0001', orderId: 'of1', orderNumber: 'OS-2026-00041', projectId: 'p1', projectNumber: 'PRJ-2026-0003',
        stepCode: 'Q', stepName: 'Qualidade', responsibleUserId: 'u1', responsibleName: 'Paula Lima', status: 'Open', ageDays: 2, openedAt: '2026-10-20T12:00:00Z', description: 'Painel riscado na lateral', attempt: 1 }],
        page: 1, pageSize: 25, total: 1, mineOpen: 1 }),
      'GET /api/v1/production/non-conformities/nc1': (r: Route) => json(r, nc),
      'GET /api/v1/production/non-conformities/nc1/attachments': (r: Route) => json(r, []),
      'POST /api/v1/production/non-conformities/nc1/start-action': (r: Route) => { started.push(r.request().postDataJSON()); return json(r, { ...nc, status: 'InAction', cause: 'Transporte', action: 'Trocar painel' }) },
    })
    await page.goto('/app/producao/qualidade')
    await expect(page.getByText('Nenhuma OS aguardando qualidade.')).toBeVisible()

    await page.goto('/app/producao/nao-conformidades')
    await expect(page.getByRole('button', { name: 'Aguardando você (1)' })).toHaveAttribute('aria-pressed', 'true')
    const row = page.getByRole('row').filter({ hasText: 'NC-2026-0001' })
    await expect(row.getByText('Aberta')).toBeVisible()
    await expect(row.getByText('Paula Lima')).toBeVisible()
    await expectNoHorizontalScroll(page)
    await row.getByRole('link', { name: 'NC-2026-0001' }).click()

    await expect(page.getByRole('heading', { name: /NC-2026-0001/ })).toBeVisible()
    await page.getByLabel('Causa').fill('Transporte')
    await page.getByLabel('Ação corretiva').fill('Trocar painel')
    await page.getByRole('button', { name: 'Iniciar ação' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Iniciar ação' }).click()
    await expect(page.getByRole('paragraph').filter({ hasText: /^Ação iniciada$/ })).toBeVisible()
    expect(started[0]).toMatchObject({ cause: 'Transporte', action: 'Trocar painel', version: 3 })
    await expectNoHorizontalScroll(page)
  })
})