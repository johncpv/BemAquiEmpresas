import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const step = (i: number, code: string, name: string, status: string) => ({
  id: `s${i}`, sequence: i, code, name, workCenterId: 'c1', workCenterName: 'Montagem', line: 'Linha 1', standardHours: 2, status, isQualityStep: false, requiresApproval: false,
  checklist: [], appliedHours: 0, awaitingApproval: false, selfApproved: false,
})
const order = {
  id: 'of1', number: 'OS-2026-00041', status: 'InProgress', projectId: 'p1', projectNumber: 'PRJ-2026-0003', projectName: 'Monte Castelo', partyId: 'cli1', partyName: 'Condomínio',
  promisedDate: '2026-12-20', projectItemId: 'pi1', itemDescription: 'Cabina', productId: 'i1', productCode: 'CAB', productName: 'Cabina e portas', quantity: 2, priority: 0, routingTemplateId: 'rt1',
  routingTemplateName: 'Elevador', steps: [step(1, 'P', 'Projeto', 'Done'), step(2, 'C', 'Corte', 'Ready'), step(3, 'M', 'Montagem', 'Waiting')],
  totalStandardHours: 6, currentStepIndex: 1, started: true, delayDays: null, cancelReason: null, cancelledAt: null, createdAt: '2026-10-21T12:00:00Z', version: 4,
}
const header = {
  id: 'of1', number: 'OS-2026-00041', status: 'InProgress', projectId: 'p1', productId: 'i1', productCode: 'CAB', productName: 'Cabina e portas', quantity: 2, closed: false,
  currentStepCode: 'C', currentStepName: 'Corte', currentStepSequence: 2,
}
const units = [{ unitId: 'kg', code: 'KG', isStockUnit: true, toStock: 1 }]
const line = (id: string, itemCode: string, itemName: string, quantity: number, required: number) => ({
  id, sequence: 1, itemId: 'it-' + itemCode, itemCode, itemName, itemType: 'RawMaterial', itemActive: true, quantity, unitId: 'kg', unitCode: 'KG', scrapPct: 10, notes: null,
  requiredQuantity: required, stockUnitId: 'kg', stockUnitCode: 'KG', onHand: null, reserved: null, available: null, shortage: null, estimatedCost: null, units,
})
const bom = (status: string, lines: ReturnType<typeof line>[], version = 1) => ({
  id: 'b1', productionOrderId: 'of1', revision: 0, status, releasedAt: status === 'Released' ? '2026-10-23T12:00:00Z' : null, releasedBy: null, releasedByName: status === 'Released' ? 'Ana Souza' : null,
  copiedFromBomId: 'b9', copiedFromOrderNumber: 'OS-2026-00012', copiedFromRevision: 1, lines, shortageCount: 0, stockConnected: false, estimatedCost: null, version, createdAt: '2026-10-23T11:00:00Z',
})
const revisions = (status: string) => [{ id: 'b1', revision: 0, status, lineCount: 1, createdAt: '2026-10-23T11:00:00Z', releasedAt: status === 'Released' ? '2026-10-23T12:00:00Z' : null, releasedByName: null }]

async function bomApi(page: Page, overrides: Record<string, (r: Route) => unknown> = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ production: { actions: ALL } as never, engineering: { actions: ALL } as never }) }),
    'GET /api/v1/production/production-orders/of1': (r: Route) => json(r, order),
    'GET /api/v1/attachments/ProductionOrder/of1': (r: Route) => json(r, []),
    'GET /api/v1/production/production-orders/of1/labor-entries': (r: Route) => json(r, { orderId: 'of1', orderNumber: 'OS-2026-00041', entries: [], totalHours: 0, awaitingApprovalStepId: null, awaitingApprovalStepName: null, version: 4 }),
    ...overrides,
  })
}

