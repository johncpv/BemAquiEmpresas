import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

type Row = { id: string; provider: string; providerName: string; eventType: string; receivedAt: string; status: string; attempts: number; lastError: string | null; canViewContent: boolean; canReprocess: boolean; contentPurged: boolean }

const row = (over: Partial<Row>): Row => ({
  id: 'e1', provider: 'test', providerName: 'Provedor de teste', eventType: 'nota.autorizada', receivedAt: '2026-10-25T13:10:00Z', status: 'Processed', attempts: 1, lastError: null,
  canViewContent: true, canReprocess: false, contentPurged: false, ...over,
})

/** Backend em memória da FND-16 (as regras reais estão nos testes de integração .NET). */
function eventsApi(rows: Row[] = [row({}), row({ id: 'e2', status: 'DeadLettered', eventType: 'nota.rejeitada', attempts: 10, lastError: 'Falha no processamento (HttpRequestException).', canReprocess: true })]) {
  const calls: { method: string; path: string; query: string }[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/settings/integrations/events': (r) => {
      const url = new URL(r.request().url())
      calls.push({ method: 'GET', path: url.pathname, query: url.search })
      const status = url.searchParams.get('status')
      const provider = url.searchParams.get('provider')
      const items = rows.filter((x) => (!status || x.status === status) && (!provider || x.provider === provider))
      return json(r, { page: { items, page: 1, pageSize: 20, total: items.length }, providers: [{ code: 'test', name: 'Provedor de teste' }], deadLettered: rows.filter((x) => x.status === 'DeadLettered').length })
    },
    'GET /api/v1/settings/integrations/events/*/content': (r) => json(r, {
      id: 'e1', provider: 'test', eventType: 'nota.autorizada', receivedAt: '2026-10-25T13:10:00Z', purged: false,
      maskedJson: '{\n  "valor": 1520.35,\n  "pagador": {\n    "nome": "***",\n    "cpfCnpj": "***"\n  }\n}',
    }),
    'POST /api/v1/settings/integrations/events/*/reprocess': (r) => {
      calls.push({ method: 'POST', path: new URL(r.request().url()).pathname, query: '' })
      const target = rows.find((x) => r.request().url().includes(x.id))
      if (target) Object.assign(target, { status: 'Received', attempts: 0, lastError: null, canReprocess: false })
      return r.fulfill({ status: 204 })
    },
  }
  return { api, calls }
}

async function noSeriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
}

test.describe('Configurações → Integrações → Eventos recebidos (FND-16)', () => {
  test('com aviso parado abre em "Parados" e Reprocessar devolve o aviso para a fila', async ({ page }) => {
    const { api, calls } = eventsApi()
    await mockApi(page, api)
    await page.goto('/app/configuracoes/integracoes')
    await expect(page).toHaveURL(/\/app\/configuracoes\/integracoes\/eventos\?situacao=DeadLettered$/)
    await expect(page.getByRole('heading', { level: 1, name: 'Avisos recebidos de sistemas externos' })).toBeVisible()
    await expect(page.getByRole('main').getByRole('navigation', { name: 'Configurações' }).getByRole('link', { name: 'Integrações' })).toHaveAttribute('aria-current', 'page')
    const table = page.getByRole('table', { name: 'Eventos recebidos' })
    await expect(table.getByText('Parado', { exact: true })).toBeVisible()
    await expect(table.getByText('nota.autorizada')).toHaveCount(0)
    await noSeriousViolations(page)
    await table.getByRole('button', { name: 'Reprocessar', exact: true }).click()
    const dialog = page.getByRole('alertdialog').or(page.getByRole('dialog'))
    await dialog.getByRole('button', { name: 'Reprocessar' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Evento reenviado para processamento' }).first()).toBeVisible()
    expect(calls.some((c) => c.method === 'POST' && c.path.endsWith('/e2/reprocess'))).toBe(true)
  })

  test('filtro na URL e "Ver conteúdo" com os dados pessoais mascarados', async ({ page }) => {
    const { api, calls } = eventsApi([row({})])
    await mockApi(page, api)
    await page.goto('/app/configuracoes/integracoes/eventos')
    await expect(page.getByRole('table', { name: 'Eventos recebidos' }).getByText('Processado')).toBeVisible()
    await page.getByRole('combobox', { name: 'Provedor' }).selectOption('test')
    await expect(page).toHaveURL(/provedor=test/)
    await expect.poll(() => calls.some((c) => c.query.includes('provider=test'))).toBe(true)
    await page.getByRole('button', { name: 'Ver conteúdo', exact: true }).click()
    const panel = page.getByRole('dialog', { name: 'Conteúdo do aviso' })
    await expect(panel.getByText('Dados pessoais mascarados')).toBeVisible()
    await expect(panel.getByLabel('Conteúdo mascarado')).toContainText('"nome": "***"')
    await expect(panel.getByLabel('Conteúdo mascarado')).toContainText('1520.35')
    await noSeriousViolations(page)
  })

  test('sem aviso: estado vazio que diz quando os avisos aparecem', async ({ page }) => {
    const { api } = eventsApi([])
    await mockApi(page, api)
    await page.goto('/app/configuracoes/integracoes/eventos')
    await expect(page.getByText('Nenhum aviso recebido ainda.')).toBeVisible()
    await expect(page.getByText('Os avisos aparecem aqui quando a nota fiscal ou o banco responderem.')).toBeVisible()
  })

  test('erro ao carregar oferece "Tentar de novo"', async ({ page }) => {
    let fail = true
    const { api } = eventsApi()
    const list = api['GET /api/v1/settings/integrations/events']
    await mockApi(page, { ...api, 'GET /api/v1/settings/integrations/events': (r) => (fail ? json(r, { title: 'Erro', detail: 'Falhou ao carregar os avisos.' }, 500) : list(r)) })
    await page.goto('/app/configuracoes/integracoes/eventos?situacao=Processed')
    await expect(page.getByRole('alert').filter({ hasText: 'Não foi possível carregar.' })).toBeVisible()
    fail = false
    await page.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(page.getByRole('table', { name: 'Eventos recebidos' })).toBeVisible()
  })

  test('sem ver o módulo dono a aba Integrações some (CA 16)', async ({ page }) => {
    const modules = allModules().map((m) => (m.code === 'finance' || m.code === 'fiscal' ? { ...m, enabled: false, actions: [] } : m))
    await mockApi(page, { 'GET /api/v1/me/permissions': (r) => json(r, { permissions: [], isOwner: false, version: '2', modules }) })
    await page.goto('/app/configuracoes/seguranca')
    const tabs = page.getByRole('main').getByRole('navigation', { name: 'Configurações' })
    await expect(tabs.getByRole('link', { name: 'Segurança' })).toBeVisible()
    await expect(tabs.getByRole('link', { name: 'Integrações' })).toHaveCount(0)
  })

  test('a 400 px provedor, tipo, recebido em e situação ficam visíveis; tentativas e erro na linha expansível', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    const { api } = eventsApi()
    await mockApi(page, api)
    await page.goto('/app/configuracoes/integracoes/eventos')
    const table = page.getByRole('table', { name: 'Eventos recebidos' })
    await expect(table.getByRole('columnheader', { name: 'Situação' })).toBeVisible()
    await expect(table.getByRole('columnheader', { name: 'Tentativas' })).toBeHidden()
    await table.getByRole('button', { name: /Detalhes de aviso de Provedor de teste/ }).first().click()
    await expect(table.getByText('10 tentativas')).toBeVisible()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
