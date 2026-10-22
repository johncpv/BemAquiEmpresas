import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

interface Center { id: string; name: string; line: string | null; shifts: { start: string; end: string }[]; hoursPerDay: number; calendarId: string; isActive: boolean; createdAt: string }
interface Exception { id: string; calendarId: string; date: string; type: 'Holiday' | 'Stoppage'; hoursAvailable: number; description: string }

const CALENDAR = { id: '0199f0e0-0000-7000-8000-0000000000c9', name: 'Calendário padrão', isDefault: true, workingDays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'] }
const page1 = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })
const minutes = (t: string) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3))

/** Backend em memória da CAD-03 (as regras reais estão nos testes de integração .NET). */
function productionApi() {
  const centers: Center[] = []
  const exceptions: Exception[] = []
  const locations = [{ id: '0199f0e0-0000-7000-8000-0000000000a1', code: 'ALM', name: 'Almoxarifado principal', isActive: true, isDefault: true, seedKey: 'alm', createdAt: '2026-10-19T12:00:00Z' }]
  const posted: unknown[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/production/work-centers': (r) => json(r, page1(centers)),
    'GET /api/v1/production/work-centers/suggested-shifts': (r) => json(r, centers.at(-1)?.shifts ?? []),
    'POST /api/v1/production/work-centers': (r) => {
      const b = r.request().postDataJSON() as Center
      posted.push(b)
      const c: Center = { ...b, id: crypto.randomUUID(), hoursPerDay: Math.round(b.shifts.reduce((s, x) => s + minutes(x.end) - minutes(x.start), 0) / 0.6) / 100, calendarId: CALENDAR.id, isActive: true, createdAt: '2026-10-19T12:00:00Z' }
      centers.push(c)
      return json(r, c, 201)
    },
    'GET /api/v1/production/calendars/default': (r) => json(r, CALENDAR),
    'GET /api/v1/production/calendars/*/exceptions': (r) => json(r, exceptions),
    'GET /api/v1/production/calendars/*/holidays/*': (r) => json(r, [
      { date: '2026-02-17', name: 'Carnaval (terça-feira)', optional: true, alreadyAdded: false },
      { date: '2026-04-03', name: 'Sexta-feira Santa', optional: false, alreadyAdded: false },
      { date: '2026-11-20', name: 'Dia Nacional de Zumbi e da Consciência Negra', optional: false, alreadyAdded: false },
    ]),
    'POST /api/v1/production/calendars/*/holidays': (r) => {
      const b = r.request().postDataJSON() as { holidays: { date: string; description: string }[] }
      posted.push(b)
      for (const h of b.holidays) exceptions.push({ id: crypto.randomUUID(), calendarId: CALENDAR.id, date: h.date, type: 'Holiday', hoursAvailable: 0, description: h.description })
      return json(r, { added: b.holidays.length, skipped: 0 })
    },
    'GET /api/v1/inventory/locations': (r) => json(r, page1(locations)),
  }
  return { api, posted }
}

async function noSeriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
}

