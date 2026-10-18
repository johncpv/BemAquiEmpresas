import { expect, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const row = (status: string) => ({ id: 't1', key: 'konebras', legalName: 'WSP Equipamentos Ltda', tradeName: 'Konebras', document: '47.225.005/0001-86', primaryHost: 'app.konebras.com.br', plan: 'Completo', status, schemaVersion: '20261018', createdAt: '2026-10-18T10:00:00Z' })
const steps = ['Register', 'CreateRole', 'CreateDatabase', 'Migrate', 'Seed', 'CreateOwner', 'SendInvite', 'Activate']
const detail = (status: string) => ({ tenant: row(status), domains: ['app.konebras.com.br'], steps, log: (status === 'Active' ? steps : steps.slice(0, 3)).map((s) => ({ step: s, status: 'Succeeded', message: null, at: '2026-10-18T10:00:00Z' })) })

test.describe('plataforma (FND-13, FND-03b, FND-06)', () => {
  test('sem sessão vai ao login; primeiro acesso exige configurar o 2FA', async ({ page }) => {
    let session = false
    await mockApi(page, {
      'GET /api/v1/platform/me': (r) => (session ? json(r, { adminId: 'a1' }) : json(r, {}, 401)),
      'POST /api/v1/platform/auth/login': (r) => json(r, { challenge: 'c1', purpose: 'setup', expiresAt: '2030-01-01T00:00:00Z' }),
      'POST /api/v1/platform/auth/2fa/setup': (r) => json(r, { manualKey: 'ABCD EFGH', otpAuthUri: 'otpauth://totp/BemAqui:admin?secret=ABCDEFGH' }),
      'POST /api/v1/platform/auth/2fa/confirm': (r) => { session = true; return json(r, { recoveryCodes: ['AAAAA-BBBBB', 'CCCCC-DDDDD'] }) },
      'GET /api/v1/platform/tenants': (r) => json(r, { items: [row('Active')], page: 1, pageSize: 20, total: 1 }),
    }, { session: false })
    await page.goto('/plataforma/tenants')
    await expect(page).toHaveURL(/\/plataforma\/entrar/)
    await page.getByLabel('E-mail').fill('admin@bemaquiapp.com.br')
    await page.getByLabel('Senha').fill('frase longa do super admin')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await expect(page.getByRole('img', { name: 'QR code para o app autenticador' })).toBeVisible()
    await page.getByLabel('Código do app').fill('123456')
    await page.getByRole('button', { name: 'Ativar' }).click()
    await expect(page.getByRole('list', { name: 'Códigos de recuperação' })).toContainText('AAAAA-BBBBB')
    await page.getByRole('button', { name: 'Concluir' }).click()
    await expect(page.getByRole('link', { name: 'Konebras' })).toBeVisible()
  })

  test('provisionar valida o CNPJ no cliente e acompanha até ativar', async ({ page }) => {
    let polls = 0
    await mockApi(page, {
      'GET /api/v1/platform/me': (r) => json(r, { adminId: 'a1' }),
      'GET /api/v1/platform/tenants': (r) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
      'GET /api/v1/platform/plans': (r) => json(r, ['Completo']),
      'POST /api/v1/platform/tenants': (r) => json(r, { key: 'konebras' }, 202),
      'GET /api/v1/platform/tenants/konebras': (r) => json(r, detail(++polls < 3 ? 'Provisioning' : 'Active')),
    })
    await page.goto('/plataforma/tenants')
    await expect(page.getByText('Nenhum tenant ainda.')).toBeVisible()
    await page.getByRole('button', { name: 'Provisionar tenant' }).first().click()
    const dialog = page.getByRole('dialog', { name: 'Provisionar tenant' })
    await dialog.getByLabel('Chave').fill('konebras')
    await dialog.getByLabel('Razão social').fill('WSP Equipamentos Ltda')
    await dialog.getByLabel('CNPJ').fill('11.111.111/1111-11')
    await dialog.getByLabel('Endereço').fill('app.konebras.com.br')
    await dialog.getByLabel('E-mail do proprietário').fill('dono@konebras.com.br')
    await dialog.getByRole('button', { name: 'Provisionar' }).click()
    await expect(dialog.getByText('CNPJ inválido. Confira os dígitos.')).toBeVisible()
    await dialog.getByLabel('CNPJ').fill('47.225.005/0001-86')
    await dialog.getByRole('button', { name: 'Provisionar' }).click()
    await expect(page).toHaveURL(/\/plataforma\/tenants\/konebras/)
    await expect(page.getByText('Provisionamento concluído. Convite enviado ao proprietário.')).toBeVisible({ timeout: 10_000 })
  })

  test('suspender exige motivo e a aba Módulos salva a liberação', async ({ page }) => {
    let suspended: unknown
    let released: unknown
    await mockApi(page, {
      'GET /api/v1/platform/me': (r) => json(r, { adminId: 'a1' }),
      'GET /api/v1/platform/tenants/konebras': (r) => json(r, detail('Active')),
      'POST /api/v1/platform/tenants/konebras/suspend': (r) => { suspended = r.request().postDataJSON(); return r.fulfill({ status: 204 }) },
      'GET /api/v1/platform/tenants/t1/modules': (r) => json(r, [{ code: 'finance', name: 'Caixa e pagamentos', area: 'Financeiro', description: 'Contas e caixa.', released: false, inPlan: false, isException: false }]),
      'PUT /api/v1/platform/tenants/t1/modules': (r) => { released = r.request().postDataJSON(); return r.fulfill({ status: 204 }) },
    })
    await page.goto('/plataforma/tenants/konebras')
    await page.getByRole('button', { name: 'Suspender' }).click()
    await page.getByLabel('Motivo da suspensão').fill('Inadimplência')
    await page.getByRole('alertdialog').getByRole('button', { name: 'Suspender' }).click()
    await expect(page.getByText('Tenant suspenso')).toBeVisible()
    expect(suspended).toEqual({ reason: 'Inadimplência' })

    await page.getByRole('button', { name: 'Módulos' }).click()
    await page.getByRole('switch', { name: 'Liberar Caixa e pagamentos' }).click()
    await expect(page.getByText('Alterações não salvas')).toBeVisible()
    await page.getByRole('button', { name: 'Salvar liberação' }).click()
    await expect(page.getByText('Liberação salva. Vale no próximo acesso dos usuários.')).toBeVisible()
    expect(released).toEqual({ modules: [{ code: 'finance', released: true }] })
  })
})