test.describe('BOM da OS (ENG-02)', () => {
  test('copia a BOM de outra OS com a origem sugerida, libera e vê o próximo passo', async ({ page }) => {
    let view: Record<string, unknown> = { order: header, released: null, draft: null, revisions: [] }
    const sent: Record<string, unknown> = {}
    await bomApi(page, {
      'GET /api/v1/engineering/boms/orders/of1': (r: Route) => json(r, view),
      'GET /api/v1/engineering/boms/orders/of1/copy-sources': (r: Route) => json(r, [
        { bomId: 'b9', productionOrderId: 'of9', orderNumber: 'OS-2026-00012', revision: 1, productCode: 'CAB', productName: 'Cabina e portas', lineCount: 1, releasedAt: '2026-10-01T12:00:00Z', suggested: true },
        { bomId: 'b8', productionOrderId: 'of8', orderNumber: 'OS-2026-00008', revision: 0, productCode: 'POR', productName: 'Porta', lineCount: 3, releasedAt: '2026-09-01T12:00:00Z', suggested: false },
      ]),
      'POST /api/v1/engineering/boms': (r: Route) => {
        sent.create = r.request().postDataJSON()
        sent.key = r.request().headers()['idempotency-key']
        const draft = bom('Draft', [line('l1', 'CHAPA', 'Chapa de aço 2 mm', 1.5, 3.3)])
        view = { order: header, released: null, draft, revisions: revisions('Draft') }
        return json(r, draft, 201)
      },
      'GET /api/v1/engineering/boms/b1/release-preview': (r: Route) => json(r, {
        bomId: 'b1', revision: 0, isRevision: false, previousRevision: null, empty: false, stockConnected: true, reservationsConnected: false, shortageCount: 1, inactiveCount: 0,
        lines: [{ itemId: 'it-CHAPA', itemCode: 'CHAPA', itemName: 'Chapa de aço 2 mm', stockUnitCode: 'KG', itemActive: true, change: 'Added', previousRequired: 0, required: 3.3,
          moreToReserve: 3.3, toRelease: 0, available: 1, toReserve: 1, shortage: 2.3, issued: null, issuedWarning: null }],
      }),
      'POST /api/v1/engineering/boms/b1/release': (r: Route) => {
        sent.release = r.request().postDataJSON()
        const released = bom('Released', [line('l1', 'CHAPA', 'Chapa de aço 2 mm', 1.5, 3.3)], 2)
        view = { order: header, released, draft: null, revisions: revisions('Released') }
        return json(r, released)
      },
    })

    await page.goto('/app/producao/os/of1?aba=bom')
    await expect(page.getByText('Esta OS ainda não tem BOM.')).toBeVisible()
    await expect(page.getByText('A etapa atual (C · Corte) só começa com a BOM liberada.', { exact: false })).toBeVisible()

    // CA 5 e padrão 6: a última OS do mesmo produto vem marcada "Sugerido" e já escolhida.
    await page.getByRole('button', { name: 'Copiar de outra OS' }).click()
    const copy = page.getByRole('alertdialog')
    await expect(copy.getByText('Sugerido')).toBeVisible()
    await expect(copy.getByRole('radio').first()).toBeChecked()
    await copy.getByRole('button', { name: 'Copiar BOM' }).click()
    await expect(page.getByText('BOM copiada')).toBeVisible()
    expect(sent.create).toEqual({ productionOrderId: 'of1', copyFromBomId: 'b9' })
    expect(sent.key).toBeTruthy()

    // CA 3: a necessidade da OS ao vivo (1,5 × 2 unidades × 1,10 = 3,3 KG).
    await expect(page.getByText('Copiada da OS-2026-00012, revisão 1.')).toBeVisible()
    await expect(page.getByLabel('Necessidade da OS na linha 1')).toContainText('3,3 KG')

    // CA 8 e 10: diálogo de impacto com a falta e o próximo passo depois de liberar.
    await page.getByRole('button', { name: 'Liberar BOM' }).click()
    const release = page.getByRole('alertdialog')
    await expect(release.getByText('Falta 2,3')).toBeVisible()
    await expect(release.getByText('1 item em falta pelo saldo disponível.')).toBeVisible()
    // EST-01a ligada: o saldo aparece; "vai reservar" e o baixado esperam a EST-02.
    await expect(release.getByText('Quanto vai ser reservado e o que já foi baixado ainda não aparecem nesta versão.')).toBeVisible()
    await expect(release.getByRole('columnheader', { name: 'Vai reservar' })).toHaveCount(0)
    await release.getByRole('button', { name: 'Liberar BOM' }).click()
    await expect(page.getByText('BOM liberada. Reservando material…')).toBeVisible()
    expect(sent.release).toEqual({ version: 1 })
    await expect(page.getByRole('button', { name: 'Ver solicitações de compra geradas' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Ver faltas da OS' })).toBeVisible()
    await expect(page.getByText('BOM liberada em', { exact: false })).toContainText('por Ana Souza. Para mudar, revise a BOM.')
    await expect(page.getByRole('button', { name: 'Revisar BOM' })).toBeVisible()
  })

  test('concluir a segunda etapa sem BOM liberada é recusado e leva à BOM da OS (CA 16)', async ({ page }) => {
    await bomApi(page, {
      'POST /api/v1/production/production-orders/of1/steps/s2/complete': (r: Route) => json(r, { status: 409, code: 'bom_not_released', detail: 'A BOM desta OS ainda não foi liberada. Fale com a Engenharia.' }, 409),
      'GET /api/v1/engineering/boms/orders/of1': (r: Route) => json(r, { order: header, released: null, draft: null, revisions: [] }),
    })

    await page.goto('/app/producao/os/of1?aba=apontamentos&concluir=s2')
    const dialog = page.getByRole('alertdialog')
    await dialog.getByRole('textbox', { name: 'Motivo' }).fill('Corte feito fora do sistema.')
    await dialog.getByRole('button', { name: 'Concluir etapa', exact: true }).click()
    await expect(dialog.getByText('A BOM desta OS ainda não foi liberada. Fale com a Engenharia.')).toBeVisible()
    await dialog.getByRole('button', { name: 'Abrir BOM da OS' }).click()
    await expect(page).toHaveURL(/\/app\/producao\/os\/of1\?aba=bom/)
    await expect(page.getByText('Esta OS ainda não tem BOM.')).toBeVisible()
  })

  test('lista de BOMs abre nas OS que vão parar e cabe a 400 px; a aba BOM também', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 820 })
    let situation: string | null = null
    await bomApi(page, {
      'GET /api/v1/engineering/boms': (r: Route) => {
        situation = new URL(r.request().url()).searchParams.get('situation')
        return json(r, { items: [{
          productionOrderId: 'of1', number: 'OS-2026-00041', orderStatus: 'InProgress', productCode: 'CAB', productName: 'Cabina e portas', quantity: 2, currentStepCode: 'C',
          currentStepName: 'Corte', currentStepSequence: 2, stepCount: 3, situation: 'None', draftBomId: null, draftLineCount: 0, draftVersion: null, releasedBomId: null,
          releasedRevision: null, releasedAt: null, willStop: true,
        }], page: 1, pageSize: 20, total: 1 })
      },
      'GET /api/v1/engineering/boms/orders/of1': (r: Route) => json(r, { order: header, released: null, draft: bom('Draft', [line('l1', 'CHAPA', 'Chapa de aço 2 mm', 1.5, 3.3)]), revisions: revisions('Draft') }),
    })

    await page.goto('/app/engenharia/boms')
    await expect(page).toHaveURL(/situacao=vao-parar/)
    await expect(page.getByText('Vai parar sem BOM')).toBeVisible()
    expect(situation).toBe('will_stop')
    await expect(page.getByRole('button', { name: 'Criar BOM' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Liberar BOM sem material' })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)

    await page.goto('/app/producao/os/of1?aba=bom')
    await expect(page.getByLabel('Item da linha 1')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Liberar BOM' })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
  })
})
