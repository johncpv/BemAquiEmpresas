import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

interface Account { id: string; name: string; kind: 'Bank' | 'Cash'; bankCode: string | null; bankName: string | null; agency: string | null; account: string | null; openingBalance: number | null; openingDate: string | null; isActive: boolean; isDefault: boolean; importSource: null; masked: boolean; createdAt: string }
interface Term { id: string; name: string; installments: { days: number; percent: number }[]; isActive: boolean; seedKey: string | null; importSource: null; createdAt: string }

/** Backend em memória da CAD-05b (as regras reais estão nos testes de integração .NET). */
function financeApi() {
  const accounts: Account[] = []
  const terms: Term[] = []
  const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })
  const split = (body: { installments: { days: number; percent: number }[]; amount: number; baseDate: string }) => {
    let used = 0
    return body.installments.map((i, n) => {
      const amount = n === body.installments.length - 1 ? Math.round((body.amount - used) * 100) / 100 : Math.round(body.amount * i.percent) / 100
      used += amount
      const d = new Date(body.baseDate + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + i.days)
      return { number: n + 1, dueDate: d.toISOString().slice(0, 10), amount }
    })
  }
  return {
    'GET /api/v1/finance/banks': (r: Route) => json(r, [{ code: '077', name: 'Banco Inter' }, { code: '341', name: 'Itaú Unibanco' }]),
    'GET /api/v1/finance/bank-accounts': (r: Route) => json(r, page(accounts)),
    'POST /api/v1/finance/bank-accounts': (r: Route) => {
      const b = r.request().postDataJSON() as Account
      const a: Account = { ...b, id: crypto.randomUUID(), bankName: b.bankCode === '077' ? 'Banco Inter' : null, isActive: true, isDefault: accounts.length === 0, importSource: null, masked: false, createdAt: '2026-10-19T12:00:00Z' }
      accounts.push(a)
      return json(r, a, 201)
    },
    'GET /api/v1/finance/payment-terms': (r: Route) => json(r, page(terms)),
    'POST /api/v1/finance/payment-terms/preview': (r: Route) => json(r, split(r.request().postDataJSON())),
    'POST /api/v1/finance/payment-terms': (r: Route) => {
      const t: Term = { ...(r.request().postDataJSON() as Term), id: crypto.randomUUID(), isActive: true, seedKey: null, importSource: null, createdAt: '2026-10-19T12:00:00Z' }
      terms.push(t)
      return json(r, t, 201)
    },
    'GET /api/v1/finance/payment-terms/*': (r: Route) => { const t = terms.find((x) => r.request().url().includes(x.id)); return t ? json(r, t) : json(r, { code: 'payment_term_not_found' }, 404) },
  } as Record<string, (r: Route) => Promise<void> | void>
}

test.describe('financeiro (CAD-05b)', () => {
  test('cadastra a conta sem saldo e vê o atalho para informar o saldo (CA 1, 4 e 6)', async ({ page }) => {
    await mockApi(page, financeApi())
    await page.goto('/app/financeiro/contas')
    await expect(page.getByText('Cadastre a primeira conta bancária ou o caixa da empresa.')).toBeVisible()
    await page.getByRole('button', { name: 'Nova conta bancária' }).first().click()
    await page.getByRole('textbox', { name: /Nome/ }).fill('Inter PJ')
    await page.getByRole('combobox', { name: /Banco/ }).click()
    await page.getByRole('option', { name: /077 Banco Inter/ }).click()
    await page.getByRole('textbox', { name: /Agência/ }).fill('0001-9')
    await page.getByRole('textbox', { name: /^Conta/ }).fill('12345678-9')
    await page.getByRole('button', { name: 'Salvar conta bancária' }).click()
    await expect(page.getByText('Conta bancária salva')).toBeVisible()
    await expect(page.getByText('Saldo inicial não informado')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Informar saldo inicial de Inter PJ' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Cadastrar condições de pagamento' })).toBeVisible()
  })

  test('cria 30/60/90 dias só pelo teclado e confere a prévia (CA 11 e 13)', async ({ page }) => {
    await mockApi(page, financeApi())
    await page.goto('/app/financeiro/condicoes-de-pagamento/nova')
    await page.getByRole('textbox', { name: /Nome/ }).fill('30/60/90 dias')
    await page.getByRole('button', { name: 'Adicionar parcela' }).press('Enter')
    await page.getByRole('button', { name: 'Adicionar parcela' }).press('Enter')
    await expect(page.getByLabel('Dias da parcela 3')).toHaveValue('90')
    await page.getByRole('button', { name: 'Dividir igualmente' }).press('Enter')
    await expect(page.getByText('Soma: 100,0000%')).toBeVisible()
    await page.getByLabel('Data base').fill('10112026')
    const preview = page.getByRole('table', { name: 'Parcelas da prévia' })
    await expect(preview.getByRole('row')).toHaveCount(4)
    await expect(preview).toContainText('10/12/2026')
    await expect(preview).toContainText('08/02/2027')
    await expect(preview).toContainText('333,34')
    await page.getByRole('button', { name: 'Salvar condição de pagamento' }).press('Enter')
    await expect(page.getByText('Condição de pagamento salva')).toBeVisible()

    for (const path of ['/app/financeiro/contas', '/app/financeiro/condicoes-de-pagamento', '/app/financeiro/condicoes-de-pagamento/nova']) {
      await page.goto(path)
      await page.getByRole('heading', { level: 1 }).waitFor()
      const results = await new AxeBuilder({ page }).analyze()
      expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
    }
  })
})
