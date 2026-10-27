import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ACCOUNT = '0199f1a2-0000-7000-8000-0000000000aa'

const detail = (over: Record<string, unknown> = {}) => ({
  bankAccountId: ACCOUNT, accountName: 'Inter PJ', agency: '0001', account: '1234567-8', configured: false, situation: 'NotConnected', environment: 'Sandbox',
  clientIdMasked: null, certificate: null, accountNumber: '12345678', grantedScopes: [], missingScopes: [], requiredScopes: ['boleto-cobranca.read', 'boleto-cobranca.write', 'extrato.read', 'webhook.read', 'webhook.write'],
  configuredAt: null, configuredByName: null, rotatedAt: null, rotatedByName: null, lastTest: null, productionAllowed: false, integrationId: null, version: null, ...over,
})

const configured = (over: Record<string, unknown> = {}) => detail({
  configured: true, situation: 'Sandbox', clientIdMasked: '•••• 9f3a', configuredAt: '2026-10-26T12:00:00Z', configuredByName: 'Ana Souza', integrationId: 'i1', version: 7,
  certificate: { notAfter: '2027-10-26T12:00:00Z', subject: 'CN=Konebras:47225005000186', thumbprint: 'ab'.repeat(32), format: 'Pem', daysToExpire: 365 },
  grantedScopes: ['boleto-cobranca.read', 'boleto-cobranca.write', 'extrato.read', 'webhook.read', 'webhook.write'], ...over,
})

/** Backend em memória da BNK-01a (as regras reais estão nos testes de integração .NET com o fake do Inter). */
function interApi(start = detail()) {
  let current: Record<string, unknown> = start
  const calls: { method: string; path: string; auth: string | null }[] = []
  let proved = false
  const log = (r: Route) => calls.push({ method: r.request().method(), path: new URL(r.request().url()).pathname, auth: r.request().headers()['authorization'] ?? null })
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/settings/integrations/inter': (r) => json(r, { items: [{ bankAccountId: ACCOUNT, name: 'Inter PJ', bankCode: '077', agency: '0001', account: '1234567-8', isInter: true, situation: current.situation, environment: current.configured ? current.environment : null, certificateNotAfter: null }], productionAllowed: false }),
    'GET /api/v1/settings/integrations/inter/*': (r) => json(r, current),
    'PUT /api/v1/settings/integrations/inter/*': (r) => {
      log(r)
      if (!proved) return json(r, { title: 'Acesso negado', detail: 'Para continuar, confirme o código da verificação em duas etapas.', code: 'two_factor_required', enrolled: true }, 403)
      current = configured()
      return json(r, current)
    },
    'POST /api/v1/settings/integrations/inter/*/rotate': (r) => {
      log(r)
      current = configured({ rotatedAt: '2026-10-26T13:00:00Z', rotatedByName: 'Ana Souza' })
      return json(r, current)
    },
    'POST /api/v1/settings/integrations/inter/*/test': (r) => {
      log(r)
      return json(r, { ok: true, outcome: 'Ok', message: 'Conexão com o Inter funcionando', action: null, grantedScopes: current.grantedScopes, missingScopes: [], testedAt: '2026-10-26T12:01:00Z' })
    },
    'POST /api/v1/me/2fa/step-up': (r) => {
      log(r)
      proved = true
      return json(r, { accessToken: 'tok-prova', expiresAt: '2030-01-01T00:00:00Z' })
    },
  }
  return { api, calls }
}

const files = (page: Page) => Promise.all([
  page.getByLabel('Certificado (.crt)').setInputFiles({ name: 'Inter_API_Certificado.crt', mimeType: 'application/x-x509-ca-cert', buffer: Buffer.from('-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----\n') }),
  page.getByLabel('Chave privada (.key)').setInputFiles({ name: 'Inter_API_Chave.key', mimeType: 'application/octet-stream', buffer: Buffer.from('-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n') }),
])

async function noOverflow(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
  expect(overflow).toBeLessThanOrEqual(0)
}

async function noSeriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
}

