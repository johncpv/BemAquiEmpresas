import { expect, test } from '@playwright/test'

test.describe('componentes de tela (FND-09c)', () => {
  test('tabela reflete ordenação, página e busca na URL (CA 4)', async ({ page }) => {
    await page.goto('/dev/galeria')
    const table = page.getByRole('table', { name: 'Projetos de exemplo' })
    await table.getByRole('button', { name: 'Valor' }).click()
    await expect(page).toHaveURL(/sort=valor/)
    await page.getByRole('button', { name: 'Próxima' }).click()
    await expect(page).toHaveURL(/page=2/)
    await page.getByRole('searchbox', { name: 'Buscar projeto ou cliente' }).fill('Aurora')
    await expect(page).toHaveURL(/q=Aurora/)
    await expect(page).not.toHaveURL(/page=2/)
    const shared = page.url()

    await page.goto(shared)
    await expect(page.getByText(/Página 1 de 1 · 10 registros/)).toBeVisible()
  })

  test('a 400px a coluna secundária some e nada transborda (CA 7)', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    await page.goto('/dev/galeria')
    const table = page.getByRole('table', { name: 'Projetos de exemplo' })
    await expect(table.getByRole('columnheader', { name: 'Cliente' })).toBeHidden()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })

  test('formulário: erro em PT-BR no campo, foco no primeiro inválido e erro do servidor (CA 8, 9)', async ({ page }) => {
    await page.goto('/dev/galeria')
    const send = page.getByRole('button', { name: 'Enviar convite' })
    await send.click()
    const nome = page.getByLabel('Nome')
    await expect(nome).toBeFocused()
    await expect(nome).toHaveAttribute('aria-invalid', 'true')
    const describedBy = await nome.getAttribute('aria-describedby')
    expect(describedBy).toBeTruthy()
    await expect(page.locator(`[id="${describedBy}"]`)).toContainText(/caracteres|mínimo/i)

    await nome.fill('Ana Souza')
    await page.getByLabel('E-mail').fill('ana@konebras.com.br')
    await send.click()
    await expect(page.getByText('Já existe um usuário com esse e-mail.').first()).toBeVisible()
  })

  test('confirmação usa o mesmo verbo e o toast o particípio (CA 11)', async ({ page }) => {
    await page.goto('/dev/galeria')
    await page.getByRole('button', { name: 'Desativar usuário' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Desativar' }).click()
    await expect(page.getByText('Desativado')).toBeVisible()
  })
})
