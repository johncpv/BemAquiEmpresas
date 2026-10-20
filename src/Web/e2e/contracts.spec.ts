import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const quote = {
  id: 'q1', number: 'ORC-2026-0009', revision: 0, status: 'Sent', isCurrent: true, partyId: 'c1', partyName: 'Aurora', contactId: null, siteAddressId: null, deliveryDays: 90, notes: null,
  pricingMode: 'Markup', sentAt: '2026-10-20T10:00:00Z', createdAt: '2026-10-19T10:00:00Z', validUntil: '2026-11-04', lostReason: null, lostDetail: null, copiedFromNumber: null, parentQuoteId: null,
  items: [{ id: 'i1', sequence: 1, kind: 'Equipment', itemId: null, itemName: null, description: 'Elevador 6 paradas', quantity: 1, unitPrice: 100000, priceOverridden: true, total: 100000 }],
  totalPrice: 100000, version: 4,
}
const preview = {
  preconditions: [{ ok: true, text: 'Só a revisão mais recente pode ser aprovada.', fixTab: 'revisoes' }, { ok: true, text: 'Defina ao menos um marco de pagamento.', fixTab: 'condicoes' }],
  totalValue: 100000, items: 1, deliveryDays: 90,
  milestones: [{ description: 'Sinal', percent: 30, amount: 30000, when: 'na assinatura do contrato' }, { description: 'Entrega', percent: 70, amount: 70000, when: '10 dias após entrega' }],
  revenueAccounts: [{ id: 'a11', label: '1.1 Venda de equipamentos' }, { id: 'a19', label: '1.9 Receitas a classificar' }], suggestedRevenueAccountId: 'a11', projectName: 'Aurora — Elevador 6 paradas',
}
const project = {
  id: 'p1', number: 'PRJ-2026-0001', name: 'Aurora — Elevador 6 paradas', status: 'Planned', partyId: 'c1', partyName: 'Aurora', contractId: 'k1', contractNumber: 'CTR-2026-0001', siteAddressId: null,
  siteAddress: null, promisedDate: '2027-01-18', forecastDate: null, priority: null, managerUserId: 'u1', managerName: 'Ana Souza', createdAt: '2026-10-20T12:00:00Z',
  items: [{ id: 'pi1', sequence: 1, description: 'Elevador 6 paradas', quantity: 1, itemId: null, quoteItemId: 'i1' }], version: 1,
}
const contract = {
  id: 'k1', number: 'CTR-2026-0001', status: 'Active', partyId: 'c1', partyName: 'Aurora', partyDocument: null, totalValue: 100000, signedAt: '2026-10-20', revenueAccountId: 'a11',
  revenueAccount: '1.1 Venda de equipamentos', quoteId: 'q1', quoteNumber: 'ORC-2026-0009', quoteRevision: 0, projectId: 'p1', projectNumber: 'PRJ-2026-0001', projectName: project.name,
  createdAt: '2000-01-01T00:00:00Z', receivables: 0, version: 1,
  milestones: [{ id: 'm1', sequence: 1, description: 'Sinal', percent: 30, amount: 30000, trigger: 'Event', triggerEvent: 'Signature', dueDate: null, daysAfterEvent: 0 }],
  snapshot: { schema: 'v1', quoteNumber: 'ORC-2026-0009', quoteRevision: 0, party: { id: 'c1', name: 'Aurora' }, deliveryDays: 90, notes: null, pricingMode: 'Markup',
    items: [{ sequence: 1, kind: 'Equipment', itemId: null, description: 'Elevador 6 paradas', quantity: 1, unitPrice: 100000, total: 100000, unitCost: null, costs: null }],
    milestones: [], totalPrice: 100000, totalCost: null, marginPct: null },
}

