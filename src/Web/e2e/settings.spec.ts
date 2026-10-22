import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const profile = (exists: boolean) => ({
  exists, document: '12345678000195', legalName: 'Konebras Elevadores Ltda', tradeName: 'Konebras', stateRegistration: null, municipalRegistration: null,
  zipCode: null, street: null, number: null, complement: null, district: null, city: null, cityIbge: null, state: null, phone: null, email: null,
  id: exists ? '0199f0e0-0000-7000-8000-0000000000e1' : null, logo: { status: 'None', version: null, pendingNew: false },
})

const rows = () => [
  { docType: 'ORC', label: 'Orçamento', prefix: 'ORC', yearly: true, digits: 4, lastIssued: 16, hasIssued: true, next: 'ORC-2026-0017', customized: false },
  { docType: 'CTR', label: 'Contrato', prefix: 'CTR', yearly: true, digits: 4, lastIssued: 0, hasIssued: false, next: 'CTR-2026-0001', customized: false },
]

/** Backend em memória da CAD-06 (as regras reais estão nos testes de integração .NET). */
function settingsApi() {
  let company = profile(false)
  const numbering = rows()
  let params = { payrollChargesPct: 80, pricingMode: 'Markup', chargesSettingId: '0199f0e0-0000-7000-8000-0000000000f1', pricingSettingId: '0199f0e0-0000-7000-8000-0000000000f2' }
  const calls: { path: string; body: unknown }[] = []
  const log = (r: Route) => { const body = r.request().postDataJSON() as unknown; calls.push({ path: new URL(r.request().url()).pathname, body }); return body }
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/settings/company': (r) => json(r, company),
    'PUT /api/v1/settings/company': (r) => { company = { ...company, ...(log(r) as object), exists: true, id: '0199f0e0-0000-7000-8000-0000000000e1' }; return json(r, company) },
    'GET /api/v1/settings/company/cep/12280000': (r) => json(r, { zipCode: '12280000', street: 'Rua das Flores', district: 'Centro', city: 'Caçapava', state: 'SP', ibgeCode: '3508504' }),
    'GET /api/v1/settings/numbering': (r) => json(r, numbering),
    'PUT /api/v1/settings/numbering/*': (r) => {
      const b = log(r) as { prefix: string; digits: number }
      const row = numbering[0]
      Object.assign(row, b, { customized: true, next: `${b.prefix}-2026-${String(row.lastIssued + 1).padStart(b.digits, '0')}` })
      return json(r, row)
    },
    'GET /api/v1/settings/parameters': (r) => json(r, params),
    'PUT /api/v1/settings/parameters/payroll-charges': (r) => { params = { ...params, payrollChargesPct: (log(r) as { pct: number }).pct }; return json(r, params) },
    'PUT /api/v1/settings/parameters/pricing-mode': (r) => { params = { ...params, pricingMode: (log(r) as { mode: string }).mode }; return json(r, params) },
  }
  return { api, calls }
}

async function noSeriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
}

const confirmation = (page: Page, title: string) => page.getByRole('dialog', { name: title }).or(page.getByRole('alertdialog', { name: title }))

