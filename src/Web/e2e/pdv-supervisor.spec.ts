import { expect, test } from '@playwright/test'
import { json } from './mock-api'
import { enterWithPin, mockPdv, openRegister, registerDevice, scan, SHIRT, SUPERVISOR, tapDigits } from './pdv-api'

/**
 * Marco MVP do PDV, fluxo 2 (PDV-04): o operador sem a permissão de desconto dá 10% numa loja com limite de 5%; a tela chama o supervisor, que
 * aprova com o PIN dele neste caixa. Verificação-chave: o pedido vai ao servidor com a ação, o supervisor, o PIN e os valores antes e depois.
 */
test('desconto acima do limite da loja pede o PIN do supervisor', async ({ page }) => {
  const approvals: Record<string, unknown>[] = []
  await mockPdv(page, {
    overrides: {
      'GET /api/v1/pos/supervisor-approvals/supervisors': (r) => json(r, [{ ...SUPERVISOR, actions: ['discount', 'cancel_item'] }]),
      'POST /api/v1/pos/supervisor-approvals': (r) => {
        approvals.push(r.request().postDataJSON() as Record<string, unknown>)
        return json(r, { approvalId: 'ap1', action: 'Discount', supervisorId: SUPERVISOR.userId, supervisorName: SUPERVISOR.displayName, approvedAt: new Date().toISOString() })
      },
    },
  })
  await registerDevice(page)
  await enterWithPin(page)
  await openRegister(page, '10000')
  await scan(page, SHIRT.gtin)
  await expect(page.getByLabel('Total a pagar: R$ 30,00')).toBeVisible()

  await page.getByRole('button', { name: /Desconto/ }).click()
  const sheet = page.getByRole('dialog', { name: 'Desconto' })
  await sheet.getByRole('button', { name: 'No total da venda' }).click()
  await sheet.getByLabel('Desconto (%)').fill('10')
  await sheet.getByRole('button', { name: 'Cliente fiel' }).click()
  await sheet.getByRole('button', { name: 'Aplicar desconto' }).click()

  // Acima do limite (5%): "Chamar supervisor", com o resumo do que vai ser aprovado.
  await expect(page.getByText('Total da venda: de R$ 30,00 para R$ 27,00')).toBeVisible()
  await page.getByRole('button', { name: SUPERVISOR.displayName }).click()
  await tapDigits(page, '512839')
  await page.getByRole('button', { name: 'Aprovar' }).click()

  await expect(page.getByText(`Desconto aplicado. Aprovado por ${SUPERVISOR.displayName}.`)).toBeVisible()
  await expect(page.getByLabel('Total a pagar: R$ 27,00')).toBeVisible()
  expect(approvals).toHaveLength(1)
  expect(approvals[0]).toMatchObject({ action: 'Discount', supervisorId: SUPERVISOR.userId, pin: '512839', reason: 'Cliente fiel', beforeAmount: 30, afterAmount: 27 })
})
