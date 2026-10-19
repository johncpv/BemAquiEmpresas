import { expect, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const NOT_RELEASED = 'Funcionalidade não liberada nessa versão do sistema.'

test.describe('menu por permissão (FND-06d)', () => {
  test('não liberado: cadeado no menu e tela padrão com a mensagem exata pela URL (CA 1, 2)', async ({ page }, info) => {
    await mockApi(page, { 'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ finance: { released: false, enabled: false } }) }) })
    await page.goto('/app')
    await page.getByRole('button', { name: 'Financeiro' }).click()
    await expect(page.getByRole('link', { name: 'Caixa e pagamentos, não liberado' })).toBeVisible()
    test.skip(info.project.name === 'celular', 'o sheet cobre a tela; a URL direta é coberta no desktop')
    await page.goto('/app/financeiro')
    await expect(page.getByText(NOT_RELEASED, { exact: true })).toBeVisible()
    await expect(page.getByText('O que Caixa e pagamentos faz.')).toBeVisible()
  })

  test('liberado sem acesso: some do menu (e a área vazia some) e a URL mostra acesso negado (CA 3, 4)', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ hr: { enabled: false } }) }) })
    await page.goto('/app')
    await expect(page.getByRole('navigation', { name: 'Navegação principal' }).getByRole('button', { name: 'Pessoas' })).toHaveCount(0)
    await page.goto('/app/rh')
    await expect(page.getByRole('heading', { name: 'Sem acesso a esta tela' })).toBeVisible()
    await expect(page.getByText('Você não tem acesso a esta funcionalidade. Fale com o administrador.')).toBeVisible()
  })

  test('permissão revogada no meio da navegação: versão nova remonta o menu e a tela (CA 6)', async ({ page }) => {
    let version = '1'
    let modules = allModules()
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r) => r.fulfill({ status: 200, contentType: 'application/json', headers: { 'X-Perm-Version': version }, body: JSON.stringify({ permissions: [], isOwner: false, version, modules }) }),
      'GET /api/v1/notifications/': (r) => r.fulfill({ status: 200, contentType: 'application/json', headers: { 'X-Perm-Version': version }, body: JSON.stringify({ page: { items: [], page: 1, pageSize: 10, total: 0 }, unread: 0 }) }),
    })
    await page.goto('/app/compras')
    await expect(page.getByText('Esta tela está em construção.')).toBeVisible()

    version = '2'
    modules = allModules({ purchasing: { enabled: false } })
    await page.getByRole('button', { name: 'Notificações' }).click() // qualquer request traz a versão nova
    await expect(page.getByRole('heading', { name: 'Sem acesso a esta tela' })).toBeVisible()
  })
})
