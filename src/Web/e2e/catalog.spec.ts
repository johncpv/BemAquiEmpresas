import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const CATALOG_MODULE = { code: 'catalog', name: 'Catálogo', area: 'Comercial', order: 45, description: 'Produtos à venda.', released: true, enabled: true, hidden: false }
const GERAL = { id: 'cat-geral', name: 'Geral', parentId: null, sortOrder: 0, defaultMarginPercent: null, defaultMarkupPercent: null, isActive: true, isDefault: true, productCount: 0 }
const UNITS = [{ id: 'u-un', code: 'UN', name: 'Unidade' }, { id: 'u-kg', code: 'KG', name: 'Quilograma' }]

type Json = Record<string, unknown>

/** /me/permissions de um tenant de Comércio, com o catálogo liberado e as ações dadas. */
const permissions = (actions: string[]) => (r: Route) =>
  json(r, { permissions: [], isOwner: false, version: '7', profiles: ['retail'], modules: [...allModules(), { ...CATALOG_MODULE, actions }] })

/** Backend em memória da CAT-01 (as regras reais estão nos testes de integração .NET). */
function catalogApi() {
  const categories: Json[] = [{ ...GERAL }]
  const products: Json[] = []
  const posted: Json[] = []
  const detail = (p: Json) => p
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/catalog/units': (r) => json(r, UNITS),
    'GET /api/v1/catalog/categories': (r) => json(r, categories),
    'POST /api/v1/catalog/categories': (r) => {
      const body = r.request().postDataJSON() as Json
      const created = { ...GERAL, ...body, id: `cat-${categories.length}`, isDefault: false, productCount: 0, isActive: true }
      categories.push(created)
      return json(r, created, 201)
    },
    'GET /api/v1/catalog/products': (r) => json(r, { items: products.map((p) => ({ ...p, categoryName: 'Geral', firstGtin: (p.barcodes as Json[])[0]?.gtin ?? null, barcodeCount: (p.barcodes as Json[]).length, variantCount: 0 })), page: 1, pageSize: 20, total: products.length }),
    'POST /api/v1/catalog/products': (r) => {
      const body = r.request().postDataJSON() as Json
      posted.push(body)
      const created = {
        id: 'p1', saleName: body.saleName, categoryId: 'cat-geral', categoryName: 'Geral', itemId: 'i1', itemCode: 'MER-000001', itemName: body.saleName, unitId: 'u-un', unitCode: 'UN',
        ncm: null, plu: body.plu, isWeighable: false, sellablePos: true, sellableOnline: false, variantOfProductId: null, parentName: null, variantAttributes: null, hasVariants: false,
        isActive: true, price: body.price, priceListId: 'pl', priceListName: 'Balcão', priceListVersion: 1,
        barcodes: (body.barcodes as Json[]).map((b, i) => ({ id: `b${i}`, gtin: b.gtin, packQuantity: b.packQuantity, createdAt: '2026-11-17T12:00:00Z' })),
        variants: [], createdAt: '2026-11-17T12:00:00Z', version: 1, photoId: null, cost: { averageCost: 0, marginPercent: 100, markupPercent: null, suggestedPrice: null },
      }
      products.push(created)
      return json(r, created, 201)
    },
    'GET /api/v1/catalog/products/*': (r) => json(r, detail(products[0])),
    'GET /api/v1/catalog/products/*/price-history': (r) => json(r, [{ id: 'h1', priceListName: 'Balcão', previousPrice: null, newPrice: products[0]?.price ?? 0, listVersion: 1, changedAt: '2026-11-17T12:00:00Z', changedByName: 'Ana Souza' }]),
    'POST /api/v1/catalog/products/*/barcodes': (r) => {
      const body = r.request().postDataJSON() as Json
      const added = { id: 'b-new', gtin: body.gtin, packQuantity: body.packQuantity, createdAt: '2026-11-17T12:05:00Z' }
      ;(products[0].barcodes as Json[]).push(added)
      return json(r, added, 201)
    },
    'PUT /api/v1/catalog/products/*/price': (r) => {
      const body = r.request().postDataJSON() as Json
      products[0] = { ...products[0], price: body.price, priceListVersion: (products[0].priceListVersion as number) + 1 }
      return json(r, products[0])
    },
    'GET /api/v1/catalog/products/*/barcodes': (r) => json(r, products[0]?.barcodes ?? []),
  }
  return { api, posted }
}

const noHorizontalScroll = async (page: import('@playwright/test').Page) =>
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)

