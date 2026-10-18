import { expect, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

test.describe('autenticação (FND-04, FND-05)', () => {
  test('sem sessão vai ao login; senha + código levam à rota pedida e o token não fica no storage (CA 3)', async ({ page }) => {
    await mockApi(
      page,
      {
        'POST /api/v1/auth/login': (r) => json(r, { accessToken: null, expiresAt: null, twoFactor: { challenge: 'c1', purpose: 'verify', expiresAt: '2030-01-01T00:00:00Z' } }),
        'POST /api/v1/auth/2fa/verify': (r) => json(r, { accessToken: 'tok-final', expiresAt: '2030-01-01T00:00:00Z' }),
      },
      { session: false },
    )
    await page.goto('/app/acessos/grupos')
    await expect(page).toHaveURL(/\/entrar\?volta=/)
    await page.getByLabel('E-mail').fill('ana@konebras.com.br')
    await page.getByLabel('Senha').fill('uma frase longa de teste')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.getByLabel('Código do app autenticador').fill('123456')
    await page.getByRole('button', { name: 'Confirmar' }).click()
    await expect(page).toHaveURL(/\/app\/acessos\/grupos$/)
    const stored = await page.evaluate(() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage) + document.cookie)
    expect(stored).not.toContain('tok-final')
  })

  test('código errado mostra as tentativas restantes; cinco erros pedem para entrar de novo (CA 6)', async ({ page }) => {
    let tries = 0
    await mockApi(
      page,
      {
        'POST /api/v1/auth/login': (r) => json(r, { accessToken: null, expiresAt: null, twoFactor: { challenge: 'c1', purpose: 'verify', expiresAt: '2030-01-01T00:00:00Z' } }),
        'POST /api/v1/auth/2fa/verify': (r) =>
          ++tries < 2 ? json(r, { detail: 'Código inválido. Restam 4 tentativas.', code: 'auth.2fa_invalid_code' }, 422) : json(r, { detail: 'Muitas tentativas com código errado. Entre de novo.', code: 'auth.2fa_too_many_attempts' }, 422),
      },
      { session: false },
    )
    await page.goto('/entrar')
    await page.getByLabel('E-mail').fill('ana@konebras.com.br')
    await page.getByLabel('Senha').fill('uma frase longa de teste')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.getByLabel('Código do app autenticador').fill('000000')
    await page.getByRole('button', { name: 'Confirmar' }).click()
    await expect(page.getByText('Código inválido. Restam 4 tentativas.')).toBeVisible()
    await page.getByRole('button', { name: 'Confirmar' }).click()
    await expect(page.getByRole('link', { name: 'Entrar de novo' })).toBeVisible()
  })

  test('sair revoga a sessão e volta ao login (FND-04 CA 8)', async ({ page }) => {
    let loggedOut = false
    await mockApi(page, { 'POST /api/v1/auth/logout': (r) => { loggedOut = true; return r.fulfill({ status: 204 }) } })
    await page.goto('/app')
    await page.getByRole('button', { name: 'Menu de Ana Souza' }).click()
    await page.getByRole('menuitem', { name: 'Sair' }).click()
    await expect(page).toHaveURL(/\/entrar/)
    expect(loggedOut).toBe(true)
  })
})
