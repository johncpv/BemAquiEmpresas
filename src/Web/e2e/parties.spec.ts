import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

interface Party { id: string; kind: 'PF' | 'PJ'; document: string | null; legalName: string; tradeName: string | null; roles: string[]; isActive: boolean; addresses: unknown[]; contacts: unknown[] }

/** Backend em memória só para o fluxo da CAD-01a (as regras reais estão nos testes de integração .NET). */
function partiesApi() {
  const parties: Party[] = []
  const detail = (p: Party) => ({ ...p, stateRegistration: null, municipalRegistration: null, taxRegime: 'NotInformed', email: null, phone: null, notes: null, createdAt: '2026-10-18T12:00:00Z' })
  const byPath = (route: Route) => parties.find((p) => route.request().url().includes(p.id))
  return {
    parties,
    handlers: {
      'GET /api/v1/parties': (r: Route) => json(r, { items: parties.map((p) => ({ id: p.id, kind: p.kind, legalName: p.legalName, tradeName: p.tradeName, document: p.document, roles: p.roles, city: 'Campinas', state: 'SP', isActive: p.isActive })), page: 1, pageSize: 20, total: parties.length }),
      'GET /api/v1/parties/document-check': (r: Route) => {
        const doc = new URL(r.request().url()).searchParams.get('document') ?? ''
        const found = parties.find((p) => p.document?.replace(/\W/g, '') === doc)
        return json(r, { valid: true, exists: !!found, id: found?.id ?? null, name: found?.legalName ?? null, deleted: false, roles: found?.roles ?? [] })
      },
      'POST /api/v1/parties': async (r: Route) => {
        const body = r.request().postDataJSON() as { kind: 'PF' | 'PJ'; document: string | null; legalName: string; tradeName: string | null; roles: string[]; billingAddress: unknown }
        const p: Party = { id: crypto.randomUUID(), kind: body.kind, document: body.document ? body.document.replace(/^(\w{2})(\w{3})(\w{3})(\w{4})(\d{2})$/, '$1.$2.$3/$4-$5') : null, legalName: body.legalName, tradeName: body.tradeName, roles: body.roles, isActive: true, addresses: body.billingAddress ? [{ id: crypto.randomUUID(), ...(body.billingAddress as object), isDefault: true }] : [], contacts: [] }
        parties.push(p)
        return json(r, detail(p), 201)
      },
      'GET /api/v1/parties/*': (r: Route) => { const p = byPath(r); return p ? json(r, detail(p)) : json(r, { code: 'not_found', detail: 'Terceiro não encontrado.' }, 404) },
      'PUT /api/v1/parties/*': (r: Route) => { const p = byPath(r)!; Object.assign(p, { roles: (r.request().postDataJSON() as { roles: string[] }).roles }); return json(r, detail(p)) },
    } as Record<string, (r: Route) => Promise<void> | void>,
  }
}

test.describe('terceiros (CAD-01a)', () => {
  test('cadastra cliente com endereço; o mesmo CNPJ como fornecedor vira "Adicionar papel" (CA 2, 4, 8)', async ({ page }) => {
    const api = partiesApi()
    await mockApi(page, api.handlers)
    await page.goto('/app/cadastros/terceiros')
    await expect(page.getByText('Cadastre o primeiro cliente ou fornecedor.')).toBeVisible()
    await page.getByRole('link', { name: 'Novo terceiro' }).first().click()

    await page.getByRole('textbox', { name: 'CPF ou CNPJ' }).fill('47.225.005/0001-86')
    await page.getByRole('textbox', { name: /Razão social/ }).fill('WSP Equipamentos por Elevação Ltda')
    await page.getByLabel('CEP').fill('13069000')
    await page.getByLabel('Logradouro').fill('Rua das Indústrias')
    await page.getByLabel('Número').fill('120')
    await page.getByLabel('Bairro').fill('Distrito Industrial')
    await page.getByLabel('Cidade').fill('Campinas')
    await page.getByLabel('UF').selectOption('SP')
    await page.getByRole('button', { name: 'Salvar terceiro' }).first().click()

    await expect(page.getByText('Terceiro salvo')).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: 'WSP Equipamentos por Elevação Ltda' })).toBeVisible()
    await expect(page.getByRole('region', { name: 'Próximos passos' })).toBeVisible()

    await page.goto('/app/cadastros/terceiros/novo?papel=Supplier')
    await expect(page.getByRole('checkbox', { name: /Fornecedor/ })).toBeChecked()
    await page.getByRole('textbox', { name: 'CPF ou CNPJ' }).fill('47225005000186')
    await page.getByRole('textbox', { name: /Razão social/ }).focus()
    await expect(page.getByText('Este CNPJ já está cadastrado como')).toBeVisible()
    await page.getByRole('button', { name: 'Adicionar papel Fornecedor' }).click()
    await expect(page.getByText('Papel adicionado')).toBeVisible()
    expect(api.parties).toHaveLength(1)
    expect(api.parties[0].roles).toEqual(['Customer', 'Supplier'])
  })

  test('lista e detalhe sem violação séria, sem rolagem horizontal e abas pelo teclado (CA 16, 22, 23)', async ({ page }) => {
    test.setTimeout(60_000) // axe em várias páginas
    const api = partiesApi()
    api.parties.push({ id: '0199f0e0-0000-7000-8000-000000000001', kind: 'PF', document: '***.456.789-**', legalName: 'José Instalador', tradeName: null, roles: ['Installer'], isActive: true, addresses: [], contacts: [] })
    await mockApi(page, api.handlers)
    await page.goto('/app/cadastros/terceiros')
    await expect(page.getByRole('link', { name: 'José Instalador' })).toBeVisible()
    if (test.info().project.name === 'desktop') await expect(page.getByText('***.456.789-**').first()).toBeVisible() // coluna secundária some a 400 px
    await page.getByLabel('Papel').selectOption('Installer')
    await expect(page).toHaveURL(/papel=Installer/)

    for (const path of ['/app/cadastros/terceiros', '/app/cadastros/terceiros/novo', '/app/cadastros/terceiros/0199f0e0-0000-7000-8000-000000000001']) {
      await page.goto(path)
      await page.getByRole('heading', { level: 1 }).waitFor()
      const results = await new AxeBuilder({ page }).analyze()
      expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
    }

    await page.getByRole('tab', { name: 'Dados' }).focus()
    await page.keyboard.press('ArrowRight')
    await expect(page.getByRole('tab', { name: /Endereços/ })).toHaveAttribute('aria-selected', 'true')
    await expect(page).toHaveURL(/aba=enderecos/)
    await expect(page.getByText('Nenhum endereço ainda.')).toBeVisible()
  })
})
