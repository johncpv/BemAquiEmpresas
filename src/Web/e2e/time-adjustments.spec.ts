import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

/** RH-01b: o funcionário pede o ajuste no celular (400 px), o Proprietário aprova e o dia muda; o abono protegido sem hr.view_health. */
const BANNER = 'Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).'

const clock = {
  banner: BANNER, firstName: 'Ana', serverNow: '2026-10-21T15:02:00Z', today: '2026-10-21', schedule: '08:00–17:48, intervalo 1 h',
  entries: [{ id: 'te1', at: '2026-10-21T11:00:00Z', type: 'In', label: 'Entrada', source: 'App', location: 'NotAsked' }],
  suggested: 'BreakStart', suggestedVerb: 'Registrar saída para o intervalo', outOfSequence: true, hint: 'Esqueceu uma marcação? Peça o ajuste ao RH.',
  geolocation: 'Off', geoConsentNeeded: false, geoConsentText: '', geoConsentVersion: '2026-10-27', registeredDevice: false,
}

const adjustment = (over: Record<string, unknown> = {}) => ({
  id: 'adj1', employeeId: 'e1', employeeName: 'Ana Souza', kind: 'AddEntry', status: 'Pending', statusLabel: 'Aguardando aprovação', day: '2026-10-20',
  summary: 'Incluir saída às 17:50', at: '2026-10-20T20:50:00Z', entryType: 'Out', entryId: null, absenceMinutes: null, absenceReason: null, absenceReasonLabel: null,
  justification: 'Esqueci de marcar a saída', protected: false, hasDocument: false, documents: [], source: 'Employee', requestedBy: 'u-ana', requestedByName: 'Ana Souza',
  requestedAt: '2026-10-21T12:00:00Z', decidedBy: null, decidedByName: null, decidedAt: null, decisionComment: null, selfApproved: false, createdEntryId: null,
  canDecide: true, canCancel: false, version: 7, ...over,
})

const impact = (approved: boolean) => ({
  before: [{ id: 'x1', at: '2026-10-20T11:00:00Z', type: 'In', label: 'Entrada', source: 'App', changed: false }],
  after: [{ id: 'x1', at: '2026-10-20T11:00:00Z', type: 'In', label: 'Entrada', source: 'App', changed: false }, { id: approved ? 'x2' : null, at: '2026-10-20T20:50:00Z', type: 'Out', label: 'Saída', source: 'Manual', changed: true }],
  workedMinutesBefore: 0, workedMinutesAfter: 590, absenceMinutesAfter: null, fullDayAbsence: false,
})

async function noHorizontalScroll(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
}

