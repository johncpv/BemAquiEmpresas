import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const ALL = ['view', 'create', 'edit', 'approve', 'delete']
const page1 = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })
const level = { attempt: 1, level: 1, ruleId: 'r1', approverName: 'Proprietário', referenceAmount: 1200, splitWarning: null, status: 'Pending' }

function order(overrides: Record<string, unknown> = {}) {
  return {
    id: 'po1', number: 'PC-2026-0001', status: 'PendingApproval', partyId: 's1', partyName: 'Gama Aços', projectId: null, projectNumber: null, projectName: null, paymentTermId: 't3',
    paymentTermName: '30/60/90 dias', expectedDate: '2026-11-13', daysLate: null, subtotal: 1200, freight: 0, ipiPct: 0, ipiAmount: 0, total: 1200, notes: null, rfqId: null, quotesCount: null,
    singleQuoteWarning: null, requestedBy: 'u2', requestedByName: 'Bruno Compras', createdAt: '2026-10-23T12:00:00Z', submittedAt: '2026-10-23T13:00:00Z', approvedAt: null, attempt: 1,
    referenceAmount: 1200, splitWarning: null, selfApproved: false, changedAfterSent: false, rejectionComment: null, cancelReason: null, cancelledAt: null, cancelledByName: null,
    quantityReceived: 0,
    lines: [{ id: 'pl1', sequence: 1, itemId: 'i1', itemCode: 'MP-010', itemName: 'Chapa de aço 3 mm', unitCode: 'KG', quantity: 100, enteredQuantity: null, enteredUnitCode: null, unitPrice: 12,
      lineTotal: 1200, costType: 'Material', quantityReceived: 0, requestLineIds: [], requests: [] }],
    levels: [level], installments: [{ number: 1, estimatedDueDate: '2026-12-13', amount: 1200 }], isRequester: false, canEdit: true, canSubmit: false, canDecide: true, canCancel: true,
    decideBlocker: null, version: 8, canDispatch: false, canDownloadPdf: false, sentAt: null, ...overrides,
  }
}

const approved = () => order({ status: 'Approved', approvedAt: '2026-10-24T10:00:00Z', levels: [{ ...level, status: 'Approved' }], canDecide: false, canDispatch: true, canDownloadPdf: true, version: 9 })
const draft = { to: 'compras@gama.com.br', subject: 'Pedido de compra PC-2026-0001 — Konebras', supplierName: 'Gama Aços', supplierEmails: ['compras@gama.com.br'], isResend: false, companyComplete: true }
const dispatch = (overrides: Record<string, unknown> = {}) => ({
  id: 'd1', to: 'compras@gama.com.br', cc: 'dono@gmail.com', recipientConfirmed: true, isResend: false, sentByName: 'Ana Souza', createdAt: '2026-10-24T10:05:00Z', attachmentId: 'a1',
  attachmentName: 'PC-2026-0001.pdf', status: 'Queued', error: null, waitingScan: true, sentAt: null, ...overrides,
})

async function expectNoHorizontalScroll(page: Page) {
  const [scroll, width] = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(scroll).toBeLessThanOrEqual(width)
}

function api(state: { current: Record<string, unknown>; dispatches: unknown[] }, posted: Record<string, unknown>) {
  return {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ purchasing: { actions: ALL } as never, finance: { enabled: false } }) }),
    'GET /api/v1/purchasing/purchase-orders/po1': (r: Route) => json(r, state.current),
    'POST /api/v1/purchasing/purchase-orders/po1/approve': async (r: Route) => {
      state.current = approved()
      await json(r, state.current)
    },
    'GET /api/v1/purchasing/purchase-orders/po1/dispatch-draft': (r: Route) => json(r, draft),
    'POST /api/v1/purchasing/purchase-orders/po1/dispatches': async (r: Route) => {
      posted.body = r.request().postDataJSON()
      posted.key = r.request().headers()['idempotency-key']
      state.dispatches = [dispatch()]
      state.current = { ...approved(), status: 'Sent', sentAt: '2026-10-24T10:05:00Z', version: 10 }
      await json(r, state.current)
    },
    'GET /api/v1/purchasing/purchase-orders/po1/documents': (r: Route) => json(r, { generated: state.dispatches.length ? [{ id: 'a1', fileName: 'PC-2026-0001.pdf', contentType: 'application/pdf', size: 1000, scanStatus: 0, createdAt: '2026-10-24T10:05:00Z', uploadedByName: null, origin: 'System' }] : [], uploaded: [], dispatches: state.dispatches, companyComplete: true }),
    'GET /api/v1/purchasing/purchase-orders/po1/decisions': (r: Route) => json(r, { levels: [level], events: [] }),
    'GET /api/v1/purchasing/purchase-orders/po1/history': (r: Route) => json(r, page1([])),
    'GET /api/v1/purchasing/purchase-orders/po1/receipts': (r: Route) => json(r, []),
    'GET /api/v1/purchasing/purchase-orders/po1/payables': (r: Route) => json(r, []),
    'GET /api/v1/finance/cash-flow': (r: Route) => json(r, { today: '2026-10-24', from: '2026-10-24', to: '2026-12-31', view: 'daily', bankAccountId: null, currentBalance: 10_000, consolidated: true,
      periods: [], lowest: null, firstNegative: null, undatedReceivables: { count: 0, total: 0 } }),
  }
}

