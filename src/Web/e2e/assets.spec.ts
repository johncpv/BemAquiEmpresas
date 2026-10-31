import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ASSET = {
  id: 'a1', tag: 'PAT-00001', name: 'Torno CNC', type: 'Machine', status: 'Active', brand: null, model: null, serialNumber: null, acquisitionDate: null, incomingInvoiceId: null,
  incomingInvoice: null, invoiceRef: null, supplierPartyId: null, supplierName: null, siteId: 's1', siteName: 'Fábrica', responsibleEmployeeId: 'e1', responsibleName: 'Ana Prado',
  responsibleTerminated: false, workCenterId: 'c1', workCenterName: 'Usinagem', notes: null, photoId: null, createdAt: '2026-10-31T12:00:00Z', version: 1,
}
const PHOTO = { id: 'f1', fileName: 'camera.jpg', contentType: 'image/jpeg', size: 120, scanStatus: 1, createdAt: '2026-10-31T12:05:00Z', uploadedByName: 'Ana Souza', origin: 'User' }
const PIXEL = Buffer.from('/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=', 'base64')

/** Backend em memória da PAT-01a (as regras reais estão nos testes de integração .NET). */
function assetsApi() {
  const assets: Record<string, unknown>[] = []
  const posted: unknown[] = []
  const photos: Record<string, unknown>[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/assets/assets': (r) => json(r, { items: assets, page: 1, pageSize: 20, total: assets.length }),
    'POST /api/v1/assets/assets': (r) => { posted.push(r.request().postDataJSON()); assets.push(ASSET); return json(r, ASSET, 201) },
    'GET /api/v1/assets/assets/suggestions': (r) => json(r, { siteId: 's1', siteName: 'Fábrica', responsibleEmployeeId: 'e1', responsibleName: 'Ana Prado' }),
    'GET /api/v1/assets/assets/*': (r) => json(r, { ...ASSET, photoId: photos.length ? 'f1' : null }),
    'GET /api/v1/assets/assets/*/attachments': (r) => json(r, { photoId: photos.length ? 'f1' : null, photos, documents: [] }),
    'POST /api/v1/assets/assets/*/photo': (r) => { photos.push(PHOTO); return json(r, PHOTO, 201) },
    'GET /api/v1/assets/assets/*/attachments/*/content': (r) => r.fulfill({ status: 200, contentType: 'image/jpeg', body: PIXEL }),
    'GET /api/v1/assets/choices/responsibles': (r) => json(r, [{ id: 'e1', name: 'Ana Prado', registration: '0042', positionName: 'Mecânica' }]),
    'GET /api/v1/assets/choices/work-centers': (r) => json(r, [{ id: 'c1', name: 'Usinagem', line: null }]),
    'GET /api/v1/assets/sites': (r) => json(r, [{ id: 's1', name: 'Fábrica', isActive: true, isSeed: true, assets: 0 }]),
  }
  return { api, posted }
}

test.describe('Patrimônio (PAT-01a)', () => {
  test('supervisor cadastra uma máquina a 400 px com local e responsável sugeridos pelo centro e envia a foto (CA 2, 3 e 8)', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 860 })
    const { api, posted } = assetsApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '3', modules: allModules().map((m) => (m.code === 'assets' ? { ...m, actions: ['view', 'create', 'edit'] } : m)) }) })
    await page.goto('/app/patrimonio')
    await expect(page).toHaveURL(/\/app\/patrimonio\/bens$/)
    await expect(page.getByText('Cadastre as máquinas e ferramentas para acompanhar manutenção e custo.')).toBeVisible()
    await page.getByRole('link', { name: 'Novo bem' }).first().focus()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/app\/patrimonio\/bens\/novo$/)

    const main = page.getByRole('main')
    await main.getByRole('textbox', { name: /^Nome/ }).focus()
    await page.keyboard.type('Torno CNC')
    await main.getByRole('combobox', { name: /^Tipo/ }).selectOption('Machine')
    await main.getByRole('combobox', { name: 'Centro de trabalho' }).selectOption('c1')
    await expect(main.getByRole('combobox', { name: /^Responsável/ })).toHaveValue('e1')
    await expect(main.getByRole('combobox', { name: /^Local/ })).toHaveText(/Fábrica/)
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await main.getByRole('button', { name: 'Salvar bem' }).focus()
    await page.keyboard.press('Enter')
    await expect(page.getByText('Bem PAT-00001 salvo')).toBeVisible()
    expect(posted[0]).toMatchObject({ name: 'Torno CNC', type: 'Machine', workCenterId: 'c1', siteId: 's1', responsibleEmployeeId: 'e1', acquisitionValue: null })
    await expect(page).toHaveURL(/\/app\/patrimonio\/bens\/a1/)

    const steps = page.getByRole('region', { name: 'Próximos passos' })
    await expect(steps.getByRole('button', { name: 'Criar plano de manutenção' })).toBeVisible()
    await steps.getByRole('button', { name: 'Anexar foto' }).click()
    await expect(page.getByRole('tab', { name: 'Documentos' })).toHaveAttribute('aria-selected', 'true')
    await page.locator('#foto-bem').setInputFiles({ name: 'camera.jpg', mimeType: 'image/jpeg', buffer: PIXEL })
    await expect(page.getByText('Foto enviada')).toBeVisible()
    await expect(page.getByRole('list', { name: 'Foto' }).getByText('camera.jpg')).toBeVisible()
    await page.getByRole('tab', { name: 'Dados' }).click()
    await expect(page.getByRole('img', { name: 'Foto de Torno CNC' })).toBeVisible()
  })
})