const ALL = ['view', 'create', 'edit', 'approve', 'delete', 'view_cost']
const routes = (approved: { body?: unknown }) => ({
  'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '1', modules: allModules({ quotes: { actions: ALL } as never, projects: { actions: ALL } as never }) }),
  'GET /api/v1/quotes/q1': (r: Route) => json(r, quote),
  'GET /api/v1/quotes/q1/costs': (r: Route) => json(r, { quoteId: 'q1', pricingMode: 'Markup', defaultPct: 40, totalCost: 80000, totalPrice: 100000, marginPct: 20, items: [], staleLaborCosts: false }),
  'GET /api/v1/quotes/q1/conditions': (r: Route) => json(r, { milestones: [], percentSum: 100, warning: null, validUntil: '2026-11-04', defaultPaymentTermId: null, version: 4 }),
  'GET /api/v1/quotes/q1/approval': (r: Route) => json(r, preview),
  'GET /api/v1/quotes/q1/approval/costs': (r: Route) => json(r, { marginPct: 20, alerts: [{ sequence: 1, description: 'Elevador 6 paradas', belowCost: false, priceOverridden: true }] }),
  'POST /api/v1/quotes/q1/approve': (r: Route) => { approved.body = r.request().postDataJSON(); return json(r, { contractId: 'k1', contractNumber: 'CTR-2026-0001', projectId: 'p1', projectNumber: 'PRJ-2026-0001' }, 201) },
  'GET /api/v1/quotes/q1/contract': (r: Route) => json(r, { contractId: null }),
  'GET /api/v1/quotes/lookups/customers/c1/contacts': (r: Route) => json(r, []),
  'GET /api/v1/quotes/lookups/customers/c1/addresses': (r: Route) => json(r, []),
  'GET /api/v1/quotes/q1/revisions': (r: Route) => json(r, []),
  'GET /api/v1/projects/p1': (r: Route) => json(r, project),
  'GET /api/v1/projects/p1/budget': (r: Route) => json(r, [{ costType: 'Material', budgetedAmount: 50000 }, { costType: 'Labor', budgetedAmount: 30000 }]),
  'GET /api/v1/contracts/k1': (r: Route) => json(r, contract),
  'GET /api/v1/attachments/Contract/k1': (r: Route) => json(r, []),
})

test.describe('Aprovar e gerar contrato (COM-04a)', () => {
  test('aprova o enviado, abre o projeto pelo próximo passo e chega ao anexo do contrato', async ({ page }) => {
    const approved: { body?: unknown } = {}
    await mockApi(page, routes(approved))
    await page.goto('/app/orcamentos/q1')
    await page.getByRole('button', { name: 'Aprovar e gerar contrato' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Aurora — Elevador 6 paradas', { exact: false })).toBeVisible()
    await expect(dialog.getByText('Digitado à mão')).toBeVisible()
    await expect(dialog.getByRole('combobox')).toHaveValue('a11')
    await dialog.getByRole('button', { name: 'Aprovar e gerar contrato' }).click()
    await expect(page.getByText('Aprovado: contrato e projeto criados. Gerando as parcelas…')).toBeVisible()
    expect(approved.body).toMatchObject({ revenueAccountId: 'a11', version: 4 })
    await page.getByRole('button', { name: 'Ver projeto criado' }).click()
    await expect(page.getByRole('img', { name: 'Etapa Planejado, 1 de 6' })).toBeVisible()
    await page.getByRole('link', { name: 'Ver contrato CTR-2026-0001' }).click()
    await expect(page.getByText('As parcelas estão demorando.', { exact: false })).toBeVisible()
    await page.getByRole('tab', { name: 'Documentos' }).click()
    await expect(page.getByText('O contrato assinado ainda não foi anexado.')).toBeVisible()
  })

  test('diálogo em tela cheia a 400 px', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 800 })
    await mockApi(page, routes({}))
    await page.goto('/app/orcamentos/q1?aprovar=1')
    const dialog = page.getByRole('alertdialog')
    await expect(dialog).toBeVisible()
    const box = await dialog.boundingBox()
    expect(box?.width).toBeGreaterThanOrEqual(399)
  })
})

