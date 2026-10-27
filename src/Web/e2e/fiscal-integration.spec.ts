import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const PATH = '/app/configuracoes/integracoes/emissor-fiscal'

type View = Record<string, unknown>

const empty = (over: View = {}): View => ({
  configured: false, allowed: true, allowedEnvironment: 'Homologation', defaultBaseUrl: 'http://emissor-fiscal:8080', baseUrl: null, apiKeySetAt: null,
  webhookSecretSetAt: null, webhookPreviousValidUntil: null, webhookUrl: null, active: false, emitterTenantId: null, issuerDocument: null, issuerName: null,
  environment: null, scopes: [], certificateValidUntil: null, lastTestAt: null, lastTestResult: null, lastTestMessage: null, lastWebhookAt: null,
  pendingInvoices: 0, oldestPendingSince: null, oldestPendingInvoiceId: null, ...over,
})

const saved = (over: View = {}): View => empty({
  configured: true, baseUrl: 'http://emissor-fiscal:8080/', apiKeySetAt: '2026-10-26T12:00:00Z', webhookSecretSetAt: '2026-10-26T12:00:00Z',
  webhookUrl: 'http://bqe-homolog-api:8080/webhooks/emissor-fiscal/AbCdEfGhIjKlMnOpQrStUv', ...over,
})

const tested = (over: View = {}): View => saved({
  active: true, emitterTenantId: 'f03e0000-0000-7000-8000-000000000001', issuerDocument: '47225005000186', issuerName: 'WSP Equipamentos por Elevação LTDA',
  environment: 'Homologation', scopes: ['consultar', 'nfe:emitir', 'nfe:eventos'], certificateValidUntil: '2027-03-10T23:59:59Z', lastTestAt: '2026-10-26T12:05:00Z', lastTestResult: 'ok', ...over,
})

/** Backend em memória da FIS-01 (as regras reais estão nos testes de integração .NET, com o fake do Emissor). */
function fiscalApi(initial: View, testResult: (v: View) => { ok: boolean; result: string; message: string; integration: View }) {
  let view = initial
  const calls: { method: string; body?: unknown }[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/fiscal/integration': (r) => json(r, view),
    'PUT /api/v1/fiscal/integration': (r) => {
      const body = r.request().postDataJSON() as { code?: string; apiKey?: string | null }
      calls.push({ method: 'PUT', body })
      if (body.code !== '123456') return json(r, { status: 403, code: 'two_factor_required', title: 'Acesso negado', detail: 'Confirme com o código do aplicativo autenticador para salvar a integração fiscal.' }, 403)
      view = saved()
      return json(r, view)
    },
    'POST /api/v1/fiscal/integration/test': (r) => {
      calls.push({ method: 'POST' })
      const result = testResult(view)
      view = result.integration
      return json(r, result)
    },
  }
  return { api, calls }
}

async function noSeriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
}