test.describe('Configurações → Integrações → Banco Inter (BNK-01a)', () => {
  test('configurar a conta com o 2FA de novo, testar a conexão e ver a faixa do ambiente de teste', async ({ page }) => {
    const { api, calls } = interApi()
    await mockApi(page, api)
    await page.goto('/app/configuracoes/integracoes/inter')
    await expect(page.getByRole('heading', { level: 1, name: 'Banco Inter' })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Integrações' }).getByRole('link', { name: 'Banco Inter' })).toHaveAttribute('aria-current', 'page')
    await page.getByRole('link', { name: /Inter PJ/ }).click()
    await expect(page.getByText('Esta conta ainda não está ligada ao Inter.')).toBeVisible()
    await page.getByRole('button', { name: 'Configurar integração' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Ambiente de teste do banco: os boletos não são cobranças reais.' })).toBeVisible()
    await expect(page.getByLabel('Conta corrente no Inter')).toHaveValue('12345678')
    await page.getByRole('textbox', { name: /^Client id/ }).fill('cliente-123')
    await page.getByRole('textbox', { name: /^Client secret/ }).fill('segredo-do-inter')
    await files(page)
    await noSeriousViolations(page)
    await page.getByRole('button', { name: 'Salvar integração bancária' }).click()

    // O servidor pede o 2FA de novo: o passo único abre, confirma o código e a mesma gravação é repetida.
    const stepUp = page.getByRole('dialog', { name: 'Confirme o código' })
    await expect(stepUp.getByText('Para continuar, confirme o código da verificação em duas etapas.')).toBeVisible()
    await stepUp.getByLabel('Código do aplicativo').fill('123456')
    await stepUp.getByRole('button', { name: 'Confirmar' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Integração bancária salva' }).first()).toBeVisible()
    const puts = calls.filter((c) => c.method === 'PUT')
    expect(puts).toHaveLength(2)
    expect(puts[1].auth).toBe('Bearer tok-prova')

    // Só de escrita: nada do segredo volta; o próximo passo é testar.
    await expect(page.getByText('•••• 9f3a')).toBeVisible()
    await expect(page.getByText('segredo-do-inter')).toHaveCount(0)
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Testar conexão' }).click()
    await expect(page.getByText('Conexão com o Inter funcionando')).toBeVisible()
    expect(calls.some((c) => c.method === 'POST' && c.path.endsWith('/test'))).toBe(true)
    await noOverflow(page)
  })

  test('certificado perto de vencer abre direto em "Trocar credenciais"', async ({ page }) => {
    const { api, calls } = interApi(configured({ situation: 'ExpiringSoon', certificate: { notAfter: '2026-11-05T12:00:00Z', subject: 'CN=Konebras', thumbprint: 'cd'.repeat(32), format: 'Pem', daysToExpire: 10 } }))
    await mockApi(page, api)
    await page.goto('/app/configuracoes/integracoes/inter')
    await expect(page.getByText('Certificado vence em breve')).toBeVisible()
    await page.getByRole('link', { name: /Inter PJ/ }).click()
    await expect(page).toHaveURL(/acao=trocar/)
    const form = page.getByRole('form', { name: 'Trocar credenciais' })
    await expect(form.getByText('Envie só o que mudou.', { exact: false })).toBeVisible()
    await files(page)
    await form.getByRole('button', { name: 'Trocar credenciais' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Credenciais trocadas' }).first()).toBeVisible()
    expect(calls.some((c) => c.path.endsWith('/rotate'))).toBe(true)
    await noOverflow(page)
  })

  test('sem settings.edit a tela fica em modo leitura, sem botões de escrita', async ({ page }) => {
    const { api } = interApi(configured())
    const modules = allModules().map((m) => (m.code === 'settings' ? { ...m, actions: ['view'] } : m))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: false, version: '3', modules }) })
    await page.goto(`/app/configuracoes/integracoes/inter/${ACCOUNT}`)
    await expect(page.getByText('Certificado válido até', { exact: false })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Trocar credenciais' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Desligar integração' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Testar conexão' })).toHaveCount(0)
  })

  test('sem conta 077: estado vazio com "Nova conta bancária"', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/settings/integrations/inter': (r) => json(r, { items: [], productionAllowed: false }) })
    await page.goto('/app/configuracoes/integracoes/inter')
    await expect(page.getByText('Nenhuma conta do Inter cadastrada.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Nova conta bancária' })).toBeVisible()
    await noOverflow(page)
  })

  test('Financeiro não liberado: tela "Funcionalidade não liberada"', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: true, version: '4', modules: allModules({ finance: { released: false } }) }) })
    await page.goto('/app/configuracoes/integracoes/inter')
    await expect(page.getByText('Funcionalidade não liberada nessa versão do sistema.')).toBeVisible()
  })
})