test.describe('Catálogo (CAT-01)', () => {
  test('gerente cadastra o produto com item novo, código de barras e preço, e segue os próximos passos (CA 3, 5, 6 e 8)', async ({ page }) => {
    const { api, posted } = catalogApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': permissions(['view', 'create', 'edit', 'delete', 'edit_price', 'view_cost']) })
    await page.goto('/app/catalogo')
    await expect(page).toHaveURL(/\/app\/catalogo\/produtos$/)
    await expect(page.getByText('Nenhum produto ainda.')).toBeVisible()
    await page.getByRole('link', { name: 'Novo produto' }).first().click()
    await expect(page).toHaveURL(/\/app\/catalogo\/produtos\/novo$/)

    const main = page.getByRole('main')
    await main.getByRole('textbox', { name: /^Nome de venda/ }).fill('Refrigerante cola 2 L')
    await expect(main.getByRole('combobox', { name: /^Unidade/ })).toHaveValue('u-un')
    const gtin = main.getByRole('textbox', { name: 'Código de barras (GTIN)' })
    await gtin.fill('7891000315508')
    await expect(page.getByText('Código de barras inválido. Confira os dígitos.')).toBeVisible()
    await gtin.fill('7891000315507')
    await expect(page.getByText('Código de barras inválido. Confira os dígitos.')).toHaveCount(0)
    await main.getByRole('textbox', { name: 'Preço de venda' }).fill('9,90')
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await noHorizontalScroll(page)
    await main.getByRole('button', { name: 'Salvar produto' }).click()

    await expect(page.getByText('Produto Refrigerante cola 2 L salvo')).toBeVisible()
    expect(posted[0]).toMatchObject({ saleName: 'Refrigerante cola 2 L', itemId: null, newItem: { unitId: 'u-un', code: null }, price: 9.9, barcodes: [{ gtin: '7891000315507', packQuantity: 1 }] })
    await expect(page).toHaveURL(/\/app\/catalogo\/produtos\/p1/)
    const steps = page.getByRole('region', { name: 'Próximos passos' })
    await expect(steps.getByRole('button', { name: 'Gerar grade' })).toBeVisible()

    await page.getByRole('button', { name: 'Adicionar código', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: 'Adicionar código de barras' })
    await dialog.getByRole('textbox', { name: 'Código de barras (GTIN)' }).fill('17891000315504')
    await dialog.getByRole('textbox', { name: /Quantidade da embalagem/ }).fill('12')
    await dialog.getByRole('button', { name: 'Adicionar código' }).click()
    await expect(page.getByRole('list', { name: 'Códigos de barras' }).getByText('Embalagem com 12')).toBeVisible()

    await page.getByRole('button', { name: 'Alterar preço', exact: true }).click()
    const price = page.getByRole('dialog', { name: /Alterar o preço/ })
    await price.getByRole('textbox', { name: 'Preço de venda' }).fill('10,50')
    await price.getByRole('button', { name: 'Alterar preço' }).click()
    await expect(page.getByText('Tabela Balcão, versão 2.')).toBeVisible()
    await expect(page.getByRole('complementary', { name: 'Custo e margem' })).toBeVisible()
  })

  test('a 400 px, quem não tem "Alterar preço" nem "Ver custo" não vê preço editável nem margem, e cria categoria com margem padrão (CA 9, 10 e 14)', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 860 })
    const { api } = catalogApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': permissions(['view', 'create', 'edit']) })
    await page.goto('/app/catalogo/produtos/novo')
    const main = page.getByRole('main')
    await expect(main.getByRole('textbox', { name: /^Nome de venda/ })).toBeVisible()
    await expect(main.getByRole('textbox', { name: 'Preço de venda' })).toHaveCount(0)
    await noHorizontalScroll(page)

    await page.goto('/app/catalogo/categorias')
    await expect(page.getByText('Só a categoria Geral, por enquanto.')).toBeVisible()
    await page.getByRole('button', { name: 'Nova categoria' }).first().click()
    const panel = page.getByRole('dialog', { name: 'Nova categoria' })
    await panel.getByRole('textbox', { name: /^Nome/ }).fill('Bebidas')
    await panel.getByRole('combobox', { name: 'Preço sugerido por' }).selectOption('margin')
    await panel.getByRole('textbox', { name: 'Margem padrão' }).fill('30')
    await panel.getByRole('button', { name: 'Salvar categoria' }).click()
    await expect(page.getByText('Categoria salva')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Bebidas' })).toBeVisible()
    await noHorizontalScroll(page)
  })

  test('fábrica: o catálogo não aparece no menu e a URL direta mostra "não liberado" (oculto, ADR 0046)', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', profiles: ['factory'], modules: [...allModules(), { ...CATALOG_MODULE, released: false, hidden: true, actions: [] }] }) })
    await page.goto('/app/catalogo/produtos')
    await expect(page.getByText('Funcionalidade não liberada nessa versão do sistema.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Catálogo' })).toHaveCount(0)
  })
})
