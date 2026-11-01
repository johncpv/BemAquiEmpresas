import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

/** RH-01c: fechar o período com uma pendência resolvida no caminho, o funcionário confirmar no celular e o espelho a 400 px. */
const BANNER = 'Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).'

const totals = (over: Record<string, unknown> = {}) => ({
  expectedMinutes: 11088, workedMinutes: 10120, overtimeMinutes: 120, absenceMinutes: 528, lateMinutes: 318, justifiedMinutes: 240, compensatedMinutes: 0,
  workedDays: 20, absenceDays: 1, inconsistentDays: 1, bankApplies: false, bankMonthMinutes: null, bankBalanceMinutes: null, bankText: null, ...over,
})

const day = (d: string, over: Record<string, unknown> = {}) => ({
  day: d, status: 'Normal', statusLabel: 'Normal', expectedMinutes: 528, workedMinutes: 528, overtimeMinutes: 0, lateMinutes: 0, absenceMinutes: 0, justifiedMinutes: 0,
  compensatedMinutes: 0, bankMinutes: null, issues: [], reviewed: false, reviewComment: null, holiday: null, leaveLabel: null, absences: [],
  entries: ['08:00', '12:00', '13:00', '17:48'].map((t, i) => ({ id: `${d}-${i}`, time: t, type: ['In', 'BreakStart', 'BreakEnd', 'Out'][i], typeLabel: 'Entrada', source: 'App' })),
  ...over,
})

const sheet = (closed: boolean, over: Record<string, unknown> = {}) => ({
  employeeId: 'e1', employeeName: 'Ana Souza', registration: '001', positionName: 'Montadora', period: '2026-10-01', start: '2026-10-01', end: '2026-10-31',
  status: closed ? 'Closed' : 'Open', closedAt: closed ? '2026-11-03T12:00:00Z' : null, closedByName: closed ? 'Paula Dona' : null,
  closedBanner: closed ? 'Período fechado em 03/11/2026 por Paula Dona' : null, reopenedAt: null, reopenReason: null, employeeAckAt: null, employeeAckSummary: null,
  banner: BANNER, scheduleText: 'seg a sex 08:00–17:48, intervalo 1 h', hasEntries: true, pendingAdjustments: 0, totals: totals(),
  days: [
    day('2026-10-05', { status: 'Overtime', statusLabel: 'Extra', workedMinutes: 648, overtimeMinutes: 120 }),
    day('2026-10-07', { status: 'Absent', statusLabel: 'Falta', workedMinutes: 0, absenceMinutes: 528, entries: [] }),
    day('2026-10-08', { status: 'Inconsistent', statusLabel: 'Inconsistente', workedMinutes: 240, lateMinutes: 288, issues: ['Falta a saída'], entries: [] }),
    day('2026-10-12', { status: 'Holiday', statusLabel: 'Feriado', expectedMinutes: 0, workedMinutes: 0, holiday: 'Nossa Senhora Aparecida', entries: [] }),
  ],
  ...over,
})

async function noHorizontalScroll(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
}