test.describe('produção e estoque (CAD-03)', () => {
  test('cadastra centro com dois turnos só pelo teclado e vê 8,80 h (CA 12)', async ({ page }) => {
    const { api, posted } = productionApi()
    await mockApi(page, api)
    await page.goto('/app/producao')
    await expect(page).toHaveURL(/\/app\/producao\/os$/)
    await page.goto('/app/producao/centros-de-trabalho')
    await expect(page.getByText('Cadastre os centros de trabalho da fábrica, como Corte e Montagem, com os turnos de cada um.')).toBeVisible()
    await page.getByRole('button', { name: 'Novo centro de trabalho' }).first().focus()
    await page.keyboard.press('Enter')
    const panel = page.getByRole('dialog', { name: 'Novo centro de trabalho' })
    await expect(panel).toBeVisible()
    await panel.getByRole('textbox', { name: /Nome/ }).focus()
    await page.keyboard.type('Corte')
    await panel.getByRole('textbox', { name: 'Início do turno 1' }).focus()
    await page.keyboard.type('0700')
    await page.keyboard.press('Tab')
    await page.keyboard.type('1100')
    await panel.getByRole('button', { name: 'Adicionar turno' }).focus()
    await page.keyboard.press('Enter')
    await panel.getByRole('textbox', { name: 'Início do turno 2' }).focus()
    await page.keyboard.type('1200')
    await page.keyboard.press('Tab')
    await page.keyboard.type('1648')
    await expect(panel.getByText('8,80 h')).toBeVisible()
    await noSeriousViolations(page)
    await panel.getByRole('button', { name: 'Salvar centro de trabalho' }).focus()
    await page.keyboard.press('Enter')
    await expect(page.getByText('Centro de trabalho salvo')).toBeVisible()
    expect(posted[0]).toEqual({ name: 'Corte', line: null, shifts: [{ start: '07:00', end: '11:00' }, { start: '12:00', end: '16:48' }] })
    await expect(page.getByRole('button', { name: 'Corte' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Cadastrar o calendário' })).toBeVisible()
  })

  test('turno que vira o dia é avisado antes de salvar (CA 13)', async ({ page }) => {
    const { api, posted } = productionApi()
    await mockApi(page, api)
    await page.goto('/app/producao/centros-de-trabalho')
    await page.getByRole('button', { name: 'Novo centro de trabalho' }).first().click()
    const panel = page.getByRole('dialog', { name: 'Novo centro de trabalho' })
    await panel.getByRole('textbox', { name: /Nome/ }).fill('Solda')
    await panel.getByRole('textbox', { name: 'Início do turno 1' }).fill('2200')
    await panel.getByRole('textbox', { name: 'Fim do turno 1' }).fill('0600')
    await panel.getByRole('button', { name: 'Salvar centro de trabalho' }).click()
    await expect(panel.getByRole('alert')).toHaveText('Turno que vira o dia não é aceito.')
    expect(posted).toHaveLength(0)
  })

  test('sugere feriados nacionais com os facultativos desmarcados e só grava o confirmado (CA 10)', async ({ page }) => {
    const { api, posted } = productionApi()
    await mockApi(page, api)
    await page.goto('/app/producao/calendario')
    await expect(page.getByRole('checkbox', { name: /Seg/ })).toBeChecked()
    await expect(page.getByText(/Nenhum feriado cadastrado em/)).toBeVisible()
    await page.getByRole('button', { name: /Sugerir feriados nacionais de/ }).click()
    const panel = page.getByRole('dialog', { name: /Feriados nacionais de/ })
    await expect(panel.getByRole('checkbox', { name: /Sexta-feira Santa/ })).toBeChecked()
    await expect(panel.getByRole('checkbox', { name: /Carnaval/ })).not.toBeChecked()
    await panel.getByRole('checkbox', { name: /Consciência Negra/ }).uncheck()
    expect(posted).toHaveLength(0)
    await noSeriousViolations(page)
    await panel.getByRole('button', { name: 'Adicionar feriados' }).click()
    await expect(page.getByText('1 feriado adicionado')).toBeVisible()
    expect(posted[0]).toEqual({ holidays: [{ date: '2026-04-03', description: 'Sexta-feira Santa' }] })
    await expect(page.getByRole('button', { name: '03/04/2026', exact: true })).toBeVisible()
  })

  test('Locais mostra o ALM padrão (CA 3)', async ({ page }) => {
    const { api } = productionApi()
    await mockApi(page, api)
    await page.goto('/app/estoque/locais')
    await expect(page.getByText('Onde o material fica guardado.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'ALM' })).toBeVisible()
    await expect(page.getByText('Padrão', { exact: true })).toBeVisible()
    await noSeriousViolations(page)
  })
})
