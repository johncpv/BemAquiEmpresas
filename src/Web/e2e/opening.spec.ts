import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'
import { openingApi } from './opening-api'

const due = () => new Date().toLocaleDateString('pt-BR', { timeZone: 'America/Sao_Paulo' }).replaceAll('/', '')

test.describe('Implantação financeira (FIN-00)', () => {
  test('do corte à conclusão: lote com erro na linha, duplicar e conferência (CA 1, 3, 5, 7, 9 e 10)', async ({ page }, info) => {
    test.skip(info.project.name === 'celular')
    test.setTimeout(60_000)
    const { api, posted } = openingApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/implantacao')
    const main = page.getByRole('main')
    await expect(main.getByRole('textbox', { name: /^Data de corte/ })).toHaveValue('30/09/2026')
    await main.getByRole('button', { name: 'Definir data de corte' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Definir data de corte' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Data de corte definida' })).toBeVisible()

    await expect(page).toHaveURL(/aba=saldos/)
    await main.getByRole('textbox', { name: 'Saldo de Banco Inter' }).fill('1500,25')
    await main.getByRole('button', { name: 'Salvar saldos' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Saldos salvos' })).toBeVisible()

    await expect(page).toHaveURL(/aba=receber/)
    await main.getByRole('combobox', { name: 'Cliente da linha 1' }).click()
    await page.getByPlaceholder('Digite para buscar').fill('Aurora')
    await page.getByRole('option', { name: /Aurora Condomínio/ }).click()
    await main.getByRole('textbox', { name: 'Descrição da linha 1' }).fill('Fatura de setembro')
    await main.getByRole('textbox', { name: 'Vencimento da linha 1' }).fill(due())
    await main.getByRole('textbox', { name: 'Saldo da linha 1' }).fill('2500')
    await main.getByRole('button', { name: 'Duplicar título 1' }).click()
    await main.getByRole('textbox', { name: 'Saldo da linha 2' }).fill('300,50')
    await main.getByRole('textbox', { name: 'Saldo da linha 2' }).blur()
    await expect(main.getByText(/Total do lote:\s*R\$\s2\.800,50/)).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    await main.getByRole('button', { name: 'Lançar títulos a receber' }).click()
    await expect(main.getByText('O documento de origem já foi lançado.')).toBeVisible()
    await expect(main.getByRole('row', { name: 'título 2' })).toHaveAttribute('aria-invalid', 'true')
    await main.getByRole('button', { name: 'Lançar títulos a receber' }).click()
    await expect(page.getByRole('status').filter({ hasText: /2 títulos a receber lançados \(R\$\s2\.800,50\)/ })).toBeVisible()
    expect(posted[1].key).toBe(posted[0].key)
    expect(posted[1].body.lines.map((l) => l.balance)).toEqual([2500, 300.5])
    expect(posted[1].body.lines[0]).toMatchObject({ partyId: 'c1', accountId: 'r9' })

    await page.getByRole('tab', { name: '5. Conferência' }).click()
    const statement = main.getByRole('textbox', { name: 'Saldo no extrato de Banco Inter' })
    await statement.fill('1500,20')
    await statement.blur()
    await expect(main.getByRole('table', { name: 'Conferência das contas' }).getByText(/0,05/)).toBeVisible()
    await main.getByRole('textbox', { name: 'Observação das diferenças' }).fill('Tarifa lançada no dia seguinte.')
    await main.getByRole('button', { name: 'Concluir implantação' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Concluir implantação' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Implantação concluída' }).first()).toBeVisible()
    await expect(page.getByRole('region', { name: 'Próximos passos' })).toBeVisible()
  })

  test('sem finance.approve a data de corte é só leitura (CA 1 e 14)', async ({ page }) => {
    const { api } = openingApi()
    const modules = allModules().map((m) => (m.code === 'finance' ? { ...m, actions: ['view', 'create', 'edit'] } : m))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '8', modules }) })
    await page.goto('/app/financeiro/implantacao')
    await expect(page.getByText('Quem define a data de corte é o Proprietário.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Definir data de corte' })).toHaveCount(0)
  })

  test('a 400 px o editor vira cartões e nada transborda', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    const { api, state } = openingApi()
    state.cutoverDate = '2026-09-30'
    await mockApi(page, api)
    await page.goto('/app/financeiro/implantacao?aba=receber')
    await expect(page.getByRole('textbox', { name: 'Descrição da linha 1' })).toBeVisible()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
