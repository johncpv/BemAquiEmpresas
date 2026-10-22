import type { Route } from '@playwright/test'
import { json } from './mock-api'

type Handler = (r: Route) => Promise<void> | void

/** Backend em memória da EST-01a (as regras reais estão nos testes de integração .NET). */
export function stockApi(options: { balances?: boolean; openingOpen?: boolean; reverseRule?: string } = {}) {
  const calls: { url: string; body: unknown }[] = []
  const record = (r: Route) => calls.push({ url: r.request().url(), body: r.request().postDataJSON() })
  const state = { postingStatus: 'Pending', version: 1, reversed: false }
  const balance = { itemId: 'i1', itemCode: 'MP-001', itemName: 'Chapa de aço 3 mm', unitCode: 'UN', itemType: 'RawMaterial', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', onHand: 25, reserved: 0, available: 25, minStock: 10, averageCost: 50, situation: 'Normal' }
  const context = {
    itemId: 'i1', code: 'MP-001', name: 'Chapa de aço 3 mm', unitId: 'un', unitCode: 'UN', tracksLot: false, isActive: true, type: 'RawMaterial', averageCost: options.balances === false ? 0 : 50, totalOnHand: options.balances === false ? 0 : 25,
    positions: options.balances === false ? [] : [{ locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', onHand: 25, reserved: 0, available: 25, hasMovement: true }],
    lots: [], suggestedLotNumber: null, defaultLocationId: 'alm', manualApprovalAmount: 1000, manualCostDeviationPct: 20, openingOpen: options.openingOpen ?? true,
  }
  const posting = (kind: string, status: string) => ({
    id: 'p1', number: 'MOV-2026-000012', kind, status, approvalReason: status === 'Pending' ? 'Acima de R$ 1.000,00' : null, estimatedValue: 1050, reason: 'Perda na obra', projectId: null, projectNumber: null, cutoffDate: null,
    requestedBy: 'u2', requestedByName: 'Bruno Almoxarife', requestedAt: '2026-10-22T12:00:00Z', decidedByName: status === 'Posted' ? 'Ana Souza' : null, decidedAt: null, decisionComment: null, selfApproved: false, postedAt: null,
    lines: [{ id: 'l1', sequence: 1, itemId: 'i1', itemCode: 'MP-001', itemName: 'Chapa de aço 3 mm', unitCode: 'UN', locationId: 'alm', locationName: 'Almoxarifado principal', toLocationId: null, toLocationName: null, lotNumber: null, quantity: 21, enteredQuantity: 21, enteredUnitCode: 'UN', unitCost: null, movementId: status === 'Posted' ? 'm1' : null, inMovementId: null, reversedAt: null, reversedByName: null }],
    impact: status === 'Pending' ? { onHandBefore: 25, onHandAfter: 4, averageBefore: 50, averageAfter: 50, value: 1050, insufficient: false } : null,
    canDecide: status === 'Pending', canCancel: false, decideBlockedReason: null, version: state.version,
  })
  const movement = () => ({
    movement: { id: 'm1', number: 'MOV-2026-000010', type: 'Receipt', at: '2026-10-22T12:00:00Z', postedAt: '2026-10-22T12:00:00Z', postedSeq: 10, itemId: 'i1', itemCode: 'MP-001', itemName: 'Chapa de aço 3 mm', unitCode: 'UN',
      locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', lotId: null, lotNumber: null, quantity: 10, unitCost: 20, totalCost: 200, balanceAfter: 10, averageCostAfter: 20, origin: 'Manual', sourceType: 'ManualPosting',
      sourceId: 'p9', sourceNumber: 'MOV-2026-000010', sourceLink: '/app/estoque/lancamentos/p9', projectId: null, productionOrderId: null, reversesMovementId: null,
      reversedByMovementId: state.reversed ? 'm2' : null, reversedAt: state.reversed ? '2026-10-22T13:00:00Z' : null, userName: 'Bruno Almoxarife' },
    reason: 'Doação', projectNumber: null, reversedByName: state.reversed ? 'Bruno Almoxarife' : null, reversalReason: state.reversed ? 'Lançado em dobro' : null, reversalSelfApproved: false, reversesNumber: null,
    manualPostingId: 'p9', authorName: 'Bruno Almoxarife', reverseRule: state.reversed ? 'AlreadyReversed' : (options.reverseRule ?? 'Author'), reverseMessage: state.reversed ? 'Este movimento já foi estornado.' : options.reverseRule === 'Denied' ? 'Este estorno precisa de outra pessoa com permissão de aprovar no Estoque.' : null, onHandNow: 10, averageCostNow: 20,
  })
  const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })
  const api: Record<string, Handler> = {
    'GET /api/v1/inventory/opening': (r) => json(r, { completed: !(options.openingOpen ?? true), completedAt: options.openingOpen === false ? '2026-10-20T15:00:00Z' : null, completedByName: options.openingOpen === false ? 'Ana Souza' : null, itemCount: 3, locationCount: 1, lineCount: 3, totalValue: 4500, manualApprovalAmount: 1000, manualCostDeviationPct: 20, version: 1 }),
    'GET /api/v1/inventory/balances': (r) => json(r, page(options.balances === false ? [] : [balance])),
    'GET /api/v1/inventory/locations': (r) => json(r, page([{ id: 'alm', code: 'ALM', name: 'Almoxarifado principal', isActive: true, isDefault: true, seedKey: 'alm', importSource: null, createdAt: '2026-10-01T00:00:00Z' }, { id: 'exp', code: 'EXP', name: 'Expedição', isActive: true, isDefault: false, seedKey: null, importSource: null, createdAt: '2026-10-01T00:00:00Z' }])),
    'GET /api/v1/inventory/categories': (r) => json(r, []),
    'GET /api/v1/inventory/items/search': (r) => json(r, [{ id: 'i1', code: 'MP-001', name: 'Chapa de aço 3 mm', type: 'RawMaterial' }]),
    'GET /api/v1/inventory/items/*/stock': (r) => json(r, context),
    'GET /api/v1/inventory/items/*/conversions': (r) => json(r, []),
    'GET /api/v1/inventory/projects': (r) => json(r, []),
    'POST /api/v1/inventory/opening-balances': (r) => { record(r); return json(r, { ...posting('OpeningBalance', 'Posted'), id: 'p0' }, 201) },
    'POST /api/v1/inventory/manual-postings': (r) => { record(r); const body = r.request().postDataJSON() as { kind: string; quantity: number }; return json(r, posting(body.kind, body.quantity * 50 > 1000 ? 'Pending' : 'Posted'), 201) },
    'GET /api/v1/inventory/manual-postings/*': (r) => json(r, posting('Issue', state.postingStatus)),
    'POST /api/v1/inventory/manual-postings/*/approve': (r) => { record(r); state.postingStatus = 'Posted'; state.version++; return json(r, posting('Issue', 'Posted')) },
    'GET /api/v1/inventory/movements/*': (r) => json(r, movement()),
    'POST /api/v1/inventory/movements/*/reverse': (r) => { record(r); state.reversed = true; return json(r, movement()) },
    'GET /api/v1/inventory/movements': (r) => json(r, page([movement().movement])),
  }
  return { api, calls, state }
}
