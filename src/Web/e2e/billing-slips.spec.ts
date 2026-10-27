import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })
const LINE = '07790001161234567890123456789012345678901234567'

const receivable = (id: string, over: Record<string, unknown> = {}) => ({
  id, number: `CR-2026-000${id.slice(1)}`, partyId: 'c1', partyName: 'Kappa Ltda', description: 'Venda de elevador', accountId: 'a1', accountName: '1.1 Venda de equipamentos', costCenterId: null, projectId: null,
  amount: 1500, paidAmount: 0, balance: 1500, interest: 0, fine: 0, discount: 0, issueDate: '2026-10-01', dueDate: '2026-12-10', status: 'Open', isOverdue: false, isDueToday: false,
  origin: 'Manual', originId: null, installmentGroupId: null, cancelReason: null, cancelledAt: null, version: 1, billingSlipId: null, billingSlipStatus: null, ...over,
})

const settings = (over: Record<string, unknown> = {}) => ({
  finePct: 2, interestMonthPct: 1, defaultDiscountPct: 0, autoEmail: true, dailyEmailLimit: 200, overdueFineFrom: 'OriginalDueDate',
  nonBusinessDayRule: 'Vencimento em dia não útil segue a regra do banco (o pagamento vale no dia útil seguinte). O título fica sem ajuste.',
  reviews: [
    { key: 'finance.late_fine_pct', label: 'Multa', reviewState: 'ToConfirm', reviewedByName: null, reviewedAt: null },
    { key: 'finance.late_interest_month_pct', label: 'Juros de mora', reviewState: 'ToConfirm', reviewedByName: null, reviewedAt: null },
    { key: 'finance.overdue_fine_from', label: 'Multa e juros do título vencido', reviewState: 'ToConfirm', reviewedByName: null, reviewedAt: null },
  ],
  pendingCount: 3, canEdit: true, ...over,
})

const draft = {
  receivableId: 'r1', receivableNumber: 'CR-2026-0001', partyId: 'c1', partyName: 'Kappa Ltda', amount: 1500, receivableDueDate: '2026-12-10', isOverdue: false, suggestedDueDate: '2026-12-10',
  finePct: 2, interestMonthPct: 1, discountAmount: 0, discountUntil: null, autoEmail: true, email: 'cobranca@kappa.com.br', overdueFineFrom: 'OriginalDueDate', nonBusinessDayRule: 'Regra do banco.',
  discountLimitPct: 5, discountLimitAmount: 500, canApprove: false, accounts: [{ id: 'b1', name: 'Inter PJ', sandbox: true, isDefault: true }],
  preconditions: [], currentSlipId: null, version: 1,
}

const slip = (over: Record<string, unknown> = {}) => ({
  id: 's1', receivableId: 'r1', bankAccountId: 'b1', bankAccountName: 'Inter PJ', sandbox: true, status: 'Issued', ourReference: '00000A', ourNumber: '0001234567', amount: 1500, dueDate: '2026-12-10',
  receivableDueDate: '2026-12-10', fineFrom: '2026-12-10', finePct: 2, interestMonthPct: 1, discountAmount: 0, discountUntil: null, message: null, barcode: '0779', digitableLine: LINE,
  pixCopyPaste: '00020101021226930014br.gov.bcb.pix', qrCode: '00020101021226930014br.gov.bcb.pix', pdfAttachmentId: 'f1', failureReason: null, failureAction: null, cancelReason: null, cancelReasonText: null,
  cancelPending: false, cancelFailureReason: null, cancelledAt: null, expiredAt: null, paidAt: null, requestedAt: new Date().toISOString(), requestedByName: 'Ana Souza', issuedAt: new Date().toISOString(),
  approvedByName: null, sendOnIssue: true, scheduledEmailAt: null, lastEmailAt: null, emails: [], version: 2, ...over,
})

