import { expect, test } from '@playwright/test'
import { COFFEE, enterWithPin, mockPdv, openRegister, registerDevice, scan, SHIRT, tapDigits } from './pdv-api'

/**
 * Marco MVP do PDV, fluxo 1 (CX-01, PDV-02, PDV-03 e CX-02): abre o caixa com fundo de troco, vende pelo leitor e pelo teclado (3*código), paga em
 * Pix e dinheiro com troco, faz uma sangria e fecha com a contagem cega. Verificação-chave: a contagem nunca mostra o valor esperado.
 */
test('turno do caixa: abrir, vender com duas formas e troco, sangria e fechar com contagem', async ({ page }) => {
  await mockPdv(page)
  await registerDevice(page)
  await enterWithPin(page)
  await openRegister(page, '15000')

  // Leitor (código + Enter) e teclado com quantidade (2*código).
  await scan(page, COFFEE.gtin)
  await scan(page, `2*${SHIRT.gtin}`)
  await expect(page.getByRole('region', { name: 'Itens da venda' })).toContainText(COFFEE.description)
  await expect(page.getByLabel('Total a pagar: R$ 78,99')).toBeVisible()

  await page.getByRole('button', { name: /Pagar/ }).click()
  const methods = page.getByRole('group', { name: 'Escolha a forma' })
  await methods.getByRole('button', { name: /Pix/ }).click()
  await page.getByLabel('Valor', { exact: true }).fill('30,00')
  await page.getByRole('button', { name: 'Recebi o Pix' }).click()
  await expect(page.getByLabel('Falta: R$ 48,99')).toBeVisible()

  await methods.getByRole('button', { name: /Dinheiro/ }).click()
  await page.getByLabel('Valor recebido').fill('50,00')
  await page.getByRole('button', { name: 'Confirmar recebimento' }).click()
  await expect(page.getByRole('heading', { name: 'Venda concluída' })).toBeVisible()
  await expect(page.getByLabel('Troco: R$ 1,01')).toBeVisible()
  await page.getByRole('button', { name: 'Nova venda' }).click()

  // Sangria com valor e motivo; o comprovante sai e a movimentação entra no painel do caixa.
  await page.getByRole('button', { name: 'Caixa', exact: true }).click()
  await page.getByRole('region', { name: 'Caixa' }).getByRole('button', { name: 'Sangria', exact: true }).click()
  const drop = page.getByRole('dialog', { name: 'Sangria' })
  await tapDigits(drop, '2000')
  await drop.getByLabel('Motivo').fill('Depósito no banco')
  await drop.getByRole('button', { name: 'Registrar sangria' }).click()
  await page.getByRole('dialog', { name: 'Sangria registrada' }).getByRole('button', { name: 'Concluir' }).click()
  await expect(page.getByText(/Sangria · Depósito no banco/)).toBeVisible()

  // Fechamento com contagem cega: o operador conta sem ver o esperado (150,00 + 48,99 − 20,00).
  await page.getByRole('region', { name: 'Caixa' }).getByRole('button', { name: 'Fechar caixa' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Fechar caixa' }).click()
  const count = page.getByRole('region', { name: 'Fechar caixa' })
  await expect(count).toBeVisible()
  await expect(count).not.toContainText(/esperado|diferença|178,99/i)
  await tapDigits(count, '17899')
  await count.getByRole('button', { name: 'Continuar' }).click()
  const kept = page.getByRole('region', { name: 'Fundo que fica' })
  await expect(kept).toContainText(/Dinheiro contado: R\$\s178,99/)
  await kept.getByRole('button', { name: 'Confirmar contagem' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Confirmar contagem' }).click()
  await expect(page.locator('[role="status"]', { hasText: 'Fechamento enviado para conferência.' })).toBeVisible()
  await expect(page.getByRole('article', { name: 'Comprovante do fechamento' })).not.toContainText(/esperado|diferença/i)
})
