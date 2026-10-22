import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const RS = String.fromCharCode(0x1e)
const codes = ['P', 'C', 'M', 'Q']
const names = ['Projeto', 'Corte', 'Montagem', 'Qualidade']
const hours = [2, 1.5, 3, 0.5]

const card = (id: string, number: string, index: number, over: Record<string, unknown> = {}) => ({
  id, number, status: index > 0 ? 'InProgress' : 'Open', productName: 'Elevador 6 paradas', quantity: 1, projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Aurora',
  partyId: 'cli1', partyName: 'Condomínio Aurora', columnKey: codes[index], currentStepId: `${id}-s${index + 1}`, currentStepCode: codes[index], currentStepName: names[index],
  currentStepStatus: 'Ready', currentStepIsQuality: codes[index] === 'Q', standardHours: hours[index], appliedHours: 0, line: 'Linha 1', codes, stepNames: names,
  currentStepIndex: index, promisedDate: '2026-12-20', risk: 'None', delayDays: null, readyToComplete: false, noRouting: false, inExecution: false, pausedReason: null,
  operatorInitials: null, routingTemplateId: 'rt1', version: 1, ...over,
})

function board(cards: ReturnType<typeof card>[]) {
  const column = (key: string, kind: string, code: string | null, name: string) => {
    const mine = cards.filter((c) => c.columnKey === key)
    return { key, kind, code, name, count: mine.length, remainingHours: mine.reduce((s, c) => s + Math.max(c.standardHours - c.appliedHours, 0), 0) }
  }
  return {
    columns: [...codes.map((c, i) => column(c, 'Step', c, names[i])), column('pronta', 'ReadyToComplete', null, 'Pronta para concluir')],
    cards, lateCount: cards.filter((c) => c.risk === 'Late').length, atRiskCount: cards.filter((c) => c.risk === 'AtRisk').length, openCount: cards.length,
    projects: [{ id: 'p1', label: 'PRJ-2026-0003 · Aurora' }], parties: [{ id: 'cli1', label: 'Condomínio Aurora' }], routings: [{ id: 'rt1', label: 'Elevador padrão' }], lines: ['Linha 1'],
  }
}

/** Hub simulado por long polling (negociação, handshake e mensagens empurradas pelo teste). */
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
  return { push: (orderId: string) => outbox.push(JSON.stringify({ type: 1, target: 'OrderBoardChanged', arguments: [{ productionOrderId: orderId }] }) + RS) }
}

async function kanbanApi(page: Page, state: { board: ReturnType<typeof board> }, overrides: Record<string, (r: Route) => unknown> = {}, actions = ALL) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ production: { actions } as never }) }),
    'GET /api/v1/production/board': (r: Route) => json(r, state.board),
    ...overrides,
  })
}

const column = (page: Page, name: string) => page.locator('section', { has: page.getByRole('heading', { name, exact: true }) })