test.describe('Parcelas e marcos do contrato (COM-04b)', () => {
  test('mostra as parcelas, registra a fabricação e a parcela é liberada', async ({ page }) => {
    test.setTimeout(60_000)
    let fabricated = false
    const parcel = (open: boolean) => ({ id: 'r2', number: 'CR-2026-0002', status: open ? 'Open' : 'Planned', dueDate: open ? '2026-10-30' : null, amount: 40000, balance: 40000 })
    const contractNow = () => ({
      ...contract, receivables: 2,
      milestones: [
        { id: 'm1', sequence: 1, description: 'Sinal', percent: 30, amount: 30000, trigger: 'Event', triggerEvent: 'Signature', dueDate: null, daysAfterEvent: 0, hasReceivable: true, receivable: { id: 'r1', number: 'CR-2026-0001', status: 'Open', dueDate: '2026-10-20', amount: 30000, balance: 30000 } },
        { id: 'm2', sequence: 2, description: 'Fabricação concluída', percent: 40, amount: 40000, trigger: 'Event', triggerEvent: 'FabricationDone', dueDate: null, daysAfterEvent: 10, hasReceivable: true, receivable: parcel(fabricated) },
      ],
    })
    const projectNow = () => ({ ...project, status: fabricated ? 'Fabricated' : 'Planned', hasFabricationMilestone: true, contractStatus: 'Active', fabricatedOn: fabricated ? '2026-10-20' : null })
    await mockApi(page, {
      ...routes({}),
      'GET /api/v1/contracts/k1': (r: Route) => json(r, contractNow()),
      'GET /api/v1/projects/p1': (r: Route) => json(r, projectNow()),
      'POST /api/v1/projects/p1/fabrication': (r: Route) => { fabricated = true; return json(r, projectNow()) },
    })
    await page.goto('/app/contratos/k1?aba=itens')
    await expect(page.getByRole('link', { name: 'CR-2026-0002' })).toBeVisible()
    await expect(page.getByRole('list', { name: 'Marcos do contrato' }).getByText('aguarda o marco', { exact: false })).toBeVisible()
    await page.goto('/app/projetos/p1')
    await page.getByRole('button', { name: 'Registrar fabricação concluída' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Libera a parcela Fabricação concluída', { exact: false })).toBeVisible()
    await dialog.getByRole('button', { name: 'Registrar fabricação concluída' }).click()
    await expect(page.getByText('Fabricação registrada')).toBeVisible()
    await expect(page.getByRole('img', { name: 'Etapa Fabricado, 3 de 6' })).toBeVisible()
    await page.goto('/app/contratos/k1?aba=itens')
    await expect(page.getByRole('list', { name: 'Marcos do contrato' }).getByText('vence 30/10/2026', { exact: false })).toBeVisible()
  })
})

test.describe('Contrato de implantação (COM-04c)', () => {
  test('marca implantação, pré-marca o recebido antes do corte e envia no corpo', async ({ page }) => {
    const approved: { body?: unknown } = {}
    const onboardingPreview = {
      ...preview, onboarding: { cutoverDate: '2026-09-30', completedAt: null },
      milestones: [
        { id: 'qm1', description: 'Sinal', percent: 30, amount: 30000, when: 'na assinatura do contrato', trigger: 'Event', triggerEvent: 'Signature', dueDate: null, daysAfterEvent: 0 },
        { id: 'qm2', description: 'Entrega', percent: 70, amount: 70000, when: '10 dias após entrega', trigger: 'Event', triggerEvent: 'Delivered', dueDate: null, daysAfterEvent: 10 },
      ],
    }
    await mockApi(page, { ...routes(approved), 'GET /api/v1/quotes/q1/approval': (r: Route) => json(r, onboardingPreview) })
    await page.goto('/app/orcamentos/q1')
    await page.getByRole('button', { name: 'Aprovar e gerar contrato' }).click()
    const dialog = page.getByRole('alertdialog')
    const signed = dialog.getByRole('textbox').first()
    await signed.fill('')
    await signed.pressSequentially('01082026')
    await dialog.getByRole('checkbox', { name: 'Contrato de implantação (já estava em andamento)' }).check()
    const list = dialog.getByRole('list', { name: 'Marcos da implantação' })
    await expect(list.getByRole('checkbox').first()).toBeChecked()
    await expect(list.getByRole('checkbox').nth(1)).not.toBeChecked()
    await dialog.getByRole('button', { name: 'Aprovar e gerar contrato' }).click()
    await expect(page.getByText('Aprovado: contrato e projeto criados. Gerando as parcelas…')).toBeVisible()
    expect(approved.body).toMatchObject({ isOnboarding: true, signedAt: '2026-08-01', milestones: [{ milestoneId: 'qm1', receivedBefore: true }, { milestoneId: 'qm2', receivedBefore: false }] })
  })
})
