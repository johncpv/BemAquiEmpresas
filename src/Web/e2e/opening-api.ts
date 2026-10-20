import type { Route } from '@playwright/test'
import { json } from './mock-api'

const ACCOUNTS = [
  { id: 'r9', code: '1.9', name: 'Receitas a classificar', type: 'Revenue', dreGroup: 'GrossRevenue', parentId: null, acceptsEntries: true, isActive: true, seedKey: '1.9' },
  { id: 'x9', code: '3.9', name: 'Despesas a classificar', type: 'Expense', dreGroup: 'OperatingExpenses', parentId: null, acceptsEntries: true, isActive: true, seedKey: '3.9' },
]

type Totals = { partyId: string; partyName: string; count: number; total: number }[]

/** Backend em memória da FIN-00 (as regras reais estão nos testes de integração .NET). Nunca toca o tenant konebras. */
export function openingApi() {
  const state = {
    cutoverDate: null as string | null, suggestedCutover: '2026-09-30', completedAt: null as string | null, notes: null as string | null, hasMovement: false, version: 1,
    accounts: [{ id: 'b1', name: 'Banco Inter', openingBalance: null as number | null, openingDate: null as string | null, hasMovement: false, systemBalanceAtCutover: 0 }],
    receivables: [] as Totals,
    payables: [] as Totals,
  }
  const posted: { body: { lines: { balance: number }[] }; key?: string }[] = []
  let attempts = 0
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/finance/opening': (r) => json(r, state),
    'PUT /api/v1/finance/opening/cutover': (r) => { state.cutoverDate = (r.request().postDataJSON() as { cutoverDate: string }).cutoverDate; return json(r, state) },
    'PUT /api/v1/finance/opening/balances': (r) => {
      const { lines } = r.request().postDataJSON() as { lines: { balance: number | null }[] }
      state.accounts[0] = { ...state.accounts[0], openingBalance: lines[0].balance, openingDate: state.cutoverDate, systemBalanceAtCutover: lines[0].balance ?? 0 }
      return json(r, state)
    },
    'POST /api/v1/finance/opening/receivables': (r) => {
      const body = r.request().postDataJSON() as { lines: { balance: number }[] }
      posted.push({ body, key: r.request().headers()['idempotency-key'] })
      if (attempts++ === 0) return json(r, { title: 'Dados inválidos.', detail: 'Dados inválidos.', status: 422, errors: { 'lines[1].description': ['O documento de origem já foi lançado.'] } }, 422)
      const total = body.lines.reduce((n, l) => n + l.balance, 0)
      state.receivables = [{ partyId: 'c1', partyName: 'Aurora Condomínio', count: body.lines.length, total }]
      return json(r, { count: body.lines.length, total })
    },
    'POST /api/v1/finance/opening/complete': (r) => { state.completedAt = new Date().toISOString(); return json(r, state) },
    'GET /api/v1/finance/chart-of-accounts': (r) => json(r, ACCOUNTS),
    'GET /api/v1/finance/cost-centers': (r) => json(r, []),
    'GET /api/v1/finance/customers': (r) => json(r, [{ id: 'c1', name: 'Aurora Condomínio', document: null, defaultPaymentTermId: null }]),
  }
  return { api, posted, state }
}