/** Backend em memória da BNK-01b (as regras reais estão nos testes de integração .NET com o fake do Inter). */
function billingApi() {
  const posted: { method: string; url: string; body: unknown; headers: Record<string, string> }[] = []
  let current = settings()
  let r1 = receivable('r1')
  let slips: ReturnType<typeof slip>[] = []
  const record = (r: Route) => posted.push({ method: r.request().method(), url: new URL(r.request().url()).pathname, body: r.request().postDataJSON(), headers: r.request().headers() })
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/settings/billing': (r) => json(r, current),
    'PUT /api/v1/settings/billing': (r) => {
      record(r)
      const b = r.request().postDataJSON() as { finePct: number }
      if (b.finePct > 2) return json(r, { title: 'Dados inválidos', detail: 'A multa não pode passar de 2,00%.', code: 'validation', errors: { finePct: ['A multa não pode passar de 2,00%.'] } }, 422)
      current = settings({ ...b, reviews: current.reviews, pendingCount: current.pendingCount })
      return json(r, current)
    },
    'POST /api/v1/settings/billing/confirm': (r) => {
      record(r)
      const { key } = r.request().postDataJSON() as { key: string }
      const reviews = current.reviews.map((x) => (x.key === key ? { ...x, reviewState: 'Confirmed', reviewedByName: 'Ana Souza', reviewedAt: '2026-10-27T10:00:00Z' } : x))
      current = settings({ reviews, pendingCount: reviews.filter((x) => x.reviewState !== 'Confirmed').length })
      return json(r, current)
    },
    'GET /api/v1/finance/bank-accounts': (r) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
    'GET /api/v1/finance/chart-of-accounts': (r) => json(r, [{ id: 'a1', code: '1.1', name: 'Venda de equipamentos', type: 'Revenue', dreGroup: 'GrossRevenue', parentId: null, acceptsEntries: true, isActive: true, seedKey: '1.1' }]),
    'GET /api/v1/finance/cost-centers': (r) => json(r, []),
    'GET /api/v1/finance/customers': (r) => json(r, []),
    'GET /api/v1/finance/parameters': (r) => json(r, { discountApprovalPct: 5, discountApprovalAmount: 500, cancelApprovalAmount: 1000, backdateDays: 7 }),
    'GET /api/v1/finance/receivables': (r) => {
      const items = [r1, receivable('r2', { partyName: 'Lambda S.A.' })]
      return json(r, { page: { items, page: 1, pageSize: 20, total: items.length }, totals: { open: 3000, overdue: 0 } })
    },
    'POST /api/v1/finance/receivables/billing-slips/batch-preview': (r) => {
      record(r)
      return json(r, {
        eligible: [{ receivableId: 'r1', number: 'CR-2026-0001', partyName: 'Kappa Ltda', amount: 1500, dueDate: '2026-12-10', email: 'cobranca@kappa.com.br' }],
        refused: [{ receivableId: 'r2', number: 'CR-2026-0002', partyName: 'Lambda S.A.', reason: 'O cliente está sem endereço de cobrança completo (CEP, rua, número, bairro, cidade e UF).' }],
        total: 1500, bankAccountId: 'b1', sandbox: true,
      })
    },
    'POST /api/v1/finance/receivables/billing-slips/batch': (r) => {
      record(r)
      return json(r, { requested: 1, slipIds: ['s9'], refused: [], message: '1 boleto solicitado' })
    },
    'GET /api/v1/finance/receivables/*': (r) => json(r, r1),
    'GET /api/v1/finance/receivables/*/settlements': (r) => json(r, []),
    'GET /api/v1/finance/receivables/*/billing-slips': (r) => json(r, slips),
    'GET /api/v1/finance/receivables/*/billing-slips/draft': (r) => json(r, draft),
    'POST /api/v1/finance/receivables/*/billing-slips': (r) => {
      record(r)
      const requested = slip({ status: 'Requested', digitableLine: null, pixCopyPaste: null, qrCode: null, pdfAttachmentId: null, ourNumber: null })
      slips = [slip()]
      r1 = receivable('r1', { billingSlipId: 's1', billingSlipStatus: 'Issued', version: 2 })
      return json(r, { slip: requested, message: 'Boleto solicitado ao Inter. Assim que o banco confirmar, ele aparece aqui.' }, 201)
    },
    'POST /api/v1/finance/receivables/*/billing-slips/*/send': (r) => {
      record(r)
      slips = [slip({ emails: [{ id: 'e1', to: 'c***@kappa.com.br', status: 0, createdAt: new Date().toISOString(), sentAt: null, error: null }] })]
      return json(r, { slip: slips[0], message: 'Boleto enviado' })
    },
    'POST /api/v1/finance/receivables/*/billing-slips/*/cancel': (r) => {
      record(r)
      slips = [slip({ status: 'Cancelled', cancelReason: 'CustomerRequest', cancelReasonText: 'Pedido do cliente' })]
      r1 = receivable('r1', { version: 3 })
      return json(r, { slip: slips[0], message: 'Cancelamento enviado ao Inter.' })
    },
  }
  return { api, posted }
}

async function noOverflow(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
  expect(overflow).toBeLessThanOrEqual(0)
}

