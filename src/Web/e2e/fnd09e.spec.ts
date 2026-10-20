import { expect, test } from '@playwright/test'
import { mockApi } from './mock-api'

/** FND-09e: mensagem no topo acima do dock, diálogo grande só pelo teclado e editor em cartões a 400 px. */
test.describe('componentes da Fase 2 (FND-09e)', () => {
  test.beforeEach(async ({ page }) => mockApi(page))

  test('diálogo grande pelo teclado; sucesso no topo, visível acima do dock (CA 1 e 4)', async ({ page }) => {
    await page.goto('/app/dev/componentes')
    await page.getByRole('button', { name: 'Dar baixa' }).first().focus()
    await page.keyboard.press('Enter')
    const dialog = page.getByRole('alertdialog', { name: /Dar baixa no título/ })
    await expect(dialog).toBeVisible()
    await expect(dialog.getByRole('group', { name: 'Impacto' })).toContainText('Saldo da conta depois')
    const viewport = page.viewportSize()!
    const box = (await dialog.boundingBox())!
    if (viewport.width < 640) {
      expect(box.width).toBeGreaterThanOrEqual(viewport.width - 1)
      expect(box.height).toBeGreaterThanOrEqual(viewport.height - 1)
    } else {
      expect(box.width).toBeGreaterThan(560)
    }

    await dialog.getByRole('button', { name: 'Dar baixa' }).focus()
    await page.keyboard.press('Enter')
    await expect(dialog.getByRole('alert')).toHaveText('Informe o valor recebido.')
    await expect(dialog.getByRole('alert')).toBeFocused()
    await dialog.getByRole('textbox', { name: /Valor recebido/ }).focus()
    await page.keyboard.type('1500')
    await dialog.getByRole('button', { name: 'Dar baixa' }).focus()
    await page.keyboard.press('Enter')
    await expect(dialog).toBeHidden()

    const message = page.getByRole('status').filter({ hasText: 'Baixa registrada' })
    await expect(message).toBeVisible()
    const dock = await page.getByRole('navigation', { name: 'Navegação principal' }).boundingBox()
    const shown = (await message.boundingBox())!
    expect(shown.y).toBeLessThan(viewport.height / 2)
    if (dock) expect(shown.y + shown.height).toBeLessThanOrEqual(dock.y)
  })

  test('a 400 px, cada linha do editor vira um cartão com os rótulos (CA 9)', async ({ page }) => {
    await page.goto('/app/dev/componentes')
    const row = page.getByRole('row', { name: 'parcela 1' })
    await expect(row).toBeVisible()
    const label = row.getByText('Percentual', { exact: true })
    if ((page.viewportSize()?.width ?? 1280) < 640) await expect(label).toBeVisible()
    else expect((await label.boundingBox())?.width ?? 0).toBeLessThanOrEqual(1)
  })
})
