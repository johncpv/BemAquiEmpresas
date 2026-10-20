import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const ACCOUNTS = [
  { id: 'a1', code: '1.1', name: 'Venda de equipamentos', type: 'Revenue', dreGroup: 'GrossRevenue', parentId: null, acceptsEntries: true, isActive: true, seedKey: '1.1' },
  { id: 'a2', code: '1.9', name: 'Receitas a classificar', type: 'Revenue', dreGroup: 'GrossRevenue', parentId: null, acceptsEntries: true, isActive: true, seedKey: '1.9' },
]
const BANK = { id: 'b1', name: 'Inter PJ', kind: 'Bank', bankCode: '077', bankName: 'Banco Inter', agency: '1', account: '1', openingBalance: null, openingDate: null, isActive: true, isDefault: true, importSource: null, hasMovement: false }
const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

type R = Record<string, unknown> & { id: string; amount: number; paidAmount: number; balance: number; status: string; version: number }

/** Backend em memória da FIN-01 (as regras reais estão nos testes de integração .NET). */
function receivablesApi() {
  const receivables: R[] = []
  const settlements: Record<string, unknown>[] = []
  const posted: { url: string; body: unknown; headers: Record<string, string> }[] = []
  const make = (i: number, b: Record<string, unknown>, amount: number, due: string, description: string): R => ({
    id: 'r' + i, number: `CR-2026-0000${i}`, partyId: 'c1', partyName: 'Kappa Ltda', description, accountId: 'a1', accountName: '1.1 Venda de equipamentos', costCenterId: null, projectId: null,
    amount, paidAmount: 0, balance: amount, interest: 0, fine: 0, discount: 0, issueDate: b.issueDate, dueDate: due, status: 'Open', isOverdue: false, isDueToday: false,
    origin: 'Manual', originId: null, installmentGroupId: null, cancelReason: null, cancelledAt: null, version: 1,
  })
  const find = (r: Route) => receivables.find((x) => r.request().url().includes('/' + x.id))!
  const record = (r: Route) => posted.push({ url: new URL(r.request().url()).pathname, body: r.request().postDataJSON(), headers: r.request().headers() })
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/finance/chart-of-accounts': (r) => json(r, ACCOUNTS),
    'GET /api/v1/finance/cost-centers': (r) => json(r, []),
    'GET /api/v1/finance/payment-terms': (r) => json(r, { items: [{ id: 't3', name: '30/60/90 dias', isActive: true, installments: [], seedKey: '30-60-90-dias', importSource: null, inUse: false }], page: 1, pageSize: 100, total: 1 }),
    'GET /api/v1/finance/bank-accounts': (r) => json(r, { items: [BANK], page: 1, pageSize: 100, total: 1 }),
    'GET /api/v1/finance/parameters': (r) => json(r, { discountApprovalPct: 5, discountApprovalAmount: 500, cancelApprovalAmount: 1000, backdateDays: 7 }),
    'GET /api/v1/finance/customers': (r) => json(r, [{ id: 'c1', name: 'Kappa Ltda', document: '12345678000190', defaultPaymentTermId: 't3' }]),
    'POST /api/v1/finance/receivables/split-preview': (r) => json(r, [{ number: 1, dueDate: '2026-12-10', amount: 333.33 }, { number: 2, dueDate: '2027-01-09', amount: 333.33 }, { number: 3, dueDate: '2027-02-08', amount: 333.34 }]),
    'POST /api/v1/finance/receivables/installments': (r) => {
      record(r)
      const b = r.request().postDataJSON() as Record<string, unknown>
      const items = [[333.33, '2026-12-10'], [333.33, '2027-01-09'], [333.34, '2027-02-08']].map(([a, d], k) => make(receivables.length + k + 1, b, a as number, d as string, `${b.description} (${k + 1}/3)`))
      receivables.push(...items)
      return json(r, { items }, 201)
    },
    'GET /api/v1/finance/receivables': (r) => json(r, { page: { items: receivables, page: 1, pageSize: 20, total: receivables.length }, totals: { open: receivables.reduce((s, x) => s + x.balance, 0), overdue: 0 } }),
    'GET /api/v1/finance/receivables/*': (r) => json(r, find(r)),
    'GET /api/v1/finance/receivables/*/settlements': (r) => json(r, settlements),
    'POST /api/v1/finance/receivables/*/settlements': (r) => {
      record(r)
      const x = find(r)
      const b = r.request().postDataJSON() as { principal: number; interest: number; fine: number; discount: number }
      x.paidAmount = Math.round((x.paidAmount + b.principal) * 100) / 100
      x.balance = Math.round((x.amount - x.paidAmount) * 100) / 100
      x.status = x.balance <= 0 ? 'Paid' : 'PartiallyPaid'
      x.version += 1
      settlements.unshift({ id: 's' + settlements.length, kind: 'Settlement', bankAccountId: 'b1', bankAccountName: 'Inter PJ', date: today(), principal: b.principal, interest: b.interest, fine: b.fine, discount: b.discount, total: b.principal + b.interest + b.fine - b.discount, backdateReason: null, createdBy: 'u1', createdAt: new Date().toISOString(), reversedAt: null, reversalReason: null })
      return json(r, x)
    },
  }
  return { api, posted, receivables }
}