test.describe('Kanban da produção (PRD-02)', () => {
  test('conclui a etapa pelo menu e o cartão muda de coluna; o aviso ao vivo move o outro cartão', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'a 400 px o quadro mostra uma coluna por vez (teste próprio)')
    const state = { board: board([card('of1', 'OS-2026-00007', 0), card('of2', 'OS-2026-00008', 1, { risk: 'Late', delayDays: 4 })]) }
    let sent: unknown
    await kanbanApi(page, state, {
      'POST /api/v1/production/production-orders/of1/steps/of1-s1/complete': (r: Route) => {
        sent = r.request().postDataJSON()
        state.board = board([card('of1', 'OS-2026-00007', 1, { version: 2 }), card('of2', 'OS-2026-00008', 1, { risk: 'Late', delayDays: 4 })])
        return json(r, {})
      },
    })
    const hub = await mockHub(page)

    await page.goto('/app/producao/kanban')
    await expect(page.getByRole('heading', { name: 'Produção: Kanban' })).toBeVisible()
    await expect(page.getByText('Ao vivo')).toBeVisible()
    await expect(column(page, 'Projeto').getByText('1 OS, 2 h restantes')).toBeVisible()
    await expect(column(page, 'Corte').getByText('Atrasada 4 d')).toBeVisible()
    await expect(page.getByRole('button', { name: '1 atrasada' })).toBeVisible()
    expect(await page.locator('[draggable="true"]').count()).toBe(0)

    await page.getByRole('button', { name: 'Ações da OS OS-2026-00007' }).click()
    await page.getByRole('menuitem', { name: 'Concluir etapa P Projeto' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByRole('button', { name: 'Concluir etapa', exact: true })).toBeDisabled()
    await dialog.getByLabel('Motivo').fill('Projeto enviado ao cliente por e-mail.')
    await dialog.getByRole('button', { name: 'Concluir etapa', exact: true }).click()
    await expect(page.getByText('Etapa concluída')).toBeVisible()
    expect(sent).toEqual({ reason: 'Projeto enviado ao cliente por e-mail.', version: 1 })
    await expect(column(page, 'Corte').getByRole('link', { name: 'OS-2026-00007' })).toBeVisible()

    // Outra aba concluiu a etapa da OS-2026-00008: o hub manda só o id e o quadro recarrega pela API.
    state.board = board([card('of1', 'OS-2026-00007', 1, { version: 2 }), card('of2', 'OS-2026-00008', 2, { risk: 'Late', delayDays: 4, version: 2 })])
    hub.push('of2')
    await expect(column(page, 'Montagem').getByRole('link', { name: 'OS-2026-00008' })).toBeVisible({ timeout: 5000 })
  })

  test('pelo teclado: setas andam entre os cartões e as colunas; "Ver como lista" mostra a tabela', async ({ page }, info) => {
    test.skip(info.project.name === 'celular', 'teclado no desktop')
    const state = { board: board([card('of1', 'OS-2026-00007', 0), card('of2', 'OS-2026-00008', 1), card('of3', 'OS-2026-00009', 1)]) }
    await kanbanApi(page, state)
    await mockHub(page)

    await page.goto('/app/producao/kanban?ver=lista')
    await expect(page.getByRole('table')).toBeVisible()
    await expect(page.getByRole('row')).toHaveCount(4)
    await page.getByRole('button', { name: 'Ver como quadro' }).click()
    await expect(page).not.toHaveURL(/ver=lista/)

    await page.getByRole('link', { name: 'OS-2026-00007' }).focus()
    await page.keyboard.press('ArrowRight')
    await expect(page.getByRole('link', { name: 'OS-2026-00008' })).toBeFocused()
    await page.keyboard.press('ArrowDown')
    await expect(page.getByRole('link', { name: 'OS-2026-00009' })).toBeFocused()
    await page.keyboard.press('ArrowLeft')
    await expect(page.getByRole('link', { name: 'OS-2026-00007' })).toBeFocused()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/app\/producao\/os\/of1$/)
  })

  test('a 400 px mostra uma coluna por vez com o seletor de etapa e a faixa com texto', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular', 'só a 400 px')
    const state = { board: board([card('of1', 'OS-2026-00007', 0), card('of2', 'OS-2026-00008', 1, { risk: 'AtRisk', promisedDate: '2026-10-25' })]) }
    await kanbanApi(page, state)
    await mockHub(page)

    await page.goto('/app/producao/kanban')
    const tabs = page.getByRole('tablist', { name: 'Etapa' })
    await expect(tabs).toBeVisible()
    await expect(page.getByRole('button', { name: '1 em risco' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'OS-2026-00007' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'OS-2026-00008' })).toBeHidden()
    await tabs.getByRole('tab', { name: /Corte/ }).click()
    await expect(page).toHaveURL(/coluna=C/)
    await expect(page.getByRole('link', { name: 'OS-2026-00008' })).toBeVisible()
    await expect(page.getByText('Em risco, previsto 25/10/2026')).toBeVisible()
    const width = await page.evaluate(() => document.documentElement.scrollWidth)
    expect(width).toBeLessThanOrEqual(400)
  })

  test('sem tempo real mostra "Offline" e o aviso de reconexão; sem production.approve o menu não conclui', async ({ page }) => {
    const state = { board: board([card('of1', 'OS-2026-00007', 0)]) }
    await kanbanApi(page, state, {}, ['view'])
    await page.route('**/hubs/production/negotiate**', (r) => r.fulfill({ status: 503, body: '' }))

    await page.goto('/app/producao/kanban')
    await expect(page.getByText('Atualização ao vivo pausada. Tentando reconectar…')).toBeVisible()
    await expect(page.getByText('Offline')).toBeVisible()
    await page.getByRole('button', { name: 'Ações da OS OS-2026-00007' }).click()
    await expect(page.getByRole('menuitem', { name: 'Abrir OS' })).toBeVisible()
    await expect(page.getByRole('menuitem', { name: /Concluir etapa/ })).toHaveCount(0)
  })

  test('vazio leva às OS de fabricação; filtro sem resultado oferece "Limpar filtros"', async ({ page }) => {
    const state = { board: { ...board([]), openCount: 0 } }
    await kanbanApi(page, state)
    await mockHub(page)

    await page.goto('/app/producao/kanban')
    await expect(page.getByText('Nenhuma OS aberta. As OS nascem com a aprovação do orçamento.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver OS de fabricação' })).toBeVisible()

    state.board = { ...board([]), openCount: 3 }
    await page.goto('/app/producao/kanban?atrasadas=1')
    await expect(page.getByText('Nenhuma OS com estes filtros.')).toBeVisible()
    await page.getByRole('button', { name: 'Limpar filtros' }).first().click()
    await expect(page).not.toHaveURL(/atrasadas/)
  })
})