test.describe('Ajustes do ponto (RH-01b)', () => {
  test('celular a 400 px: o funcionário pede a marcação esquecida e desconsidera a errada, e acompanha em Meus pedidos', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 800 })
    const posted: { body: Record<string, unknown>; key: string | null }[] = []
    const mine: unknown[] = []
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ hr: { enabled: false } }) }),
      'GET /api/v1/me/hr/link': (r: Route) => json(r, { name: 'Ana Souza', situation: 'Active' }),
      'GET /api/v1/me/hr/time-entries/today': (r: Route) => json(r, clock),
      'GET /api/v1/me/hr/time-entries': (r: Route) => json(r, [{ id: 'te0', at: '2026-10-20T10:12:00Z', day: '2026-10-20', type: 'In', source: 'App', deviceName: null, location: 'NotAsked', status: 'Valid', latitude: null, longitude: null, accuracyM: null }]),
      'GET /api/v1/me/hr/absence-reasons': (r: Route) => json(r, [{ code: 'MedicalCertificate', label: 'Atestado médico', appliesTo: 'Both', isHealth: true, justifies: true }]),
      'GET /api/v1/me/hr/time-adjustments': (r: Route) => json(r, mine),
      'POST /api/v1/me/hr/time-adjustments': (r: Route) => {
        const body = r.request().postDataJSON() as Record<string, unknown>
        posted.push({ body, key: r.request().headers()['idempotency-key'] ?? null })
        const saved = body.kind === 'VoidEntry'
          ? adjustment({ id: 'adj2', kind: 'VoidEntry', summary: 'Desconsiderar entrada às 07:12', justification: body.justification, canDecide: false, canCancel: true })
          : adjustment({ justification: body.justification, canDecide: false, canCancel: true })
        mine.unshift(saved)
        return json(r, saved, 201)
      },
    })
    await page.goto('/app/meu-ponto')
    await expect(page.getByRole('heading', { name: 'Meu ponto' })).toBeVisible()

    // O aviso "Esqueceu uma marcação?" abre o pedido.
    await page.getByRole('button', { name: 'Pedir ajuste' }).first().click()
    const dialog = page.getByRole('alertdialog', { name: 'Incluir marcação esquecida' })
    await expect(dialog).toBeVisible()
    await dialog.getByLabel('Data').fill('2026-10-20')
    await dialog.getByLabel('Hora').fill('17:50')
    await dialog.getByLabel('Tipo').selectOption('Out')
    await dialog.getByLabel('Justificativa').fill('curta')
    await dialog.getByRole('button', { name: 'Pedir ajuste' }).click()
    await expect(dialog.getByRole('alert')).toContainText('Escreva a justificativa com 10 a 200 caracteres.')
    await dialog.getByLabel('Justificativa').fill('Esqueci de marcar a saída')
    await noHorizontalScroll(page)
    await dialog.getByRole('button', { name: 'Pedir ajuste' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Pedido enviado para aprovação' }).first()).toBeVisible()
    expect(posted[0].body).toEqual({ kind: 'AddEntry', day: '2026-10-20', time: '17:50', entryType: 'Out', justification: 'Esqueci de marcar a saída' })
    expect(posted[0].body).not.toHaveProperty('employeeId')
    expect(posted[0].key).toMatch(/^[0-9a-f-]{36}$/)

    await page.getByRole('button', { name: 'Ver os últimos 60 dias' }).click()
    await page.getByRole('list', { name: 'Minhas marcações' }).getByRole('button', { name: 'Desconsiderar esta marcação' }).click()
    const drop = page.getByRole('alertdialog', { name: 'Desconsiderar esta marcação' })
    await expect(drop).toContainText('Desconsiderar entrada às 07:12')
    await drop.getByLabel('Justificativa').fill('Marquei a entrada duas vezes')
    await drop.getByRole('button', { name: 'Pedir ajuste' }).click()
    await expect(drop).toBeHidden()
    expect(posted[1].body).toEqual({ kind: 'VoidEntry', entryId: 'te0', justification: 'Marquei a entrada duas vezes' })
    expect(posted[1].key).not.toBe(posted[0].key)

    await page.getByRole('button', { name: 'Meus pedidos' }).click()
    const requests = page.getByRole('list', { name: 'Meus pedidos' })
    await expect(requests).toContainText('Incluir saída às 17:50')
    await expect(requests).toContainText('Aguardando aprovação')
    await expect(requests.getByRole('button', { name: 'Cancelar pedido' }).first()).toBeVisible()
    await noHorizontalScroll(page)
  })

  for (const width of [1280, 400]) {
    test(`Proprietário aprova no painel e o dia muda (${width} px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 })
      let approved = false
      const decisions: unknown[] = []
      await mockApi(page, {
        'GET /api/v1/hr/time-adjustments': (r: Route) => json(r, { items: approved ? [] : [adjustment()], truncated: false }),
        'GET /api/v1/hr/time-adjustments/adj1': (r: Route) => json(r, { adjustment: adjustment(approved ? { status: 'Approved', statusLabel: 'Aprovado', canDecide: false, decidedByName: 'Paula Dona', decidedAt: '2026-10-21T13:00:00Z' } : {}), impact: impact(approved) }),
        'POST /api/v1/hr/time-adjustments/adj1/approve': (r: Route) => { decisions.push(r.request().postDataJSON()); approved = true; return json(r, adjustment({ status: 'Approved' })) },
        'GET /api/v1/hr/employees': (r: Route) => json(r, { items: [{ id: 'e1', name: 'Ana Souza' }], page: 1, pageSize: 100, total: 1 }),
      })
      await page.goto('/app/rh/ponto/ajustes')
      await expect(page.getByRole('note')).toContainText(BANNER)
      const cards = page.getByRole('list', { name: 'Pedidos de ajuste' })
      await expect(cards).toContainText('Incluir saída às 17:50')
      await expect(cards).toContainText('Aguardando aprovação')
      await cards.getByRole('button', { name: 'Ver' }).click()
      await expect(page).toHaveURL(/pedido=adj1/)

      const panel = page.getByRole('dialog', { name: 'Pedido de ajuste' })
      await expect(panel.getByRole('region', { name: 'Antes' })).toContainText('0 h')
      await expect(panel.getByRole('region', { name: 'Depois' })).toContainText('9 h 50 min')
      await expect(panel).toContainText('Esqueci de marcar a saída')
      if (width === 400) await noHorizontalScroll(page)
      await panel.getByRole('button', { name: 'Aprovar ajuste' }).click()
      const confirm = page.getByRole('alertdialog', { name: 'Aprovar o ajuste de Ana Souza?' })
      await confirm.getByRole('button', { name: 'Aprovar', exact: true }).click()
      await expect(page.getByRole('status').filter({ hasText: 'Ajuste aprovado' }).first()).toBeVisible()
      expect(decisions).toEqual([{ version: 7 }])
      await expect(panel).toContainText('Aprovado')
      await expect(panel).toContainText('Decidido por Paula Dona')
      await expect(panel.getByRole('button', { name: 'Aprovar ajuste' })).toHaveCount(0)
    })
  }

  test('sem hr.view_health, o abono aparece só como Ausência justificada e o próprio pedido não é aprovado', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ hr: { enabled: true } }).map((m) => (m.code === 'hr' ? { ...m, actions: ['view', 'create', 'approve'] } : m)) }),
      'GET /api/v1/hr/time-adjustments': (r: Route) => json(r, {
        items: [
          adjustment({ id: 'adj3', kind: 'Absence', summary: 'Abonar 4 h: Ausência justificada', at: null, entryType: null, absenceMinutes: 240, absenceReasonLabel: 'Ausência justificada', justification: null, protected: true, hasDocument: true }),
          adjustment({ id: 'adj4', canDecide: false, requestedByName: 'Você' }),
        ], truncated: false,
      }),
      'GET /api/v1/hr/employees': (r: Route) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
    })
    await page.goto('/app/rh/ponto/ajustes')
    const cards = page.getByRole('list', { name: 'Pedidos de ajuste' })
    await expect(cards).toContainText('Abonar 4 h: Ausência justificada')
    await expect(cards).toContainText('Ausência justificada · Documento anexado')
    await expect(cards.getByRole('checkbox')).toHaveCount(1)
    await cards.getByRole('checkbox').check()
    await page.getByRole('button', { name: 'Aprovar selecionados (1)' }).click()
    await expect(page.getByRole('alertdialog', { name: 'Aprovar 1 ajuste?' })).toContainText('Abonar 4 h: Ausência justificada')
  })
})
