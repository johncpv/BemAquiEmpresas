import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const AGING = {
  today: '2026-10-20', total: 3700, count: 4, customers: 2,
  buckets: [{ label: '0–30', fromDays: 1, toDays: 30, total: 1000, count: 1 }, { label: '31–60', fromDays: 31, toDays: 60, total: 300, count: 1 }, { label: '61–90', fromDays: 61, toDays: 90, total: 2000, count: 1 }, { label: '90+', fromDays: 91, toDays: null, total: 400, count: 1 }],
  rows: [
    { partyId: 'c2', partyName: 'Beta Engenharia', buckets: [0, 0, 2000, 400], total: 2400, maxDaysLate: 120, count: 2 },
    { partyId: 'c1', partyName: 'Alfa Condomínio', buckets: [1000, 300, 0, 0], total: 1300, maxDaysLate: 35, count: 2 },
  ],
}

test.describe('Inadimplência (FIN-06a)', () => {
  test('faixas, clientes e ida aos títulos vencidos do cliente (CA 1 e 2)', async ({ page }) => {
    let listed = ''
    await mockApi(page, {
      'GET /api/v1/finance/aging': (r: Route) => json(r, AGING),
      'GET /api/v1/finance/receivables': (r: Route) => { listed = r.request().url(); return json(r, { page: { items: [], page: 1, pageSize: 20, total: 0 }, totals: { open: 0, overdue: 0 } }) },
    })
    await page.goto('/app/financeiro/inadimplencia')
    const main = page.getByRole('main')
    await expect(main.getByRole('region', { name: 'Faixas de atraso' })).toContainText('Mais de 90 dias')
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await main.getByRole('link', { name: 'Beta Engenharia' }).click()
    await expect(page).toHaveURL(/financeiro\/receber\?.*cliente=c2/)
    await expect.poll(() => listed).toContain('partyId=c2')
    expect(listed).toContain('situation=vencido')
  })

  test('sem vencidos, o vazio oferece ver a receber', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/finance/aging': (r: Route) => json(r, { ...AGING, total: 0, count: 0, customers: 0, rows: [] }) })
    await page.goto('/app/financeiro/inadimplencia')
    await expect(page.getByText('Nenhum título vencido. Bom sinal.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Ver a receber' })).toBeVisible()
  })
})
