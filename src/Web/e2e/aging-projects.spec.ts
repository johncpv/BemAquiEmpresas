import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const buckets = [0, 30000, 0, 0]

test.describe('Inadimplência por projeto e pontualidade (FIN-06b)', () => {
  test('aba Por projeto e a pontualidade por cliente', async ({ page }, info) => {
    await mockApi(page, {
      'GET /api/v1/finance/aging': (r: Route) => json(r, { today: '2026-10-20', total: 30000, count: 1, customers: 1, buckets: [{ label: '0–30', fromDays: 1, toDays: 30, total: 0, count: 0 }, { label: '31–60', fromDays: 31, toDays: 60, total: 30000, count: 1 }, { label: '61–90', fromDays: 61, toDays: 90, total: 0, count: 0 }, { label: '90+', fromDays: 91, toDays: null, total: 0, count: 0 }],
        rows: [{ partyId: 'c1', partyName: 'Aurora', buckets, total: 30000, maxDaysLate: 40, count: 1, avgDelayDays: 12.5, pctPaidLate: 25 }] }),
      'GET /api/v1/finance/aging/projects': (r: Route) => json(r, { today: '2026-10-20', total: 30000, rows: [{ projectId: 'p1', projectNumber: 'PRJ-2026-0001', projectName: 'Aurora — Elevador', partyId: 'c1', partyName: 'Aurora', buckets, total: 30000, maxDaysLate: 40, count: 1 }] }),
    })
    await page.goto('/app/financeiro/inadimplencia')
    if (info.project.name !== 'celular') await expect(page.getByText('12,5 dias')).toBeVisible()
    await page.getByRole('tab', { name: 'Por projeto' }).click()
    await expect(page.getByRole('link', { name: 'PRJ-2026-0001' })).toBeVisible()
  })

  test('terceiro com o bloco de títulos e pontualidade', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/parties/c1': (r: Route) => json(r, { id: 'c1', kind: 'PJ', legalName: 'Aurora Ltda', tradeName: 'Aurora', document: null, roles: ['Customer'], isActive: true, addresses: [], contacts: [], version: 1 }),
      'GET /api/v1/quotes': (r: Route) => json(r, { items: [], page: 1, pageSize: 10, total: 0 }),
      'GET /api/v1/projects': (r: Route) => json(r, { items: [], page: 1, pageSize: 10, total: 0 }),
      'GET /api/v1/finance/parties/c1/summary': (r: Route) => json(r, { receivables: { open: 70000, overdue: 30000, paidLast12m: 10000 }, payables: { open: 0, overdue: 0, paidLast12m: 0 }, paymentStats: { avgDelayDays: 12.5, pctPaidLate: 25, pctOverdueNow: 42.9, last12mInvoiced: 100000, hasHistory: true, tenantAvgDelayDays: 3 } }),
    })
    await page.goto('/app/cadastros/terceiros/c1?aba=comercial-financeiro')
    await expect(page.getByText('Sem orçamentos para este terceiro.')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Títulos e pontualidade' }).getByText('12,5 dias')).toBeVisible()
  })
})