test.describe('Espelho de ponto (RH-01c)', () => {
  test('o RH fecha o período depois de revisar o dia inconsistente e recebe os próximos passos', async ({ page }) => {
    let reviewed = false
    let closed = false
    const reviews: unknown[] = []
    const overview = () => ({
      period: '2026-10-01', start: '2026-10-01', end: '2026-10-31', suggestedPeriod: '2026-10-01', status: closed ? 'Closed' : 'Open', closedAt: null, closedByName: null,
      closedBanner: closed ? 'Período fechado em 03/11/2026 por Paula Dona' : null, closable: !closed, banner: BANNER,
      rows: [{ employeeId: 'e1', name: 'Ana Souza', registration: '001', status: closed ? 'Closed' : 'Open', expectedMinutes: 11088, workedMinutes: 10120, overtimeMinutes: 120, absenceMinutes: 528,
        lateMinutes: 318, justifiedMinutes: 240, bankMonthMinutes: null, bankBalanceMinutes: null, inconsistentDays: reviewed ? 0 : 1, pendingAdjustments: 0, employeeAckAt: null,
        ackLabel: closed ? 'Aguardando o funcionário' : 'Período em aberto', situationLabel: closed ? 'Fechado' : reviewed ? '1 falta' : '1 dia inconsistente', situationTone: closed ? 'info' : reviewed ? 'critical' : 'alert' }],
    })
    await mockApi(page, {
      'GET /api/v1/hr/timesheets': (r: Route) => json(r, overview()),
      'GET /api/v1/hr/timesheets/2026-10/close-check': (r: Route) => json(r, {
        period: '2026-10-01', start: '2026-10-01', end: '2026-10-31', canClose: reviewed, blockers: [], employeeCount: 1,
        employees: reviewed ? [] : [{ employeeId: 'e1', name: 'Ana Souza', pendingAdjustments: 0, inconsistentDays: ['2026-10-08'] }],
      }),
      'GET /api/v1/hr/employees/e1/timesheets/2026-10': (r: Route) => json(r, sheet(false, reviewed ? { days: sheet(false).days.map((d) => (d.day === '2026-10-08' ? { ...d, reviewed: true, reviewComment: 'Saída conferida' } : d)) } : {})),
      'POST /api/v1/hr/employees/e1/timesheets/reviews': (r: Route) => { reviews.push(r.request().postDataJSON()); reviewed = true; return json(r, day('2026-10-08', { status: 'Inconsistent', reviewed: true })) },
      'POST /api/v1/hr/timesheets/2026-10/close': (r: Route) => { closed = true; return json(r, { period: '2026-10-01', closed: 1, message: 'Período outubro de 2026 fechado' }) },
    })
    await page.goto('/app/rh/ponto/espelho?mes=2026-10')
    await expect(page.getByRole('note')).toContainText(BANNER)
    await expect(page.getByRole('table')).toContainText('1 dia inconsistente')

    await page.getByRole('button', { name: 'Fechar o período outubro de 2026' }).click()
    const dialog = page.getByRole('alertdialog', { name: 'Fechar o período outubro de 2026?' })
    await expect(dialog).toContainText('Ana Souza: 1 dia inconsistente')
    await dialog.getByRole('button', { name: 'Abrir o espelho' }).click()
    await expect(page).toHaveURL(/espelho\/e1/)

    await page.getByRole('button', { name: 'Marcar como revisado' }).first().click()
    const review = page.getByRole('alertdialog', { name: /como revisado/ })
    await review.getByLabel('Comentário').fill('Saída conferida')
    await review.getByRole('button', { name: 'Marcar como revisado' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Dia revisado' }).first()).toBeVisible()
    expect(reviews).toEqual([{ day: '2026-10-08', comment: 'Saída conferida' }])

    await page.getByRole('link', { name: 'Voltar ao espelho geral' }).click()
    await page.getByRole('button', { name: 'Fechar o período outubro de 2026' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Fechar período' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Período outubro de 2026 fechado' }).first()).toBeVisible()
    const steps = page.getByRole('region', { name: 'Próximos passos' })
    await expect(steps.getByRole('button', { name: 'Baixar espelhos em PDF' })).toBeVisible()
    await expect(steps.getByRole('button', { name: 'Lançar a folha de outubro de 2026' })).toBeVisible()
    await expect(steps.getByRole('button', { name: 'Gerar pacote para a contadora' })).toBeVisible()
  })

  test('celular a 400 px: o funcionário abre o Meu espelho fechado e confirma', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 800 })
    let acked: string | null = null
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ hr: { enabled: false } }) }),
      'GET /api/v1/me/hr/link': (r: Route) => json(r, { name: 'Ana Souza', situation: 'Active' }),
      'GET /api/v1/me/hr/time-entries/today': (r: Route) => json(r, {
        banner: BANNER, firstName: 'Ana', serverNow: '2026-11-04T15:02:00Z', today: '2026-11-04', schedule: '08:00–17:48, intervalo 1 h', entries: [], suggested: 'In',
        suggestedVerb: 'Registrar entrada', outOfSequence: false, hint: '', geolocation: 'Off', geoConsentNeeded: false, geoConsentText: '', geoConsentVersion: '2026-10-27', registeredDevice: false,
      }),
      'GET /api/v1/me/hr/time-adjustments': (r: Route) => json(r, []),
      'GET /api/v1/me/hr/timesheets': (r: Route) => json(r, [{ period: '2026-10-01', start: '2026-10-01', end: '2026-10-31', status: 'Closed', closedAt: '2026-11-03T12:00:00Z', employeeAckAt: acked }]),
      'GET /api/v1/me/hr/timesheets/2026-10': (r: Route) => json(r, sheet(true, { employeeAckAt: acked })),
      'POST /api/v1/me/hr/timesheets/2026-10/ack': (r: Route) => { acked = '2026-11-04T15:03:00Z'; return json(r, { period: '2026-10-01', at: acked, summary: 'Esperado 184 h 48 min' }) },
    })
    await page.goto('/app/meu-ponto?aba=espelho')
    const section = page.getByRole('region', { name: 'Meu espelho' })
    await expect(section).toContainText('Período fechado em 03/11/2026 por Paula Dona')
    await expect(section.getByRole('list', { name: 'Dias do espelho de Ana Souza' })).toContainText('Falta a saída')
    await noHorizontalScroll(page)
    await section.getByRole('button', { name: 'Li e confirmo' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Li e confirmo' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Confirmado' }).first()).toBeVisible()
    await expect(section).toContainText('Confirmado em')
  })
})
