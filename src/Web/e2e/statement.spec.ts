import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const payer = allModules().map((m) => (m.code === 'finance' ? { ...m, actions: [...m.actions, 'pay'] } : m))
const line = (id: string, amount: number, balance: number, extra: Record<string, unknown> = {}) => ({
  transactionId: id, date: '2026-11-05', description: 'Movimento ' + id, type: 'Receipt', origin: 'Título a receber', link: '/app/financeiro/receber/r1', amount, balance,
  withoutOrder: false, transferId: null, bankEntryId: null, isReversal: false, reversed: false, movementVersion: null, ...extra,
})

function statementApi() {
  const posted: unknown[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: false, version: '9', modules: payer }),
    'GET /api/v1/finance/bank-accounts/*/statement': (r) => json(r, {
      accountId: 'b1', accountName: 'Banco Inter', from: '2026-11-01', to: '2026-11-30', openingMissing: false, hasMovement: true, startLabel: 'Saldo inicial', startBalance: 10000, endBalance: 9212.5,
      lines: { items: [line('t1', 425, 10425), line('t2', -1200, 9225, { origin: 'Ordem de pagamento', link: '/app/financeiro/ordens-de-pagamento/op1' }),
        line('t3', -12.5, 9212.5, { type: 'BankEntry', origin: 'Lançamento sem OP', link: null, withoutOrder: true, bankEntryId: 'e1', movementVersion: 3 })], page: 1, pageSize: 50, total: 3 },
    }),
    'GET /api/v1/finance/bank-accounts/balances': (r) => json(r, { accounts: [{ id: 'b1', name: 'Banco Inter', currentBalance: 9212.5, openingMissing: false }, { id: 'b2', name: 'Caixa', currentBalance: 100, openingMissing: false }], consolidated: 9312.5 }),
    'GET /api/v1/finance/chart-of-accounts': (r) => json(r, [{ id: 'x5', code: '3.5', name: 'Financeiras', type: 'Expense', dreGroup: 'FinancialResult', parentId: null, acceptsEntries: true, isActive: true, seedKey: '3.5' }]),
    'GET /api/v1/finance/cost-centers': (r) => json(r, []),
    'POST /api/v1/finance/transfers': (r) => { posted.push(r.request().postDataJSON()); return json(r, { id: 'tr1', fromAccountId: 'b1', toAccountId: 'b2', amount: 50, date: '2026-11-10', description: 'x', reversed: false, reversalReason: null, version: 1 }, 201) },
    'POST /api/v1/finance/bank-entries/*/reverse': (r) => { posted.push(r.request().postDataJSON()); return json(r, {}) },
  }
  return { api, posted }
}

test.describe('Extrato da conta (FIN-04)', () => {
  test('saldo inicial, acumulado, origem e Sem OP; transferir e estornar lançamento (CA 1, 5, 12 e 13)', async ({ page }, info) => {
    test.skip(info.project.name === 'celular')
    const { api, posted } = statementApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/contas/b1/extrato')
    const table = page.getByRole('table', { name: 'Extrato da conta' })
    await expect(table.getByText('Saldo inicial')).toBeVisible()
    await expect(table.getByRole('link', { name: 'Ordem de pagamento' })).toBeVisible()
    await expect(table.getByText('Sem OP', { exact: true })).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    await page.getByRole('button', { name: 'Transferir', exact: true }).click()
    const dialog = page.getByRole('alertdialog')
    await dialog.getByRole('combobox', { name: 'Destino' }).selectOption('b2')
    await dialog.getByRole('textbox', { name: 'Valor' }).fill('50')
    await dialog.getByRole('textbox', { name: 'Valor' }).blur()
    await dialog.getByRole('button', { name: 'Transferir', exact: true }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Transferência registrada' }).first()).toBeVisible()
    expect(posted[0]).toMatchObject({ fromAccountId: 'b1', toAccountId: 'b2', amount: 50 })
    await expect(page.getByRole('region', { name: 'Próximos passos' })).toContainText('Ver extrato do destino')

    await table.getByRole('button', { name: 'Estornar' }).click()
    const reverse = page.getByRole('alertdialog')
    await reverse.getByRole('textbox', { name: 'Motivo' }).fill('Tarifa devolvida')
    await reverse.getByRole('button', { name: 'Estornar lançamento' }).click()
    await expect.poll(() => posted.length).toBe(2)
    expect(posted[1]).toMatchObject({ reason: 'Tarifa devolvida', version: 3 })
  })

  test('a 400 px nada transborda', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    const { api } = statementApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/contas/b1/extrato')
    await expect(page.getByRole('table', { name: 'Extrato da conta' })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
  })
})
