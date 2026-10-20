import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ACCOUNTS = [
  { id: 'x1', code: '3.3', name: 'Administrativas', type: 'Expense', dreGroup: 'OperatingExpenses', parentId: null, acceptsEntries: true, isActive: true, seedKey: '3.3' },
  { id: 'r1', code: '1.1', name: 'Venda de equipamentos', type: 'Revenue', dreGroup: 'GrossRevenue', parentId: null, acceptsEntries: true, isActive: true, seedKey: '1.1' },
]
const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

const PAYABLE = {
  id: 'p1', number: 'CP-2026-00001', partyId: 's1', partyName: 'Gama Serviços', description: 'Manutenção', accountId: 'x1', accountName: '3.3 Administrativas', costCenterId: null,
  amount: 1000, retentionIss: 50, retentionInss: 0, retentionIr: 0, retentionPcc: 0, netAmount: 950, paidAmount: 0, balance: 950, issueDate: today(), dueDate: today(), isEstimate: false,
  status: 'Open', isOverdue: false, isDueToday: true, origin: 'Manual', originId: null, installmentGroupId: null, competence: null, paymentMethod: 'Pix', paymentMethodChangedAt: new Date().toISOString(),
  cancelReason: null, cancelledAt: null, version: 1,
}

/** Backend em memória da FIN-02 (as regras reais estão nos testes de integração .NET). */
function payablesApi() {
  const posted: { body: unknown; headers: Record<string, string> }[] = []
  const items: typeof PAYABLE[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/finance/chart-of-accounts': (r) => json(r, ACCOUNTS),
    'GET /api/v1/finance/cost-centers': (r) => json(r, []),
    'GET /api/v1/finance/payment-terms': (r) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
    'GET /api/v1/finance/parameters': (r) => json(r, { discountApprovalPct: 5, discountApprovalAmount: 500, cancelApprovalAmount: 1000, backdateDays: 7 }),
    'GET /api/v1/finance/suppliers': (r) => json(r, [{ id: 's1', name: 'Gama Serviços', document: '12345678000190', defaultPaymentTermId: null }]),
    'POST /api/v1/finance/payables': (r) => { posted.push({ body: r.request().postDataJSON(), headers: r.request().headers() }); items.push(PAYABLE); return json(r, { items: [PAYABLE], holderWarning: null, boletoWarning: null }, 201) },
    'GET /api/v1/finance/payables': (r) => json(r, { page: { items, page: 1, pageSize: 20, total: items.length }, totals: { open: 950, overdue: 0 } }),
    'GET /api/v1/finance/payables/*': (r) => json(r, PAYABLE),
    'GET /api/v1/finance/payables/*/payment-method': (r) => json(r, { details: { kind: 'Pix', pixKeyType: 'Cpf', pixKey: '529*****725', holderName: 'Fulano', holderDocument: '***.982.247-**' }, holderWarning: 'O titular (***.982.247-**) não é o fornecedor.', boletoWarning: null }),
  }
  return { api, posted }
}

async function press(page: Page, name: string, scope = page.locator('body')) {
  await scope.getByRole('button', { name, exact: true }).first().focus()
  await page.keyboard.press('Enter')
}

test.describe('Contas a pagar (FIN-02)', () => {
  test('lança com retenção e Pix pelo teclado; líquido ao vivo e aviso do titular no título (CA 1, 4, 6 e 7)', async ({ page }) => {
    const { api, posted } = payablesApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/pagar')
    await expect(page.getByText('Lance a primeira conta a pagar ou cadastre as despesas fixas do mês.')).toBeVisible()
    await page.getByRole('link', { name: 'Novo título a pagar' }).first().focus()
    await page.keyboard.press('Enter')
    const main = page.getByRole('main')
    await main.getByRole('combobox', { name: /^Fornecedor/ }).click()
    await page.getByPlaceholder('Digite para buscar').fill('Gama')
    await page.getByRole('option', { name: /Gama Serviços/ }).click()
    await main.getByRole('textbox', { name: /^Descrição/ }).focus()
    await page.keyboard.type('Manutenção')
    await main.getByRole('combobox', { name: /^Conta/ }).selectOption('x1')
    await expect(main.getByRole('combobox', { name: /^Conta/ }).locator('option', { hasText: 'Venda de equipamentos' })).toHaveCount(0)
    await main.getByRole('textbox', { name: /^Valor/ }).first().focus()
    await page.keyboard.type('1000')
    await main.getByRole('textbox', { name: /^Vencimento/ }).focus()
    await page.keyboard.type(today().split('-').reverse().join(''))
    await main.getByRole('textbox', { name: /^ISS/ }).focus()
    await page.keyboard.type('50')
    await main.getByRole('textbox', { name: /^INSS/ }).focus()
    await expect(main.getByText(/Valor líquido a pagar:\s*R\$\s950,00/)).toBeVisible()
    await main.getByRole('combobox', { name: /^Forma de pagamento/ }).selectOption('Pix')
    await main.getByRole('combobox', { name: /^Tipo de chave/ }).selectOption('Cpf')
    await main.getByRole('textbox', { name: /^Chave Pix/ }).fill('52998224725')
    await main.getByRole('textbox', { name: /^Nome do titular/ }).fill('Fulano')
    await main.getByRole('textbox', { name: /^CPF ou CNPJ do titular/ }).fill('52998224725')
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await press(page, 'Salvar título a pagar', main)
    await expect(page.getByRole('status').filter({ hasText: 'Título a pagar salvo' })).toBeVisible()
    expect(posted[0].headers['idempotency-key']).toBeTruthy()
    expect(posted[0].body).toMatchObject({ partyId: 's1', accountId: 'x1', amount: 1000, retentionIss: 50, paymentDetails: { kind: 'Pix', pixKeyType: 'Cpf', pixKey: '52998224725', holderName: 'Fulano' } })
    await expect(page).toHaveURL(/\/app\/financeiro\/pagar\/p1/)
    await page.getByRole('tab', { name: 'Forma de pagamento' }).click()
    await expect(page.getByText('O titular (***.982.247-**) não é o fornecedor.')).toBeVisible()
  })

  test('sem finance.edit o título é só leitura e não há cancelar (CA 21)', async ({ page }) => {
    const { api } = payablesApi()
    const modules = allModules().map((m) => (m.code === 'finance' ? { ...m, actions: ['view'] } : m))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '8', modules }) })
    await page.goto('/app/financeiro/pagar/p1')
    await expect(page.getByRole('heading', { level: 1, name: /CP-2026-00001/ })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Cancelar título' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Salvar título a pagar' })).toHaveCount(0)
  })
})
