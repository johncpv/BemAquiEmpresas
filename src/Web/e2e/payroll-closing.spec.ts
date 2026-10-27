import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'
import { ordersApi } from './payment-orders-api'

async function noHorizontalScroll(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
}

const closer = allModules().map((m) => (m.code === 'hr' ? { ...m, actions: ['view', 'create', 'edit', 'approve', 'view_sensitive'] } : m.code === 'finance' ? { ...m, actions: ['view', 'create'] } : m))
const payer = allModules().map((m) => (m.code === 'finance' ? { ...m, actions: ['view', 'create', 'edit', 'pay'] } : m))

/** Backend em memória da RH-02c (as regras reais estão nos testes de integração .NET). */
function closingApi() {
  const state = { status: 'Draft', closeBody: null as Record<string, unknown> | null, idempotency: null as string | null }
  const run = () => ({
    id: 'r1', period: '2026-10-01', type: 'Monthly', mode: 'Entered', status: state.status, payslipCount: 2, createdAt: '2026-10-27T12:00:00Z',
    closedAt: state.status === 'Draft' ? null : '2026-10-30T15:00:00Z', closedByName: state.status === 'Draft' ? null : 'Ana Souza', version: state.status === 'Draft' ? 1 : 2,
    paymentDate: state.status === 'Draft' ? null : '2026-11-06', paidAt: null, reopenReason: null, payableCount: state.status === 'Draft' ? null : 3,
  })
  const row = (id: string, number: string, salary: boolean, name: string, amount: number, quarantine = false) => ({
    payableId: id, number, isSalary: salary, originId: 'o-' + id, employeeId: salary ? 'e-' + id : null, payeeName: name, amount, dueDate: '2026-11-06', status: 'Open', statusLabel: 'Em aberto',
    paymentOrderId: null, inQuarantine: quarantine, quarantineUntil: quarantine ? '2026-11-20T12:00:00Z' : null,
  })
  const quarantined = row('p2', 'CP-2026-00102', true, '102 — Rui Prado', 2800, true)
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/hr/payroll-runs/*': (r) => json(r, run()),
    'GET /api/v1/hr/payroll-runs/*/payslips/sensitive': (r) => json(r, []),
    'GET /api/v1/me/2fa': (r) => json(r, { enabled: true, recoveryCodesLeft: 8 }),
    'GET /api/v1/hr/payroll-runs/*/close/sensitive': (r) => json(r, {
      runId: 'r1', period: '2026-10-01', type: 'Monthly', blockers: [], suggestedPaymentDate: '2026-11-06', paymentDateRequired: false, paymentDateConfirmed: false,
      newcomers: [{ employeeId: 'e2', name: 'Rui Prado', registration: '102', quarantineUntil: null }],
      accountChanged: [{ employeeId: 'e2', name: 'Rui Prado', registration: '102', quarantineUntil: '2026-11-20T12:00:00Z' }],
      salaryCount: 2, salaryTotal: 6100, chargeCount: 1, chargeTotal: 480, version: 1,
    }),
    'POST /api/v1/hr/payroll-runs/*/close': (r) => {
      state.closeBody = r.request().postDataJSON()
      state.idempotency = r.request().headers()['idempotency-key'] ?? null
      state.status = 'Closed'
      return json(r, { run: run(), payables: 3, message: 'Folha de outubro de 2026 fechada. 3 títulos a pagar gerados.' })
    },
    'GET /api/v1/hr/payroll-runs/*/payments/sensitive': (r) => json(r, {
      items: [row('p1', 'CP-2026-00101', true, '101 — Joana Lima', 3300), quarantined, row('p3', 'CP-2026-00103', false, 'FGTS — FGTS outubro de 2026', 480)],
      salaryTotal: 6100, chargeTotal: 480, salaryPreselected: ['p1'], quarantined: [quarantined], chargePreselected: ['p3'], canSeeStatus: true,
    }),
    'GET /api/v1/finance/payables/*': (r) => json(r, {
      id: 'p1', number: 'CP-2026-00101', partyId: null, partyName: 'Funcionário 101 — Joana Lima', description: 'Salário Mensal outubro de 2026 — 101', accountId: 'a', accountName: '3.1 Pessoal administrativo',
      costCenterId: null, amount: 3300, retentionIss: 0, retentionInss: 0, retentionIr: 0, retentionPcc: 0, netAmount: 3300, paidAmount: 0, balance: 3300, issueDate: '2026-10-30', dueDate: '2026-11-06',
      isEstimate: false, status: 'Open', isOverdue: false, isDueToday: false, origin: 'Payroll', originId: 'o-p1', openingReference: null, installmentGroupId: null, competence: null,
      paymentMethod: 'EmployeeAccount', paymentMethodChangedAt: null, cancelReason: null, cancelledAt: null, version: 1, payeeType: 'Employee', payeeId: 'e1',
    }),
  }
  return { state, api }
}

