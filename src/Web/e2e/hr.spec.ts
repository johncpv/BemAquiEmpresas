import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const SCHEDULE = { id: 's44', name: '44 h — seg a sex 08:00–17:48', days: [1, 2, 3, 4, 5].map((weekday) => ({ weekday, start: '08:00', end: '17:48', breakMinutes: 60 })), weeklyHours: 44, monthlyHours: 220, isActive: true, seedKey: '44h-seg-sex' }

/** Backend em memória da CAD-04a (as regras reais estão nos testes de integração .NET). */
function hrApi() {
  const positions: Record<string, unknown>[] = []
  const schedules = [SCHEDULE]
  const requests: string[] = []
  const posted: unknown[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/hr/positions': (r) => { requests.push(r.request().url()); return json(r, { items: positions, page: 1, pageSize: 20, total: positions.length }) },
    'GET /api/v1/hr/positions/summary': (r) => json(r, { total: positions.length, calculated: positions.length, chargesPct: 80 }),
    'GET /api/v1/hr/positions/work-centers': (r) => json(r, [{ id: 'wc1', name: 'Montagem' }]),
    'GET /api/v1/hr/positions/*/sensitive': (r) => { requests.push(r.request().url()); return json(r, { id: 'p1', salaryMin: null, salaryMax: null, referenceSalary: null }) },
    'POST /api/v1/hr/positions': (r) => {
      const b = r.request().postDataJSON() as Record<string, unknown>
      posted.push(b)
      const p = { ...b, id: 'p1', defaultWorkCenterName: 'Montagem', workScheduleName: SCHEDULE.name, monthlyHours: b.monthlyHours ?? 220, hourlyCost: 0, hourlyCostManual: false, isActive: true, createdAt: '2026-10-19T12:00:00Z' }
      positions.push(p)
      return json(r, p, 201)
    },
    'PUT /api/v1/hr/positions/*/salary': (r) => { posted.push(r.request().postDataJSON()); positions[0].hourlyCost = 27; return json(r, positions[0]) },
    'GET /api/v1/hr/schedules': (r) => json(r, schedules),
    'POST /api/v1/hr/schedules': (r) => { const b = r.request().postDataJSON() as typeof SCHEDULE; posted.push(b); return json(r, { ...b, id: 's2', weeklyHours: 44, monthlyHours: 220, isActive: true, seedKey: null }, 201) },
  }
  return { api, requests, posted }
}

test.describe('RH (CAD-04a)', () => {
  test('cargo com jornada e faixa mostra a prévia de R$ 27,00 por hora (CA 1 a 4)', async ({ page }) => {
    const { api, posted } = hrApi()
    const modules = allModules().map((m) => (m.code === 'hr' ? { ...m, actions: [...m.actions, 'view_sensitive'] } : m))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '4', modules }) })
    await page.goto('/app/rh')
    await expect(page).toHaveURL(/\/app\/rh\/cargos$/)
    await page.getByRole('button', { name: 'Novo cargo' }).first().click()
    const panel = page.getByRole('dialog', { name: 'Novo cargo' })
    await panel.getByRole('textbox', { name: /^Nome/ }).fill('Montador')
    await panel.getByRole('combobox', { name: 'Jornada' }).selectOption('s44')
    await expect(panel.getByRole('textbox', { name: /Horas mensais/ })).toHaveValue('220,00')
    await panel.getByRole('button', { name: 'Salvar cargo' }).click()
    await expect(page.getByText('Cargo salvo')).toBeVisible()
    expect(posted[0]).toMatchObject({ name: 'Montador', workScheduleId: 's44', monthlyHours: 220 })

    const edit = page.getByRole('dialog', { name: 'Cargo Montador' })
    await edit.getByRole('textbox', { name: 'Salário mínimo' }).fill('3000')
    await edit.getByRole('textbox', { name: 'Salário máximo' }).fill('3600')
    await edit.getByRole('textbox', { name: 'Salário máximo' }).blur()
    await expect(edit.getByRole('textbox', { name: /Salário de referência/ })).toHaveValue('3.300,00')
    await expect(edit.getByText(/Custo-hora previsto: R\$\s27,00/)).toBeVisible()
    await edit.getByRole('button', { name: 'Salvar faixa salarial' }).click()
    await expect(page.getByText('Faixa salarial salva')).toBeVisible()
    expect(posted[1]).toEqual({ salaryMin: 3000, salaryMax: 3600, referenceSalary: null })
  })

  test('sem hr.view_sensitive a faixa nem é pedida à API (CA 10)', async ({ page }) => {
    const { api, requests } = hrApi()
    const modules = allModules().map((m) => (m.code === 'hr' ? { ...m, actions: ['view', 'create', 'edit'] } : m))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '3', modules }) })
    await page.goto('/app/rh/cargos')
    await page.getByRole('button', { name: 'Novo cargo' }).first().click()
    const panel = page.getByRole('dialog', { name: 'Novo cargo' })
    await panel.getByRole('textbox', { name: /^Nome/ }).fill('Soldador')
    await panel.getByRole('button', { name: 'Salvar cargo' }).click()
    await expect(page.getByRole('dialog', { name: 'Cargo Soldador' }).getByText('Dados protegidos')).toBeVisible()
    expect(requests.some((u) => u.includes('/sensitive'))).toBe(false)
  })

  test('jornada pelo teclado soma 44 h por semana (CA 15)', async ({ page }) => {
    const { api, posted } = hrApi()
    await mockApi(page, api)
    await page.goto('/app/rh/jornadas')
    await page.getByRole('button', { name: 'Nova jornada' }).first().focus()
    await page.keyboard.press('Enter')
    const panel = page.getByRole('dialog', { name: 'Nova jornada' })
    await panel.getByRole('textbox', { name: /^Nome/ }).focus()
    await page.keyboard.type('Comercial')
    await panel.getByRole('textbox', { name: 'Entrada do dia 1' }).focus()
    await page.keyboard.type('0800')
    await page.keyboard.press('Tab')
    await page.keyboard.type('1748')
    for (let i = 0; i < 4; i++) {
      await panel.getByRole('button', { name: 'Adicionar dia' }).focus()
      await page.keyboard.press('Enter')
    }

    await expect(panel.getByText(/Total: 44,00 h por semana · 220,00 h por mês/)).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await panel.getByRole('button', { name: 'Salvar jornada' }).focus()
    await page.keyboard.press('Enter')
    await expect(page.getByText('Jornada salva')).toBeVisible()
    expect((posted[0] as { days: unknown[] }).days).toHaveLength(5)
  })
})
