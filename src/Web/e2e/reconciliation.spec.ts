import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const payer = allModules().map((m) => (m.code === 'finance' ? { ...m, actions: [...m.actions, 'pay', 'approve'] } : m))
const bankLine = (id: string, amount: number, extra: Record<string, unknown> = {}) => ({
  id, date: '2026-11-05', amount, description: 'PIX RECEBIDO ' + id, kind: 'Pix', counterpartyName: 'Cliente', counterpartyDocument: null, source: 'Api', bankVerified: true,
  status: 'Pending', importedByName: null, blocked: false, blockedReason: null, version: 7, ...extra,
})
const tx = (id: string, amount: number, extra: Record<string, unknown> = {}) => ({
  id, date: '2026-11-05', amount, description: 'Recebimento ' + id, type: 'Receipt', origin: 'Título a receber CR-1', link: '/app/financeiro/receber/r1', pendingReview: false, reconciled: false, version: 3, ...extra,
})

function reconciliationApi() {
  const posted: { url: string; body: unknown }[] = []
  const view = {
    accountId: 'b1', accountName: 'Banco Inter', from: '2026-11-01', to: '2026-11-30', hasStatement: true,
    pairs: [{ line: bankLine('l1', 1000), transaction: tx('t1', 1000), confidence: 'High', reason: 'Mesmo valor e mesma data' }],
    bankOnly: [{
      line: bankLine('l2', -12.9, { description: 'TAR PACOTE', kind: 'Fee' }), canBankEntry: true, suggestRule: true,
      action: { kind: 'BankEntry', text: 'Lançar como Tarifa bancária sem OP', entryKind: 'BankFee', accountId: 'x5', accountName: 'Financeiras', ruleId: null, receivableId: null, receivableNumber: null, customerName: null, receivableBalance: null, receivableVersion: null },
    }],
    systemOnly: [{ transaction: tx('t9', -70, { description: 'Pagamento que não saiu', type: 'Payment' }), daysWithoutBank: 8, stale: true }],
    done: [], totals: { pending: 2, reconciled: 0, ignored: 0, balanceDate: '2026-11-05', bankBalance: 987.1, systemBalance: 1000, difference: -12.9 },
    reconciledUntil: null, closedUntil: null, ofxBankEntryCap: 500, counterpartyMasked: false,
  }
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: false, version: '9', modules: payer }),
    'GET /api/v1/finance/bank-accounts': (r) => json(r, { items: [{ id: 'b1', name: 'Banco Inter', isActive: true, isDefault: true }], page: 1, pageSize: 100, total: 1 }),
    'GET /api/v1/finance/bank-accounts/*/reconciliation': (r) => json(r, view),
    'GET /api/v1/finance/chart-of-accounts': (r) => json(r, [{ id: 'x5', code: '3.5', name: 'Financeiras', type: 'Expense', dreGroup: 'FinancialResult', parentId: null, acceptsEntries: true, isActive: true, seedKey: '3.5' }]),
    'GET /api/v1/finance/cost-centers': (r) => json(r, []),
    'POST /api/v1/finance/bank-accounts/*/reconciliations': (r) => { posted.push({ url: r.request().url(), body: r.request().postDataJSON() }); return json(r, { reconciliationId: 'r1', kind: 'Suggested', lines: 1, transactions: 1, amount: 1000 }) },
    'POST /api/v1/finance/bank-accounts/*/reconciliation/lines/*/bank-entry': (r) => { posted.push({ url: r.request().url(), body: r.request().postDataJSON() }); return json(r, { reconciliationId: 'r2', kind: 'BankEntry', lines: 1, transactions: 1, amount: -12.9 }) },
  }
  return { api, posted }
}

test.describe('Conciliação bancária (BNK-03b)', () => {
  test('uma linha por par, Enter concilia, lançar tarifa sem OP e as seções (CA 1, 2, 4, 6 e 12)', async ({ page }, info) => {
    test.skip(info.project.name === 'celular')
    const { api, posted } = reconciliationApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/conciliacao')
    const pairs = page.getByRole('region', { name: 'Pares sugeridos' })
    await expect(pairs.getByText('Confiança Alta')).toBeVisible()
    await expect(page.getByRole('region', { name: 'No sistema, não no banco' }).getByText('Sem o banco há 8 dias')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Sem par no banco' }).getByText('Lançar como Tarifa bancária sem OP')).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    // Só pelo teclado: foco no par e Enter.
    await pairs.getByRole('listitem').first().focus()
    await page.keyboard.press('Enter')
    await expect.poll(() => posted.length).toBe(1)
    expect(posted[0].body).toMatchObject({ lines: [{ id: 'l1', version: 7 }], transactions: [{ id: 't1', version: 3 }], accepted: true })

    await page.getByRole('region', { name: 'Sem par no banco' }).getByRole('button', { name: 'Lançar sem OP' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByRole('combobox', { name: /Tipo/ })).toHaveValue('BankFee')
    await dialog.getByRole('button', { name: 'Lançar sem OP' }).click()
    await expect.poll(() => posted.length).toBe(2)
    expect(posted[1].url).toContain('/reconciliation/lines/l2/bank-entry')
    expect(posted[1].body).toMatchObject({ kind: 'BankFee', accountId: 'x5', lineVersion: 7 })
  })

  test('a 400 px os pares viram cartões e nada transborda', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    const { api } = reconciliationApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/conciliacao')
    await expect(page.getByRole('region', { name: 'Pares sugeridos' })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
  })
})
