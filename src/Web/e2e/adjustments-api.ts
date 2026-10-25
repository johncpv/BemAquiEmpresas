import type { Route } from '@playwright/test'
import { allModules, json } from './mock-api'

type Handler = (r: Route) => Promise<void> | void

/** Permissões de quem só lança (Almoxarifado do seed com o patch inventory-adjust-warehouse): sem inventory.approve. */
export const warehousePermissions = () => ({
  permissions: [], isOwner: false, version: '2',
  modules: allModules().map((m) => (m.code === 'inventory' ? { ...m, actions: ['view', 'create', 'edit', 'adjust'] } : m.code === 'production' ? { ...m, actions: ['view'] } : { ...m, enabled: false })),
})

/** Proprietário: todas as ações do catálogo, inclusive a específica inventory.adjust. */
export const ownerPermissions = () => ({
  permissions: [], isOwner: true, version: '1',
  modules: allModules().map((m) => (m.code === 'inventory' ? { ...m, actions: [...m.actions, 'adjust'] } : m)),
})

/** Backend em memória da EST-01b (as regras reais estão nos testes de integração .NET). */
export function adjustmentsApi(options: { status?: 'Pending' | 'Approved' } = {}) {
  const calls: { url: string; body: unknown }[] = []
  const record = (r: Route) => calls.push({ url: r.request().url(), body: r.request().postDataJSON() })
  const state = { status: options.status ?? 'Pending', version: 1, created: options.status !== undefined }
  const context = {
    itemId: 'i1', code: 'MP-001', name: 'Guia T89', unitId: 'un', unitCode: 'UN', tracksLot: false, isActive: true, type: 'RawMaterial', averageCost: 12.5, totalOnHand: 10,
    positions: [{ locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', onHand: 10, reserved: 4, available: 6, hasMovement: true }],
    lots: [], suggestedLotNumber: null, defaultLocationId: 'alm', manualApprovalAmount: 1000, manualCostDeviationPct: 20, openingOpen: false,
  }
  const approved = () => state.status === 'Approved'
  const line = () => ({
    id: 'l1', sequence: 1, itemId: 'i1', itemCode: 'MP-001', itemName: 'Guia T89', unitCode: 'UN', tracksLot: false, lotId: null, lotNumber: null,
    systemQtyAtRequest: 10, countedQty: 8, deltaQty: -2, enteredQuantity: 8, enteredUnitCode: 'UN', unitCostEstimate: 12.5, estimatedValue: 25,
    onHand: approved() ? 8 : 11, reserved: 4, available: approved() ? 4 : 7, systemQtyNow: approved() ? 8 : 11, deltaNow: approved() ? 0 : -3, valueNow: approved() ? 0 : 37.5,
    onHandAfter: 8, changed: !approved(), refusal: null,
    systemQtyAtApproval: approved() ? 11 : null, appliedDeltaQty: approved() ? -3 : null, unitCost: approved() ? 12.5 : null, movementId: approved() ? 'm1' : null,
    movementNumber: approved() ? 'MOV-2026-000031' : null, belowMinimum: approved(),
  })
  const detail = () => ({
    id: 'a1', number: 'AJ-2026-0001', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', mode: 'Counted', origin: 'Manual', originId: null, originNumber: null,
    originLink: null, reason: 'Loss', reasonLabel: 'Perda/avaria', reasonDetail: null, status: state.status, requestedBy: 'u2', requestedByName: 'Bruno Almoxarife',
    requestedAt: '2026-10-24T12:00:00Z', decidedByName: approved() ? 'Ana Souza' : null, decidedAt: approved() ? '2026-10-24T13:00:00Z' : null, decisionComment: null, selfApproved: false,
    cancelReason: null, reversedAt: null, reversedByName: null, reversalReason: null, reversalSelfApproved: false, itemCount: 1, surplusValue: 0, shortageValue: approved() ? 37.5 : 25,
    differenceValue: approved() ? 37.5 : 25, lines: [line()],
    impact: approved() ? null : { changedSinceRequest: true, surplus: 0, shortage: 37.5, refusal: null, refusalItemId: null },
    canDecide: !approved(), canCancel: false, canReverse: approved(), decideBlockedReason: null, reverseBlockedReason: null, version: state.version,
  })
  const summary = () => ({
    id: 'a1', number: 'AJ-2026-0001', locationId: 'alm', locationName: 'Almoxarifado principal', mode: 'Counted', origin: 'Manual', status: state.status, reversed: false,
    requestedByName: 'Bruno Almoxarife', requestedAt: '2026-10-24T12:00:00Z', itemCount: 1, surplusValue: 0, shortageValue: 25, differenceValue: 25, selfApproved: false, awaitingMe: !approved(),
  })
  const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })
  const api: Record<string, Handler> = {
    'GET /api/v1/inventory/locations': (r) => json(r, page([
      { id: 'alm', code: 'ALM', name: 'Almoxarifado principal', isActive: true, isDefault: true, seedKey: 'alm', importSource: null, createdAt: '2026-10-01T00:00:00Z' },
      { id: 'exp', code: 'EXP', name: 'Expedição', isActive: true, isDefault: false, seedKey: null, importSource: null, createdAt: '2026-10-01T00:00:00Z' },
    ])),
    'GET /api/v1/inventory/items/search': (r) => json(r, [{ id: 'i1', code: 'MP-001', name: 'Guia T89', type: 'RawMaterial' }]),
    'GET /api/v1/inventory/items/*/stock': (r) => json(r, context),
    'GET /api/v1/inventory/items/*/conversions': (r) => json(r, []),
    'GET /api/v1/inventory/adjustments': (r) => json(r, page(state.created ? [summary()] : [])),
    'POST /api/v1/inventory/adjustments': (r) => { record(r); state.created = true; return json(r, detail(), 201) },
    'GET /api/v1/inventory/adjustments/*': (r) => json(r, detail()),
    'POST /api/v1/inventory/adjustments/*/approve': (r) => { record(r); state.status = 'Approved'; state.version++; return json(r, detail()) },
    'GET /api/v1/notifications/': (r) => json(r, {
      page: { items: [{ id: 'n1', type: 'inventory.adjustment_pending', title: 'Ajuste AJ-2026-0001 de Almoxarifado principal aguardando aprovação: 1 item, R$ 25,00 de diferença.', link: '/app/estoque/ajustes/a1', createdAt: '2026-10-24T12:00:00Z', readAt: null }], page: 1, pageSize: 10, total: 1 },
      unread: 1,
    }),
  }
  return { api, calls, state }
}