test.describe('Envio do pedido ao fornecedor (CMP-03b)', () => {
  test('aprovar, enviar ao fornecedor pelo próximo passo e ver o envio na aba Documentos', async ({ page }, info) => {
    const state = { current: order() as Record<string, unknown>, dispatches: [] as unknown[] }
    const posted: Record<string, unknown> = {}
    await mockApi(page, api(state, posted))
    await page.goto('/app/compras/pedidos/po1')
    await expect(page.getByRole('heading', { name: 'PC-2026-0001 — Gama Aços' })).toBeVisible({ timeout: 20_000 })
    await page.getByRole('button', { name: 'Aprovar pedido' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Aprovar pedido' }).click()
    await expect(page.getByText('Pedido aprovado. Previsão lançada no caixa.').first()).toBeVisible()

    // Propositivo: o primeiro próximo passo é "Enviar ao fornecedor", com o destinatário preenchido.
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Enviar ao fornecedor' }).click()
    const dialog = page.getByRole('alertdialog', { name: 'Enviar o pedido ao fornecedor' })
    await expect(dialog.getByRole('textbox', { name: 'Para', exact: true })).toHaveValue('compras@gama.com.br')
    await expect(dialog.getByText('Assunto: Pedido de compra PC-2026-0001 — Konebras.', { exact: false })).toBeVisible()
    await dialog.getByRole('textbox', { name: /^Cópia/ }).fill('dono@gmail.com')
    await expect(dialog.getByText('Este e-mail não está nos contatos de Gama Aços. Confirmo o destinatário.')).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Enviar ao fornecedor' })).toBeDisabled()
    await dialog.getByRole('checkbox', { name: /Confirmo o destinatário/ }).check()
    await dialog.getByRole('textbox', { name: /^Mensagem/ }).fill('Favor confirmar o prazo.')
    if (info.project.name === 'celular') {
      const box = await dialog.boundingBox()
      expect(box?.width ?? 0).toBeGreaterThanOrEqual((page.viewportSize()?.width ?? 0) - 1)
    }

    const axe = await new AxeBuilder({ page }).include('[role="alertdialog"]').analyze()
    expect(axe.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
    await dialog.getByRole('button', { name: 'Enviar ao fornecedor' }).click()
    await expect(page.getByText('Pedido enviado para a fila de e-mail').first()).toBeVisible()
    expect(posted.body).toEqual({ to: 'compras@gama.com.br', cc: ['dono@gmail.com'], message: 'Favor confirmar o prazo.', confirmRecipient: true, version: 9 })
    expect(posted.key).toBeTruthy()
    await expect(page.getByText('Enviado', { exact: true }).first()).toBeVisible()

    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Ver o envio em Documentos' }).click()
    const sends = page.getByRole('list', { name: 'Envios ao fornecedor' })
    await expect(sends.getByText('compras@gama.com.br')).toBeVisible()
    await expect(sends.getByText('Aguardando verificação do anexo')).toBeVisible()
    await expect(sends.getByText('Destinatário confirmado')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Reenviar' }).first()).toBeVisible()
    if (info.project.name === 'celular') await expectNoHorizontalScroll(page)
  })

  test('faixa "Alterado depois do envio" com Reenviar, envio que falhou e o limite diário com Baixar PDF', async ({ page }) => {
    const state = {
      current: { ...approved(), changedAfterSent: true, sentAt: '2026-10-23T10:00:00Z' } as Record<string, unknown>,
      dispatches: [dispatch({ status: 'Failed', error: 'O antivírus recusou ou não conseguiu verificar o anexo.', waitingScan: false })] as unknown[],
    }
    await mockApi(page, {
      ...api(state, {}),
      'GET /api/v1/purchasing/purchase-orders/po1/dispatch-draft': (r: Route) => json(r, { ...draft, isResend: true }),
      'POST /api/v1/purchasing/purchase-orders/po1/dispatches': (r: Route) => json(r, { title: 'Limite', status: 429, code: 'email_dispatch_limit', detail: 'Limite diário de envios atingido. Tente amanhã ou baixe o PDF.' }, 429),
    })
    await page.goto('/app/compras/pedidos/po1?aba=documentos')
    const band = page.getByRole('note').filter({ hasText: 'Alterado depois do envio: reenvie ao fornecedor.' })
    await expect(band).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('Falhou: O antivírus recusou ou não conseguiu verificar o anexo.')).toBeVisible()
    await band.getByRole('button', { name: 'Reenviar' }).click()
    const dialog = page.getByRole('alertdialog', { name: 'Reenviar o pedido ao fornecedor' })
    await dialog.getByRole('button', { name: 'Reenviar' }).click()
    await expect(dialog.getByRole('alert')).toContainText('Limite diário de envios atingido. Tente amanhã ou baixe o PDF.')
    await expect(dialog.getByRole('button', { name: 'Baixar PDF' })).toBeVisible()
  })

  test('Documentos sem envio: estado vazio com a ação', async ({ page }) => {
    const state = { current: approved() as Record<string, unknown>, dispatches: [] as unknown[] }
    await mockApi(page, api(state, {}))
    await page.goto('/app/compras/pedidos/po1?aba=documentos')
    await expect(page.getByText('Nenhum envio.')).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('Envie o pedido ao fornecedor depois de aprovado.')).toBeVisible()
    await page.getByRole('status').filter({ hasText: 'Nenhum envio.' }).getByRole('button', { name: 'Enviar ao fornecedor' }).click()
    await expect(page.getByRole('alertdialog', { name: 'Enviar o pedido ao fornecedor' })).toBeVisible()
  })
})
