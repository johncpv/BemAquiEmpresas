import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

type Account = { id: string; code: string; name: string; type: string; dreGroup: string; parentId: string | null; acceptsEntries: boolean; isActive: boolean; seedKey: string | null }
const acc = (id: string, code: string, name: string, parentId: string | null, acceptsEntries = true): Account => ({ id, code, name, type: 'Expense', dreGroup: 'Costs', parentId, acceptsEntries, isActive: true, seedKey: code })

/** Backend em memória da CAD-05a (as regras reais estão nos testes de integração .NET). */
function chartApi() {
  const accounts: Account[] = [acc('a2', '2', 'Custos', null, false), acc('a21', '2.1', 'Materiais', 'a2'), acc('a22', '2.2', 'Mão de obra direta', 'a2')]
  const posted: unknown[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/finance/chart-of-accounts': (r) => json(r, accounts),
    'GET /api/v1/finance/chart-of-accounts/suggestion': (r) => json(r, { code: '2.1.01', type: 'Expense', dreGroup: 'Costs' }),
    'POST /api/v1/finance/chart-of-accounts': (r) => {
      const b = r.request().postDataJSON() as Account
      posted.push(b)
      const created = { ...b, id: 'n1', acceptsEntries: true, isActive: true, seedKey: null }
      accounts.push(created)
      accounts.find((a) => a.id === b.parentId)!.acceptsEntries = false
      return json(r, created, 201)
    },
    'DELETE /api/v1/finance/chart-of-accounts/*': (r) => json(r, { code: 'account_in_use', detail: 'Conta com lançamentos não pode ser excluída. Inative-a.' }, 409),
    'POST /api/v1/finance/chart-of-accounts/*/inactivate': (r) => { const a = accounts.find((x) => r.request().url().includes(x.id))!; a.isActive = false; return json(r, a) },
  }
  return { api, posted }
}

test.describe('plano de contas (CAD-05a)', () => {
  test('árvore pelas setas e conta filha com o código sugerido (CA 4, 5 e 9)', async ({ page }) => {
    const { api, posted } = chartApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/plano-de-contas')
    const tree = page.getByRole('treegrid', { name: 'Plano de contas' })
    await expect(tree.getByRole('row', { name: /2\.1 Materiais/ })).toBeVisible()
    await tree.getByRole('row', { name: /^2 Custos/ }).focus()
    await page.keyboard.press('ArrowLeft')
    await expect(tree.getByRole('row', { name: /2\.1 Materiais/ })).toBeHidden()
    await page.keyboard.press('ArrowRight')
    await expect(tree.getByRole('row', { name: /2\.1 Materiais/ })).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    await page.getByRole('button', { name: 'Nova conta filha de 2.1' }).click()
    const panel = page.getByRole('dialog', { name: 'Nova conta filha de 2.1' })
    await expect(panel.getByRole('textbox', { name: /Código/ })).toHaveValue('2.1.01')
    await panel.getByRole('textbox', { name: /^Nome/ }).fill('Chapas de aço')
    await panel.getByRole('button', { name: 'Salvar conta contábil' }).focus()
    await page.keyboard.press('Enter')
    await expect(page.getByText('Conta contábil salva')).toBeVisible()
    expect(posted[0]).toEqual({ code: '2.1.01', name: 'Chapas de aço', type: 'Expense', dreGroup: 'Costs', parentId: 'a21' })
  })

  test('excluir conta com lançamento oferece Inativar (CA 13)', async ({ page }) => {
    const { api } = chartApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/plano-de-contas')
    await page.getByRole('button', { name: /2\.2 Mão de obra direta/ }).click()
    await page.getByRole('button', { name: 'Excluir conta contábil' }).click()
    const confirm = page.getByRole('alertdialog').or(page.getByRole('dialog', { name: /Excluir a conta 2.2/ }))
    await confirm.getByRole('button', { name: 'Excluir' }).click()
    await expect(page.getByText('Conta com lançamentos não pode ser excluída. Inative-a.')).toBeVisible()
    await page.getByRole('button', { name: 'Inativar' }).last().click()
    await expect(page.getByText('Inativada')).toBeVisible()
  })
})
