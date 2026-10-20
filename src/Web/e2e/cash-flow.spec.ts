import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const iso = (offset: number) => { const d = new Date(Date.now() + offset * 86_400_000); return d.toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' }) }

function flow(view: string) {
  const days = Array.from({ length: 12 }, (_, i) => i - 2)
  let balance = 1000
  const daily = days.map((o) => {
    const inflow = o === 0 ? 500 : 0
    const outflow = o === 3 ? 2000 : 0
    balance += inflow - outflow
    return { start: iso(o), end: iso(o), inflow, outflow, balance, projected: o >= 0, containsToday: o === 0, overdueInflow: o === 0 ? 500 : 0, overdueOutflow: 0 }
  })
  const periods = view === 'daily' ? daily : [{ ...daily[0], end: daily[11].end, inflow: 500, outflow: 2000, balance, containsToday: true, overdueInflow: 500 }]
  return {
    today: iso(0), from: iso(-2), to: iso(9), view, bankAccountId: null, currentBalance: 1000, consolidated: true, periods,
    lowest: { date: iso(3), balance: -500 }, firstNegative: { date: iso(3), balance: -500 }, undatedReceivables: { count: 1, total: 4000 },
  }
}

const api: Record<string, (r: Route) => Promise<void> | void> = {
  'GET /api/v1/finance/cash-flow': (r) => json(r, flow(new URL(r.request().url()).searchParams.get('view') ?? 'daily')),
  'GET /api/v1/finance/cash-flow/day': (r) => json(r, { date: iso(3), projected: true, items: [
    { kind: 'Payable', id: 'p1', number: 'CP-2026-00007', description: 'Chapas de aço', partyName: 'Gama Aços', amount: -2000, dueDate: iso(3), overdue: false, estimate: true, link: '/app/financeiro/pagar/p1' },
  ] }),
  'GET /api/v1/finance/bank-accounts': (r) => json(r, { items: [{ id: 'b1', name: 'Banco Inter', kind: 'Bank', isActive: true }], page: 1, pageSize: 100, total: 1 }),
}

test.describe('Fluxo de caixa (FIN-05)', () => {
  test('alerta de caixa negativo, troca de visão e detalhe do dia (CA 7, 8 e 10)', async ({ page }) => {
    await mockApi(page, api)
    await page.goto('/app/financeiro/fluxo-de-caixa')
    const main = page.getByRole('main')
    await expect(main.getByRole('alert')).toContainText('O caixa fica negativo em')
    await expect(main.getByRole('table', { name: 'Fluxo de caixa por período' }).getByText('Vencidos em aberto')).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    await main.getByRole('button', { name: 'Ver o que vence nesse dia' }).click()
    const panel = page.getByRole('dialog')
    await expect(panel.getByRole('link', { name: /CP-2026-00007/ })).toBeVisible()
    await expect(panel.getByText('Estimado')).toBeVisible()
    await page.keyboard.press('Escape')

    await main.getByRole('combobox', { name: 'Visão' }).selectOption('semanal')
    await expect(page).toHaveURL(/visao=semanal/)
    await expect(main.getByRole('button', { name: /^Ver o dia/ })).toHaveCount(0)
  })

  test('a 400 px nada transborda', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    await mockApi(page, api)
    await page.goto('/app/financeiro/fluxo-de-caixa')
    await expect(page.getByRole('table', { name: 'Fluxo de caixa por período' })).toBeVisible()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