test.describe('Boleto com Pix (BNK-01b)', () => {
  test('Configurações → Cobrança: valores do seed, conferir a multa e validação', async ({ page }) => {
    const { api, posted } = billingApi()
    await mockApi(page, api)
    await page.goto('/app/configuracoes/cobranca')
    await expect(page.getByRole('heading', { level: 1, name: 'Cobrança' })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Configurações', exact: true }).getByRole('link', { name: 'Cobrança' })).toHaveAttribute('aria-current', 'page')
    await expect(page.getByText('3 valores a confirmar')).toBeVisible()
    await expect(page.getByText('A confirmar com a contadora')).toHaveCount(3)
    await page.getByRole('button', { name: 'Marcar como conferido: Multa', exact: true }).click()
    await expect(page.getByText('2 valores a confirmar')).toBeVisible()
    await expect(page.getByText(/Conferido por Ana Souza/)).toBeVisible()
    expect(posted.find((p) => p.url === '/api/v1/settings/billing/confirm')?.body).toEqual({ key: 'finance.late_fine_pct' })

    await page.getByRole('textbox', { name: /^Multa/ }).fill('2,50')
    await page.getByRole('button', { name: 'Salvar cobrança' }).click()
    await expect(page.getByText('A multa não pode passar de 2,00%.')).toBeVisible()
    await page.getByRole('textbox', { name: /^Multa/ }).fill('1,50')
    await page.getByRole('button', { name: 'Salvar cobrança' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Cobrança salva' })).toBeVisible()
    await noOverflow(page)
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
  })

  test('emitir o boleto pelo teclado, copiar a linha digitável, enviar e cancelar', async ({ page, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    const { api, posted } = billingApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/receber/r1')
    await expect(page.getByRole('heading', { level: 1, name: /CR-2026-0001/ })).toBeVisible()
    // Só pelo teclado: foco no botão, Enter abre; Tab até o verbo e Enter confirma.
    await page.getByRole('button', { name: 'Emitir boleto' }).first().focus()
    await page.keyboard.press('Enter')
    const dialog = page.getByRole('alertdialog', { name: 'Emitir boleto do título CR-2026-0001' })
    await expect(dialog).toBeVisible()
    await expect(dialog.getByRole('status').filter({ hasText: 'Ambiente de teste do banco' })).toBeVisible()
    await expect(dialog.getByRole('group', { name: 'Impacto' })).toContainText('cobranca@kappa.com.br')
    await expect(dialog.getByRole('group', { name: 'Impacto' })).toContainText('R$ 1.500,00')
    await noOverflow(page)
    const confirm = dialog.getByRole('button', { name: 'Emitir boleto' })
    await confirm.focus()
    await page.keyboard.press('Enter')
    await expect(page.getByRole('status').filter({ hasText: 'Boleto solicitado ao Inter.' })).toBeVisible()
    const issue = posted.find((p) => p.method === 'POST' && p.url === '/api/v1/finance/receivables/r1/billing-slips')!
    expect(issue.headers['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(issue.body).toMatchObject({ bankAccountId: 'b1', dueDate: '2026-12-10', finePct: 2, interestMonthPct: 1, discountAmount: 0, sendEmail: true, version: 1 })

    await expect(page).toHaveURL(/aba=boleto/)
    const current = page.getByRole('region', { name: 'Boleto atual' })
    await expect(current.getByText('Boleto emitido')).toBeVisible()
    await expect(current.getByTestId('Linha digitável')).toHaveText(LINE)
    await expect(current.getByRole('img', { name: 'QR Code do Pix do boleto' })).toBeVisible()
    await current.getByRole('button', { name: 'Copiar linha digitável' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Linha digitável copiado' })).toBeVisible()
    await noOverflow(page)

    await current.getByRole('button', { name: 'Enviar ao cliente' }).click()
    await page.getByRole('alertdialog', { name: 'Enviar o boleto ao cliente' }).getByRole('button', { name: 'Enviar ao cliente' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Boleto enviado' })).toBeVisible()
    await expect(current.getByText(/c\*\*\*@kappa\.com\.br/)).toBeVisible()

    await current.getByRole('button', { name: 'Cancelar boleto' }).click()
    const cancel = page.getByRole('alertdialog', { name: 'Cancelar o boleto no Inter?' })
    await cancel.getByLabel(/Motivo/).selectOption('CustomerRequest')
    await cancel.getByRole('button', { name: 'Cancelar boleto' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Boleto cancelado' })).toBeVisible()
    expect(posted.find((p) => p.url.endsWith('/cancel'))?.body).toEqual({ reason: 'CustomerRequest' })
    await expect(page.getByText('Este título ainda não tem boleto.')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Boletos anteriores' })).toContainText('Pedido do cliente')
  })

  test('lote com impacto em lista: elegíveis, recusados com o motivo e o total', async ({ page }) => {
    const { api, posted } = billingApi()
    await mockApi(page, api)
    await page.goto('/app/financeiro/receber')
    await page.getByRole('checkbox', { name: 'Selecionar título CR-2026-0001' }).check()
    await page.getByRole('checkbox', { name: 'Selecionar título CR-2026-0002' }).check()
    await page.getByRole('button', { name: 'Emitir boletos' }).click()
    const dialog = page.getByRole('alertdialog', { name: 'Emitir boletos' })
    await expect(dialog.getByRole('region', { name: 'Vão receber boleto' })).toContainText('CR-2026-0001 · Kappa Ltda')
    await expect(dialog.getByRole('region', { name: 'Vão receber boleto' })).toContainText('Total: R$ 1.500,00')
    await expect(dialog.getByRole('region', { name: 'Recusados' })).toContainText('sem endereço de cobrança')
    await noOverflow(page)
    await dialog.getByRole('button', { name: 'Emitir 1 boleto' }).click()
    await expect(page.getByRole('status').filter({ hasText: '1 boleto solicitado' })).toBeVisible()
    const batch = posted.find((p) => p.url.endsWith('/batch'))!.body as { items: { receivableId: string; clientRequestId: string }[] }
    expect(batch.items).toHaveLength(1)
    expect(batch.items[0]).toMatchObject({ receivableId: 'r1' })
    expect(batch.items[0].clientRequestId).toMatch(/^[0-9a-f-]{36}$/)
    expect(today()).toMatch(/\d{4}-\d{2}-\d{2}/)
  })
})