async function press(page: Page, name: string | RegExp, scope = page.locator('body')) {
  await scope.getByRole('button', { name, exact: typeof name === 'string' }).first().focus()
  await page.keyboard.press('Enter')
}

test.describe('Contas a receber (FIN-01)', () => {
  test('parcela pela condição do cliente e dá baixa parcial só pelo teclado (CA 3, 5, 8 e 9)', async ({ page }) => {
    const { api, posted } = receivablesApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/receber')
    await expect(page.getByText('Lance o primeiro título a receber ou aprove um orçamento para gerar as parcelas do contrato.')).toBeVisible()
    await page.getByRole('link', { name: 'Novo título a receber' }).first().focus()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/app\/financeiro\/receber\/novo$/)

    const main = page.getByRole('main')
    await main.getByRole('combobox', { name: /^Cliente/ }).click()
    await page.getByPlaceholder('Digite para buscar').fill('Kappa')
    await page.getByRole('option', { name: /Kappa Ltda/ }).click()
    await main.getByRole('textbox', { name: /^Descrição/ }).focus()
    await page.keyboard.type('Elevador')
    await main.getByRole('textbox', { name: /^Valor/ }).focus()
    await page.keyboard.type('1000')
    await main.getByRole('radio', { name: 'Parcelar pela condição' }).focus()
    await page.keyboard.press('Space')
    await expect(main.getByRole('combobox', { name: /Condição de pagamento/ })).toHaveValue('t3')
    const preview = main.getByRole('table', { name: 'Parcelas que serão lançadas' })
    await expect(preview).toContainText('333,34')
    await press(page, 'Salvar títulos a receber', main)
    await expect(page.getByRole('status').filter({ hasText: '3 títulos a receber salvos' })).toBeVisible()
    expect(posted[0].headers['idempotency-key']).toBeTruthy()
    expect(posted[0].body).toMatchObject({ partyId: 'c1', accountId: 'a1', amount: 1000, paymentTermId: 't3' })

    await expect(page).toHaveURL(/\/app\/financeiro\/receber\/r1/)
    await expect(page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Dar baixa' })).toBeVisible()
    await press(page, 'Dar baixa', main)
    const dialog = page.getByRole('alertdialog', { name: /Dar baixa no título CR-2026-00001/ })
    await expect(dialog.getByRole('textbox', { name: /^Principal/ })).toHaveValue('333,33')
    await dialog.getByRole('textbox', { name: /^Principal/ }).focus()
    await page.keyboard.press('Control+A')
    await page.keyboard.type('100')
    await dialog.getByRole('textbox', { name: /^Juros/ }).focus()
    await page.keyboard.type('5')
    await expect(dialog.getByRole('group', { name: 'Impacto' })).toContainText(/Saldo depois\s*R\$\s233,33/)
    await expect(dialog.getByRole('group', { name: 'Impacto' })).toContainText(/Entra na conta\s*R\$\s105,00/)
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await press(page, 'Dar baixa', dialog)
    await expect(page.getByRole('status').filter({ hasText: 'Baixa registrada' })).toBeVisible()
    expect(posted[1].body).toMatchObject({ bankAccountId: 'b1', principal: 100, interest: 5, version: 1 })
    await expect(main.getByText('Parcial').first()).toBeVisible()
  })

  test('sem finance.edit as ações com efeito financeiro não aparecem (CA 25)', async ({ page }) => {
    const { api, receivables } = receivablesApi()
    receivables.push({ id: 'r1', number: 'CR-2026-00001', partyId: 'c1', partyName: 'Kappa Ltda', description: 'Venda', accountId: 'a1', accountName: '1.1 Venda', costCenterId: null, projectId: null, amount: 10, paidAmount: 0, balance: 10, interest: 0, fine: 0, discount: 0, issueDate: today(), dueDate: today(), status: 'Open', isOverdue: false, isDueToday: true, origin: 'Manual', originId: null, installmentGroupId: null, cancelReason: null, cancelledAt: null, version: 1 })
    const { allModules } = await import('./mock-api')
    const modules = allModules().map((m) => (m.code === 'finance' ? { ...m, actions: ['view'] } : m))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '9', modules }) })
    await page.goto('/app/financeiro/receber')
    await expect(page.getByRole('main').getByText('Vence hoje')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Novo título a receber' })).toHaveCount(0)
    await page.goto('/app/financeiro/receber/r1')
    await expect(page.getByRole('heading', { level: 1, name: /CR-2026-00001/ })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Dar baixa' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Cancelar título' })).toHaveCount(0)
  })
})
