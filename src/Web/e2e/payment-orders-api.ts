import type { Route } from '@playwright/test'
import { json } from './mock-api'

/** Backend em memória da FIN-03a (as regras reais estão nos testes de integração .NET). */
export function ordersApi(start: 'Pending' | 'Approved', isRequester = false) {
  const state = { status: start as string, version: 1 }
  const calls: { url: string; body: unknown }[] = []
  const order = () => ({
    summary: { id: 'op1', number: 'OP-2026-0001', status: state.status, total: 950, expectedDate: '2026-10-25', requestedBy: 'u1', requesterName: 'Ana Financeiro', items: 1, selfApproved: false, submittedAt: '2026-10-20T10:00:00Z' },
    items: [{ id: 'i1', payableId: 'p1', payableNumber: 'CP-2026-00007', description: 'Chapas', partyName: 'Gama Aços', netAmount: 950, retentions: 50, amount: 950, expectedCharges: 0, approvedTotal: 950, dueDate: '2026-10-25',
      payment: { kind: 'Pix', pixKeyType: 'Cpf', pixKey: '529*****725', holderName: 'Fulano de Tal', holderDocument: '***.982.247-**' }, warnings: ['O titular não é o fornecedor.'], paidTotal: 0 }],
    decisions: [], isRequester, canDecide: !isRequester && state.status === 'Pending', cancelReason: null, reversalReason: null, paidOn: null, bankAccountId: null, version: state.version,
  })
  const decide = (status: string) => (r: Route) => { calls.push({ url: r.request().url(), body: r.request().postDataJSON() }); state.status = status; state.version++; return json(r, order()) }
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/finance/payment-orders/*': (r) => json(r, order()),
    'POST /api/v1/finance/payment-orders/*/approve': decide('Approved'),
    'POST /api/v1/finance/payment-orders/*/pay': (r) => { calls.push({ url: r.request().url(), body: r.request().postDataJSON() }); state.status = 'Paid'; state.version++; return json(r, { order: order(), total: 950, retentions: [{ payableId: 'p1', payableNumber: 'CP-2026-00007', amount: 50, suggestedDueDate: '2026-11-20' }] }) },
    'GET /api/v1/finance/bank-accounts': (r) => json(r, { items: [{ id: 'b1', name: 'Banco Inter', kind: 'Bank', isActive: true, isDefault: true }], page: 1, pageSize: 100, total: 1 }),
    'GET /api/v1/finance/cash-flow': (r) => json(r, { currentBalance: 500, periods: [], view: 'daily', consolidated: false, today: '2026-10-20', from: '2026-10-20', to: '2026-10-20', bankAccountId: 'b1', lowest: null, firstNegative: null, undatedReceivables: { count: 0, total: 0 } }),
    'GET /api/v1/attachments/PaymentOrder/*': (r) => json(r, []),
  }
  return { api, calls, state }
}
