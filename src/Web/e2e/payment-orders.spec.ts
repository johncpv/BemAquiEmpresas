import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'
import { ordersApi } from './payment-orders-api'

const payer = allModules().map((m) => (m.code === 'finance' ? { ...m, actions: [...m.actions, 'pay'] } : m))
const withPay = (api: Record<string, (r: Route) => Promise<void> | void>) => ({ ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '9', modules: payer }) })

test.describe('Ordens de pagamento (FIN-03a)', () => {
  test('o aviso leva direto ao Aprovar, com o destino do dinheiro e o aviso do titular (CA 6 e 7)', async ({ page }) => {
    const { api, calls } = ordersApi('Pending')
    await mockApi(page, api)
    await page.goto('/app/financeiro/ordens-de-pagamento/op1?acao=aprovar')
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Para: Pix 529*****725 · Fulano de Tal')).toBeVisible()
    await expect(dialog.getByText('O titular não é o fornecedor.')).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await dialog.getByRole('button', { name: 'Aprovar' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Aprovada' }).first()).toBeVisible()
    expect(calls[0].url).toContain('/approve')
    await expect(page.getByRole('region', { name: 'Próximos passos' })).toContainText('Próxima OP aguardando você')
  })

  test('pagar pelo teclado mostra o total e o saldo negativo e sugere o recolhimento (CA 13, 15 e 19)', async ({ page }) => {
    const { api, calls } = ordersApi('Approved')
    await mockApi(page, withPay(api))
    await page.goto('/app/financeiro/ordens-de-pagamento/op1')
    await page.getByRole('button', { name: 'Pagar', exact: true }).focus()
    await page.keyboard.press('Enter')
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('O saldo da conta fica negativo.')).toBeVisible()
    await dialog.getByRole('textbox', { name: 'Juros de CP-2026-00007' }).fill('0')
    await dialog.getByRole('button', { name: 'Pagar', exact: true }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Pagamento registrado' }).first()).toBeVisible()
    expect(calls[0].body).toMatchObject({ bankAccountId: 'b1', items: [{ itemId: 'i1', interest: 0, fine: 0, discount: 0 }] })
    await expect(page.getByRole('region', { name: 'Próximos passos' })).toContainText('Gerar título do recolhimento (CP-2026-00007)')
  })

  test('quem solicitou não vê "Aprovar" (CA 9)', async ({ page }) => {
    const { api } = ordersApi('Pending', true)
    await mockApi(page, api)
    await page.goto('/app/financeiro/ordens-de-pagamento/op1')
    await expect(page.getByText('Quem solicitou a ordem não pode aprová-la.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Aprovar', exact: true })).toHaveCount(0)
  })

  test('a 400 px o diálogo de pagar não transborda', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    const { api } = ordersApi('Approved')
    await mockApi(page, withPay(api))
    await page.goto('/app/financeiro/ordens-de-pagamento/op1')
    await page.getByRole('button', { name: 'Pagar', exact: true }).click()
    await expect(page.getByRole('alertdialog')).toBeVisible()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
