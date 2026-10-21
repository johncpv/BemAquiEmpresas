import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'pay']

test.describe('Alçadas (FIN-03b)', () => {
  test('mostra a regra padrão, cria uma faixa e simula', async ({ page }) => {
    let body: unknown
    const rules = [{ id: 'r1', level: 1, minAmount: 0, maxAmount: null, accountId: null, accountLabel: null, approverGroupId: 'g1', approverUserId: null, approverName: 'Proprietário', isDefault: true }]
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ finance: { actions: ALL } as never }) }),
      'GET /api/v1/finance/approval-rules': (r: Route) => json(r, rules),
      'GET /api/v1/finance/approval-rules/approvers': (r: Route) => json(r, [{ id: 'g1', name: 'Proprietário', isGroup: true }, { id: 'u2', name: 'Bruno', isGroup: false }]),
      'GET /api/v1/finance/approval-rules/simulate': (r: Route) => json(r, { referenceAmount: 25000, splitWarning: null, levels: [{ level: 1, ruleId: 'r1', approverName: 'Proprietário', referenceAmount: 25000, splitWarning: null, status: 'Simulado' }, { level: 2, ruleId: 'r2', approverName: 'Bruno', referenceAmount: 25000, splitWarning: null, status: 'Simulado' }] }),
      'POST /api/v1/finance/approval-rules': (r: Route) => { body = r.request().postDataJSON(); return json(r, { ...rules[0], id: 'r2', level: 2, minAmount: 20000, approverName: 'Bruno', isDefault: false }) },
      'GET /api/v1/finance/chart-of-accounts': (r: Route) => json(r, []),
    })
    await page.goto('/app/financeiro/alcadas')
    await expect(page.getByText('Hoje o Proprietário aprova tudo.', { exact: false })).toBeVisible()
    await page.getByRole('button', { name: 'Nova regra de alçada' }).click()
    await page.getByRole('combobox', { name: 'Nível', exact: true }).selectOption('2')
    await page.getByLabel('A partir de', { exact: true }).fill('20000')
    await page.getByRole('combobox', { name: 'Aprovador' }).selectOption('u:u2')
    await page.getByRole('button', { name: 'Salvar regra de alçada' }).click()
    await expect(page.getByText('Regra de alçada salva')).toBeVisible()
    expect(body).toMatchObject({ level: 2, minAmount: 20000, approverUserId: 'u2', approverGroupId: null })
    await page.getByLabel('Valor da OP simulada').fill('25000')
    await expect(page.getByText('Nível 2: Bruno')).toBeVisible()
  })
})