test.describe('Configurações → Integrações → Emissor fiscal (FIS-01)', () => {
  test('configurar com 2FA de novo, testar a conexão e ver a faixa de homologação e o próximo passo', async ({ page }) => {
    const { api, calls } = fiscalApi(empty(), () => ({ ok: true, result: 'ok', message: 'Conexão testada', integration: tested() }))
    await mockApi(page, api)
    await page.goto(PATH)
    await expect(page.getByRole('heading', { level: 1, name: 'Integrações: Emissor fiscal' })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Integrações' }).getByRole('link', { name: 'Emissor fiscal' })).toHaveAttribute('aria-current', 'page')
    await expect(page.getByText('Ambiente de homologação da SEFAZ: as notas não têm valor fiscal.')).toBeVisible()
    await expect(page.getByText('Conecte o Emissor Fiscal para emitir notas. Peça ao administrador do Emissor Fiscal uma chave sem o escopo admin.')).toBeVisible()
    await noSeriousViolations(page)

    await page.getByRole('button', { name: 'Configurar' }).click()
    const form = page.getByRole('form', { name: 'Conexão com o Emissor Fiscal' })
    await expect(form.getByLabel('Endereço interno do Emissor Fiscal')).toHaveValue('http://emissor-fiscal:8080')
    await form.getByRole('button', { name: 'Salvar integração fiscal' }).click()
    await expect(form.getByText('Informe a chave de API do Emissor Fiscal.')).toBeVisible()
    await form.getByLabel(/^Chave de API/).fill('ef_chave-do-tenant-demo-0001')
    await form.getByLabel(/^Segredo do aviso/).fill('segredo-do-aviso-0123456789')
    await form.getByRole('button', { name: 'Salvar integração fiscal' }).click()

    const dialog = page.getByRole('alertdialog')
    await dialog.getByLabel('Código do autenticador').fill('000000')
    await dialog.getByRole('button', { name: 'Salvar integração fiscal' }).click()
    await expect(dialog.getByText('Confirme com o código do aplicativo autenticador para salvar a integração fiscal.')).toBeVisible()
    await dialog.getByLabel('Código do autenticador').fill('123456')
    await dialog.getByRole('button', { name: 'Salvar integração fiscal' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Integração fiscal salva' }).first()).toBeVisible()
    expect(calls.filter((c) => c.method === 'PUT').at(-1)?.body).toMatchObject({ baseUrl: 'http://emissor-fiscal:8080', apiKey: 'ef_chave-do-tenant-demo-0001', code: '123456' })

    await expect(page.getByText('Aguardando o teste')).toBeVisible()
    await expect(page.getByText('Configurada em', { exact: false }).first()).toBeVisible()
    await expect(page.getByText('ef_chave-do-tenant-demo-0001')).toHaveCount(0)
    await page.getByRole('button', { name: 'Testar conexão' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Conexão testada' }).first()).toBeVisible()
    await expect(page.getByText('Ativa', { exact: true })).toBeVisible()
    await expect(page.getByText('47.225.005/0001-86', { exact: false })).toBeVisible()
    await expect(page.getByText('Homologação (sem valor fiscal)')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Configurar o fiscal da empresa' })).toBeVisible()
  })

  test('teste recusado mostra o motivo e a ação dentro da tela', async ({ page }) => {
    const { api } = fiscalApi(saved(), (v) => ({ ok: false, result: 'admin_scope', message: 'Use uma chave sem o escopo admin.', integration: { ...v, lastTestResult: 'admin_scope', lastTestMessage: 'Use uma chave sem o escopo admin.' } }))
    await mockApi(page, api)
    await page.goto(PATH)
    await page.getByRole('button', { name: 'Testar conexão' }).click()
    await expect(page.getByRole('alert').filter({ hasText: 'Use uma chave sem o escopo admin.' })).toBeVisible()
    await expect(page.getByText('Recusada no teste')).toBeVisible()
  })

  test('tenant sem ambiente fiscal permitido não mostra o formulário', async ({ page }) => {
    const { api } = fiscalApi(empty({ allowed: false, allowedEnvironment: 'None' }), (v) => ({ ok: false, result: '', message: '', integration: v }))
    await mockApi(page, api)
    await page.goto(PATH)
    await expect(page.getByText('A emissão desta empresa só liga na virada para a produção.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Configurar' })).toHaveCount(0)
  })

  test('sem settings.edit a tela fica só de leitura', async ({ page }) => {
    const modules = allModules().map((m) => (m.code === 'settings' ? { ...m, actions: ['view'] } : m))
    const { api } = fiscalApi(tested(), (v) => ({ ok: true, result: 'ok', message: '', integration: v }))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: false, version: '3', modules }) })
    await page.goto(PATH)
    await expect(page.getByText('Ativa', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Testar conexão' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Alterar conexão' })).toHaveCount(0)
    await expect(page.getByText('Só o Proprietário altera e testa a integração.')).toBeVisible()
  })

  test('módulo fiscal não liberado mostra a tela padrão', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: true, version: '4', modules: allModules({ fiscal: { released: false } }) }) })
    await page.goto(PATH)
    await expect(page.getByText('Funcionalidade não liberada nessa versão do sistema.')).toBeVisible()
  })

  test('erro ao carregar oferece "Tentar de novo"', async ({ page }) => {
    let fail = true
    const { api } = fiscalApi(tested(), (v) => ({ ok: true, result: 'ok', message: '', integration: v }))
    const get = api['GET /api/v1/fiscal/integration']
    await mockApi(page, { ...api, 'GET /api/v1/fiscal/integration': (r) => (fail ? json(r, { title: 'Erro', detail: 'Falhou ao carregar a integração.' }, 500) : get(r)) })
    await page.goto(PATH)
    await expect(page.getByRole('alert').filter({ hasText: 'Não foi possível carregar.' })).toBeVisible()
    fail = false
    await page.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(page.getByText('Ativa', { exact: true })).toBeVisible()
  })

  test('a 400 px as seções empilham, os botões de 44 px aparecem sem rolar e não há rolagem horizontal', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    const { api } = fiscalApi(tested({ pendingInvoices: 2, oldestPendingSince: '2026-10-26T11:00:00Z' }), (v) => ({ ok: true, result: 'ok', message: 'Conexão testada', integration: v }))
    await mockApi(page, api)
    await page.goto(PATH)
    const button = page.getByRole('button', { name: 'Testar conexão' })
    await expect(button).toBeInViewport()
    expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(44)
    await expect(page.getByText('2 notas esperando a SEFAZ.', { exact: false })).toBeVisible()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
    await noSeriousViolations(page)
  })
})