test.describe('configurações da empresa (CAD-06)', () => {
  test('Configurações abre na Empresa, com dados do catálogo, CNPJ só leitura e CEP que preenche (CA 1 a 3)', async ({ page }) => {
    const { api, calls } = settingsApi()
    await mockApi(page, api)
    await page.goto('/app/configuracoes')
    await expect(page).toHaveURL(/\/app\/configuracoes\/empresa$/)
    await expect(page.getByRole('main').getByRole('navigation', { name: 'Configurações' }).getByRole('link')).toHaveText(['Empresa', 'Numeração', 'Parâmetros', 'Segurança', 'Inteligência artificial', 'Importação do Dolibarr'])
    await expect(page.getByText('Os dados vieram do cadastro da plataforma.')).toBeVisible()
    await expect(page.getByRole('textbox', { name: 'CNPJ' })).toHaveValue('12.345.678/0001-95')
    await expect(page.getByRole('textbox', { name: 'CNPJ' })).toBeDisabled()
    await expect(page.getByLabel(/Razão social/)).toHaveValue('Konebras Elevadores Ltda')
    await page.getByLabel('CEP').fill('12280000')
    await expect(page.getByLabel('Cidade')).toHaveValue('Caçapava')
    await page.getByLabel('Número').fill('100')
    await noSeriousViolations(page)
    await page.getByRole('button', { name: 'Salvar dados da empresa' }).click()
    await expect(page.getByText('Dados da empresa salvos')).toBeVisible()
    const put = calls.find((c) => c.path === '/api/v1/settings/company')
    expect(put?.body).toMatchObject({ legalName: 'Konebras Elevadores Ltda', city: 'Caçapava', number: '100' })
    expect(put?.body).not.toHaveProperty('document')
    await expect(page.getByText('Os dados vieram do cadastro da plataforma.')).toBeHidden()
  })

  test('Numeração pelo teclado: prévia ao vivo, confirmação do que não tem volta e reinício anual travado (CA 9, 10 e 12)', async ({ page }) => {
    const { api, calls } = settingsApi()
    await mockApi(page, api)
    await page.goto('/app/configuracoes/numeracao')
    await expect(page.getByRole('cell', { name: 'ORC-2026-0017' })).toBeVisible()
    await page.getByRole('button', { name: 'Orçamento' }).focus()
    await page.keyboard.press('Enter')
    const panel = page.getByRole('dialog', { name: 'Numeração: Orçamento' })
    await expect(panel).toBeVisible()
    await expect(panel.getByRole('checkbox', { name: 'Reinicia por ano' })).toBeDisabled()
    await panel.getByRole('textbox', { name: /Prefixo/ }).fill('orcv')
    await panel.getByRole('spinbutton', { name: /Dígitos/ }).fill('5')
    await expect(panel.getByText('ORCV-2026-00017')).toBeVisible()
    await noSeriousViolations(page)
    await panel.getByRole('button', { name: 'Salvar numeração' }).click()
    const confirm = confirmation(page, 'Salvar numeração?')
    await expect(confirm.getByText('Os números já emitidos não mudam.')).toBeVisible()
    await expect(confirm.getByText('ORCV-2026-00017')).toBeVisible()
    await confirm.getByRole('button', { name: 'Salvar numeração' }).click()
    await expect(page.getByText('Numeração salva')).toBeVisible()
    await expect(page.getByRole('cell', { name: 'ORCV-2026-00017' })).toBeVisible()
    expect(calls.find((c) => c.path === '/api/v1/settings/numbering/ORC')?.body).toEqual({ prefix: 'ORCV', yearly: true, digits: 5, nextNumber: null })
  })

  test('próximo número menor que o último emitido é recusado na tela (CA 11)', async ({ page }) => {
    const { api } = settingsApi()
    await mockApi(page, api)
    await page.goto('/app/configuracoes/numeracao')
    await page.getByRole('button', { name: 'Orçamento' }).click()
    const panel = page.getByRole('dialog', { name: 'Numeração: Orçamento' })
    await panel.getByRole('textbox', { name: /Próximo número/ }).fill('10')
    await expect(panel.getByText('O próximo número precisa ser maior que o último emitido (16).')).toBeVisible()
    await expect(panel.getByRole('button', { name: 'Salvar numeração' })).toBeDisabled()
    await panel.getByRole('textbox', { name: /Próximo número/ }).fill('500')
    await expect(panel.getByText('ORC-2026-0500')).toBeVisible()
  })

  test('Parâmetros: % de encargos com confirmação do impacto e modo de precificação (CA 15, 16 e 18)', async ({ page }) => {
    const { api, calls } = settingsApi()
    await mockApi(page, api)
    await page.goto('/app/configuracoes/parametros')
    const pct = page.getByRole('textbox', { name: /% de encargos estimados/ })
    await expect(pct).toHaveValue('80,00')
    await pct.fill('72,5')
    await pct.blur()
    await page.getByRole('button', { name: 'Salvar % de encargos' }).click()
    const charges = confirmation(page, 'Salvar % de encargos?')
    await expect(charges.getByText('Os cargos com custo-hora calculado serão recalculados.')).toBeVisible()
    await charges.getByRole('button', { name: 'Salvar % de encargos' }).click()
    await expect(page.getByText('% de encargos salvo')).toBeVisible()
    expect(calls.find((c) => c.path.endsWith('/payroll-charges'))?.body).toEqual({ pct: 72.5 })

    await page.getByRole('radio', { name: 'Margem sobre o preço' }).click()
    await page.getByRole('button', { name: 'Salvar modo de precificação' }).click()
    await confirmation(page, 'Salvar modo de precificação?').getByRole('button', { name: 'Salvar modo de precificação' }).click()
    await expect(page.getByText('Modo de precificação salvo')).toBeVisible()
    expect(calls.find((c) => c.path.endsWith('/pricing-mode'))?.body).toEqual({ mode: 'Margin' })
    await noSeriousViolations(page)
  })

  test('Parâmetros da produção: custo-hora por apontamento (padrão desligado) e limite do apontamento aberto (PRD-04)', async ({ page }) => {
    const { api, calls } = settingsApi()
    let production = { showLaborEntryCost: false, openLaborAlertHours: 12, showLaborEntryCostId: null as string | null, openLaborAlertHoursId: null as string | null }
    await mockApi(page, {
      ...api,
      'GET /api/v1/production/parameters': (r: Route) => json(r, production),
      'PUT /api/v1/production/parameters': (r: Route) => {
        calls.push({ path: '/api/v1/production/parameters', body: r.request().postDataJSON() })
        production = { ...production, ...(r.request().postDataJSON() as object), showLaborEntryCostId: 'pp1', openLaborAlertHoursId: 'pp2' }
        return json(r, production)
      },
    })
    await page.goto('/app/configuracoes/parametros')
    const show = page.getByRole('checkbox', { name: 'Exibir custo-hora em cada apontamento' })
    await expect(show).not.toBeChecked()
    const hours = page.getByRole('textbox', { name: /Apontamento aberto vira alerta/ })
    await expect(hours).toHaveValue('12')
    await hours.fill('80')
    await expect(page.getByText('Informe de 1 a 72 horas.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Salvar parâmetros da produção' })).toBeDisabled()
    await hours.fill('8')
    await show.check()
    await page.getByRole('button', { name: 'Salvar parâmetros da produção' }).click()
    await expect(page.getByText('Parâmetros da produção salvos')).toBeVisible()
    expect(calls.find((c) => c.path === '/api/v1/production/parameters')?.body).toEqual({ showLaborEntryCost: true, openLaborAlertHours: 8 })
    await expect(page.getByText('Histórico de alterações').last()).toBeVisible()
  })

  test('com settings.view e sem settings.edit, as abas abrem só para leitura (CA 20)', async ({ page }) => {
    const { api } = settingsApi()
    const modules = allModules().map((m) => (m.code === 'settings' ? { ...m, actions: ['view'] } : m))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: ['settings.view'], isOwner: false, version: '2', modules }) })
    await page.goto('/app/configuracoes/empresa')
    await expect(page.getByLabel(/Razão social/)).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Salvar dados da empresa' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Trocar logo' })).toHaveCount(0)
    await page.goto('/app/configuracoes/parametros')
    await expect(page.getByRole('textbox', { name: /% de encargos estimados/ })).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Salvar % de encargos' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Salvar parâmetros da produção' })).toHaveCount(0)
  })
})