test.describe('RH-02c: fechamento da folha e salários no Financeiro', () => {
  test('fecha com 2FA, vê os pagamentos e abre a OP dos salários sem a conta em quarentena (CA 1, 2, 5 e 9)', async ({ page }) => {
    const { state, api } = closingApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '21', modules: closer }) })
    await page.goto('/app/rh/folha/r1')
    await page.getByRole('button', { name: 'Fechar folha' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('Gera 2 títulos a pagar de salários, total R$ 6.100,00, e 1 título de guia, total R$ 480,00, que seguem para ordem de pagamento.')).toBeVisible()
    await expect(dialog.getByText('Novos na folha')).toBeVisible()
    await expect(dialog.getByText(/Rui Prado: conta em quarentena até 20\/11\/2026/)).toBeVisible()
    await expect(dialog.getByText('A confirmar com a contadora')).toBeVisible()
    await expect(dialog.getByRole('textbox', { name: /Data de pagamento/ })).toHaveValue(/06\/11\/2026|2026-11-06/)
    await dialog.getByLabel('Código do aplicativo autenticador').fill('123456')
    await dialog.getByRole('button', { name: 'Fechar folha' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Folha fechada' }).first()).toBeVisible()
    expect(state.closeBody).toMatchObject({ paymentDate: '2026-11-06', twoFactorCode: '123456', version: 1 })
    expect(state.idempotency).toMatch(/^[0-9a-f-]{36}$/)
    await expect(page.getByText('Para mudar, reabra a folha.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Reabrir folha' })).toBeVisible()

    await page.getByRole('tab', { name: 'Pagamentos' }).click()
    const outside = page.getByRole('region', { name: 'Fora do pagamento automático' })
    await expect(outside).toContainText('102 — Rui Prado: conta em quarentena até 20/11/2026')
    await expect(outside.getByRole('link', { name: 'Revisar conta' })).toBeVisible()
    await noHorizontalScroll(page)
    await page.getByRole('button', { name: 'Criar ordem de pagamento dos salários' }).click()
    await expect(page).toHaveURL(/ordens-de-pagamento\/nova\?titulos=p1$/)
    await expect(page.getByText('Funcionário 101 — Joana Lima')).toBeVisible()
    await expect(page.getByText('Conta do funcionário')).toBeVisible()
    await noHorizontalScroll(page)
  })

  test('a conta do funcionário só é lida ao abrir o "Pagar" (CA 10)', async ({ page }) => {
    const { api } = ordersApi('Approved')
    const reads: string[] = []
    const order = {
      summary: { id: 'op1', number: 'OP-2026-0009', status: 'Approved', total: 3300, expectedDate: '2026-11-06', requestedBy: 'u9', requesterName: 'Ana Financeiro', items: 1, selfApproved: false, submittedAt: '2026-10-30T10:00:00Z' },
      items: [{ id: 'i1', payableId: 'p1', payableNumber: 'CP-2026-00101', description: 'Salário Mensal outubro de 2026 — 101', partyName: 'Funcionário 101 — Joana Lima', netAmount: 3300, retentions: 0,
        amount: 3300, expectedCharges: 0, approvedTotal: 3300, dueDate: '2026-11-06', payment: { kind: 'EmployeeAccount', account: '****-43' }, warnings: ['Novo na folha.'], paidTotal: 0, payeeType: 'Employee' }],
      decisions: [], isRequester: false, canDecide: false, cancelReason: null, reversalReason: null, paidOn: null, bankAccountId: null, version: 3,
    }
    await mockApi(page, {
      ...api,
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '22', modules: payer }),
      'GET /api/v1/finance/payment-orders/*': (r) => json(r, order),
      'GET /api/v1/finance/payment-orders/*/payee-accounts': (r) => {
        reads.push(r.request().url())
        return json(r, [{ itemId: 'i1', payableNumber: 'CP-2026-00101', employeeName: 'Joana Lima', registration: '101', bankCode: '077', bankName: 'Inter', agency: '0001', account: '7766554-3',
          accountType: 'Checking', holderName: 'Joana Lima', holderCpf: '52998224725', pixKeyType: null, pixKey: null }])
      },
    })
    await page.goto('/app/financeiro/ordens-de-pagamento/op1')
    await expect(page.getByText('Funcionário 101 — Joana Lima').first()).toBeVisible()
    expect(reads).toEqual([])
    await page.getByRole('button', { name: 'Pagar', exact: true }).click()
    const block = page.getByRole('region', { name: 'Para onde vai o dinheiro' })
    await expect(block).toContainText('Banco 077 Inter · agência 0001 · conta 7766554-3')
    expect(reads).toHaveLength(1)
    await noHorizontalScroll(page)
  })
})
