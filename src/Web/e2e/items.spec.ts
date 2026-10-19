import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

type Item = Record<string, unknown> & { id: string; code: string; name: string; type: string }

const UN = { id: '0199f0e0-0000-7000-8000-0000000000b1', code: 'UN', name: 'Unidade', isActive: true, seedKey: 'un', itemCount: 0 }
const KG = { id: '0199f0e0-0000-7000-8000-0000000000b2', code: 'KG', name: 'Quilograma', isActive: true, seedKey: 'kg', itemCount: 0 }
const WARRANTIES = [
  { id: 'w1', itemType: 'RawMaterial', months: null }, { id: 'w2', itemType: 'Component', months: 3 }, { id: 'w3', itemType: 'Finished', months: 12 },
  { id: 'w4', itemType: 'Consumable', months: null }, { id: 'w5', itemType: 'Service', months: 3 },
]

/** Backend em memória da CAD-02a (as regras reais estão nos testes de integração .NET). */
function itemsApi() {
  const items: Item[] = []
  const posted: Record<string, unknown>[] = []
  const detail = (b: Record<string, unknown>, id: string): Item => ({
    ...b, id, code: String(b.code), name: String(b.name), type: String(b.type), unitCode: b.unitId === KG.id ? 'KG' : 'UN', categoryName: null, averageCost: 0, isActive: true, importSource: null, createdAt: '2026-10-19T12:00:00Z',
  }) as Item
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/inventory/units': (r) => json(r, [UN, KG]),
    'GET /api/v1/inventory/categories': (r) => json(r, []),
    'GET /api/v1/inventory/warranty-defaults': (r) => json(r, WARRANTIES),
    'GET /api/v1/inventory/items': (r) => json(r, { items, page: 1, pageSize: 20, total: items.length }),
    'GET /api/v1/inventory/items/search': (r) => json(r, items.map((i) => ({ id: i.id, code: i.code, name: i.name, type: i.type }))),
    'POST /api/v1/inventory/items': (r) => {
      const b = r.request().postDataJSON() as Record<string, unknown>
      posted.push(b)
      const item = detail(b, crypto.randomUUID())
      items.push(item)
      return json(r, item, 201)
    },
    'GET /api/v1/inventory/items/*': (r) => { const i = items.find((x) => r.request().url().includes(x.id)); return i ? json(r, i) : json(r, { code: 'item_not_found', detail: 'Item não encontrado.' }, 404) },
  }
  return { api, posted }
}

async function noSeriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
}

test.describe('itens (CAD-02a)', () => {
  test('cadastra matéria-prima só pelo teclado: o tipo sugere o lote e o NCM é exigido (CA 6, 7 e 10)', async ({ page }) => {
    const { api, posted } = itemsApi()
    await mockApi(page, api)
    await page.goto('/app/estoque')
    await expect(page).toHaveURL(/\/app\/estoque\/itens$/)
    await expect(page.getByText('Cadastre o primeiro item: matéria-prima, componente ou produto acabado.')).toBeVisible()
    await page.getByRole('link', { name: 'Novo item' }).first().focus()
    await page.keyboard.press('Enter')
    await expect(page.getByRole('heading', { name: 'Novo item' })).toBeVisible()
    await page.getByRole('combobox', { name: /Tipo/ }).focus()
    await page.keyboard.press('ArrowDown')
    await expect(page.getByRole('combobox', { name: /Tipo/ })).toHaveValue('RawMaterial')
    await expect(page.getByRole('checkbox', { name: 'Controla lote' })).toBeChecked()
    await expect(page.getByRole('checkbox', { name: 'Controla número de série' })).not.toBeChecked()
    await page.keyboard.press('Tab')
    await page.keyboard.type('MP-0001')
    await page.keyboard.press('Tab')
    await page.keyboard.type('Chapa de aço 3 mm')
    await page.getByRole('combobox', { name: /Unidade de estoque/ }).focus()
    await page.keyboard.press('ArrowDown')
    await page.getByRole('button', { name: 'Salvar item' }).focus()
    await page.keyboard.press('Enter')
    await expect(page.getByText('Preencha o NCM (8 dígitos) para o item ativo.')).toBeVisible()
    expect(posted).toHaveLength(0)
    await page.getByRole('textbox', { name: /NCM/ }).focus()
    await page.keyboard.type('84313110')
    await expect(page.getByRole('textbox', { name: /NCM/ })).toHaveValue('8431.31.10')
    await noSeriousViolations(page)
    await page.getByRole('button', { name: 'Salvar item' }).focus()
    await page.keyboard.press('Enter')
    await expect(page.getByText('Item salvo')).toBeVisible()
    expect(posted[0]).toMatchObject({ type: 'RawMaterial', code: 'MP-0001', unitId: UN.id, ncm: '84313110', tracksLot: true, tracksSerial: false, warrantyMonths: null })
    await expect(page.getByRole('heading', { name: 'MP-0001 — Chapa de aço 3 mm' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Duplicar este item' })).toBeVisible()
  })

  test('produto acabado vem com série e 12 meses; duplicar copia tudo menos o código (CA 10, 12 e 18)', async ({ page }) => {
    const { api, posted } = itemsApi()
    await mockApi(page, api)
    await page.goto('/app/estoque/itens/novo')
    await page.getByRole('combobox', { name: /Tipo/ }).selectOption('Finished')
    await expect(page.getByRole('checkbox', { name: 'Controla número de série' })).toBeChecked()
    await expect(page.getByRole('textbox', { name: /Garantia/ })).toHaveValue('12')
    await page.getByRole('textbox', { name: /Código/ }).fill('ELV-1000')
    await page.getByRole('textbox', { name: /^Nome/ }).fill('Elevador de carga 1.000 kg')
    await page.getByRole('combobox', { name: /Unidade de estoque/ }).selectOption(UN.id)
    await page.getByRole('textbox', { name: /NCM/ }).fill('84281000')
    await page.getByRole('button', { name: 'Salvar item' }).click()
    await expect(page.getByText('Item salvo')).toBeVisible()

    await page.getByRole('button', { name: 'Duplicar item' }).click()
    await expect(page.getByRole('heading', { name: 'Novo item (cópia de ELV-1000)' })).toBeVisible()
    await expect(page.getByRole('textbox', { name: /^Nome/ })).toHaveValue('Elevador de carga 1.000 kg')
    await expect(page.getByRole('textbox', { name: /Código/ })).toHaveValue('')
    await expect(page.getByRole('checkbox', { name: 'Controla número de série' })).toBeChecked()
    expect(posted).toHaveLength(1)
  })

  test('Ctrl K encontra o item a partir de 2 letras (CA 20)', async ({ page }) => {
    const { api } = itemsApi()
    await mockApi(page, api)
    await page.goto('/app/estoque/itens/novo')
    await page.getByRole('combobox', { name: /Tipo/ }).selectOption('Consumable')
    await page.getByRole('textbox', { name: /Código/ }).fill('LUV-01')
    await page.getByRole('textbox', { name: /^Nome/ }).fill('Luva de raspa')
    await page.getByRole('combobox', { name: /Unidade de estoque/ }).selectOption(UN.id)
    await page.getByRole('button', { name: 'Salvar item' }).click()
    await expect(page.getByText('Item salvo')).toBeVisible()
    await page.keyboard.press('Control+k')
    await page.getByPlaceholder('Buscar tela ou módulo…').fill('lu')
    await expect(page.getByRole('option', { name: /LUV-01 Luva de raspa/ })).toBeVisible()
  })
})
