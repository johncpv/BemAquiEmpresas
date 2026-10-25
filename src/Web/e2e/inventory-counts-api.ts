import type { Route } from '@playwright/test'
import { json } from './mock-api'

type Handler = (r: Route) => Promise<void> | void

/** Backend em memória da EST-05 (as regras reais estão nos testes de integração .NET): inventário c1 no Almoxarifado. */
export function inventoryCountsApi(options: { me?: 'ana' | 'bruno' | 'carla'; opened?: boolean } = {}) {
  const me = options.me ?? 'ana'
  const names = { ana: 'Ana Souza', bruno: 'Bruno Almoxarife', carla: 'Carla Conferente' }
  const calls: { url: string; method: string; body: unknown }[] = []
  const record = (r: Route) => calls.push({ url: r.request().url(), method: r.request().method(), body: r.request().postDataJSON() })
  const state = {
    opened: options.opened ?? false, status: 'Counting' as 'Counting' | 'Closed', version: 1, adjustment: null as null | 'Pending' | 'Approved', recount: false,
    lines: [
      { id: 'l1', sequence: 1, itemId: 'i1', itemCode: 'MP-001', itemName: 'Guia T89', lotNumber: null as string | null, countedQty: 8 as number | null, countedBy: 'bruno', isRecount: false, version: 2 },
      { id: 'l2', sequence: 2, itemId: 'i2', itemCode: 'MP-002', itemName: 'Polia 400', lotNumber: 'L-77', countedQty: null as number | null, countedBy: null as string | null, isRecount: false, version: 1 },
    ],
  }
  const counted = () => state.lines.filter((l) => l.countedQty !== null).length
  const progress = () => `${counted()} de ${state.lines.length} itens contados`
  const detail = () => ({
    id: 'c1', number: 'INV-2026-0001', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', status: state.status, statusLabel: state.status === 'Counting' ? 'Em contagem' : 'Fechado',
    scopeLabel: 'Itens com saldo no local', snapshotAt: '2026-10-24T12:00:00Z', openedByName: 'Ana Souza', closedByName: state.status === 'Closed' ? 'Ana Souza' : null,
    closedAt: state.status === 'Closed' ? '2026-10-24T15:00:00Z' : null, uncountedPolicy: state.status === 'Closed' ? 'Zero' : null, cancelReason: null, cancelledByName: null, cancelledAt: null,
    reopenCount: 0, lineCount: state.lines.length, countedCount: counted(), recountPending: state.recount ? 1 : 0, progress: progress(),
    adjustmentId: state.adjustment ? 'a9' : null, adjustmentNumber: state.adjustment ? 'AJ-2026-0009' : null, adjustmentStatus: state.adjustment,
    adjustmentSituation: state.adjustment ? `Ajuste AJ-2026-0009 ${state.adjustment === 'Pending' ? 'aguardando aprovação' : 'aprovado'}` : null,
    canCount: state.status === 'Counting', canManage: me === 'ana', canCancel: state.status === 'Counting' && me === 'ana', canReopen: false, version: state.version,
  })
  const countingLine = (l: (typeof state.lines)[number]) => ({
    id: l.id, sequence: l.sequence, itemId: l.itemId, itemCode: l.itemCode, itemName: l.itemName, unitCode: 'UN', stockUnitId: 'un', units: [{ id: 'un', code: 'UN' }], tracksLot: !!l.lotNumber,
    lotId: l.lotNumber ? 'lot77' : null, lotNumber: l.lotNumber, countedQty: l.countedQty, enteredQuantity: l.countedQty, enteredUnitCode: l.countedQty === null ? null : 'UN',
    countedByName: l.countedBy ? names[l.countedBy as keyof typeof names] : null, countedAt: l.countedQty === null ? null : '2026-10-24T13:00:00Z', countedByMe: l.countedBy === me,
    isRecount: l.isRecount, addedDuringCount: false, version: l.version,
  })
  // CA 8: a recontagem não aparece para quem contou a original (Bruno).
  const visible = () => state.lines.filter((l) => !(l.isRecount && l.countedQty === null && me === 'bruno'))
  const counting = () => ({
    countId: 'c1', number: 'INV-2026-0001', locationId: 'alm', locationName: 'Almoxarifado principal', status: state.status, lineCount: state.lines.length, countedCount: counted(),
    progress: progress(), canCount: state.status === 'Counting', lines: visible().map(countingLine), version: state.version,
  })
  const divergenceLines = (zero: boolean) => {
    const out = []
    const l1 = state.lines.find((l) => l.itemId === 'i1')!
    if (l1.countedQty !== null) {
      out.push({
        id: l1.id, sequence: 1, itemId: 'i1', itemCode: 'MP-001', itemName: 'Guia T89', unitCode: 'UN', lotId: null, lotNumber: null, systemQty: 10, countedQty: l1.countedQty, counted: true,
        countedByName: names[l1.countedBy as keyof typeof names], countedAt: '2026-10-24T13:00:00Z', movementDelta: -1, hadMovement: true, difference: l1.countedQty - 9, unitCost: 12.5,
        value: Math.abs(l1.countedQty - 9) * 12.5, systemQtyNow: 9, after: l1.countedQty, isRecount: l1.isRecount, previousCountedByName: l1.isRecount ? 'Bruno Almoxarife' : null,
        previousCountedQty: l1.isRecount ? 8 : null, recountPending: false, canRequestRecount: state.status === 'Counting',
      })
    }

    const l2 = state.lines.find((l) => l.itemId === 'i2')!
    if (l2.countedQty !== null || zero) {
      const c = l2.countedQty ?? 0
      out.push({
        id: l2.id, sequence: 2, itemId: 'i2', itemCode: 'MP-002', itemName: 'Polia 400', unitCode: 'UN', lotId: 'lot77', lotNumber: 'L-77', systemQty: 4, countedQty: l2.countedQty, counted: l2.countedQty !== null,
        countedByName: l2.countedBy ? names[l2.countedBy as keyof typeof names] : null, countedAt: null, movementDelta: 0, hadMovement: false, difference: c - 4, unitCost: 30, value: Math.abs(c - 4) * 30,
        systemQtyNow: 4, after: c, isRecount: false, previousCountedByName: null, previousCountedQty: null, recountPending: false, canRequestRecount: l2.countedQty !== null,
      })
    }

    return out.filter((l) => l.difference !== 0).sort((a, b) => b.value - a.value)
  }
  const divergences = (url: string) => {
    const zero = new URL(url).searchParams.get('uncountedPolicy') === 'Zero'
    const lines = divergenceLines(zero)
    const surplus = lines.filter((l) => l.difference > 0).reduce((s, l) => s + l.value, 0)
    const shortage = lines.filter((l) => l.difference < 0).reduce((s, l) => s + l.value, 0)
    const uncounted = state.lines.filter((l) => l.countedQty === null).length
    return {
      countId: 'c1', number: 'INV-2026-0001', status: state.status, uncounted, uncountedQuestion: uncounted ? `Considerar ${uncounted} ${uncounted === 1 ? 'item não contado' : 'itens não contados'} como zero?` : null,
      policy: zero ? 'Zero' : null, surplus, shortage, net: surplus - shortage, lines, version: state.version,
    }
  }
  const summary = () => ({ id: 'c1', number: 'INV-2026-0001', locationId: 'alm', locationName: 'Almoxarifado principal', status: state.status, snapshotAt: '2026-10-24T12:00:00Z', openedByName: 'Ana Souza', lineCount: 2, countedCount: counted(), adjustmentId: null, adjustmentNumber: null })
  const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length })
  const adjustment = () => ({
    id: 'a9', number: 'AJ-2026-0009', locationId: 'alm', locationCode: 'ALM', locationName: 'Almoxarifado principal', mode: 'Delta', origin: 'InventoryCount', originId: 'c1', originNumber: 'INV-2026-0001',
    originLink: '/app/estoque/inventarios/c1', reason: 'Count', reasonLabel: 'Contagem', reasonDetail: 'Inventário INV-2026-0001', status: state.adjustment ?? 'Pending', requestedBy: 'u1', requestedByName: 'Ana Souza',
    requestedAt: '2026-10-24T15:00:00Z', decidedByName: state.adjustment === 'Approved' ? 'Carla Conferente' : null, decidedAt: state.adjustment === 'Approved' ? '2026-10-24T16:00:00Z' : null,
    decisionComment: null, selfApproved: false, cancelReason: null, reversedAt: null, reversedByName: null, reversalReason: null, reversalSelfApproved: false, itemCount: 1, surplusValue: 0, shortageValue: 120,
    differenceValue: 120, lines: [{
      id: 'al1', sequence: 1, itemId: 'i2', itemCode: 'MP-002', itemName: 'Polia 400', unitCode: 'UN', tracksLot: true, lotId: 'lot77', lotNumber: 'L-77', systemQtyAtRequest: 4, countedQty: null, deltaQty: -4,
      enteredQuantity: -4, enteredUnitCode: 'UN', unitCostEstimate: 30, estimatedValue: 120, onHand: 4, reserved: 3, available: 1, systemQtyNow: 4, deltaNow: -4, valueNow: 120, onHandAfter: 0, changed: false,
      refusal: null, systemQtyAtApproval: null, appliedDeltaQty: null, unitCost: null, movementId: null, movementNumber: null, belowMinimum: false,
    }],
    impact: state.adjustment === 'Approved' ? null : {
      changedSinceRequest: false, surplus: 0, shortage: 120, refusal: null, refusalItemId: null,
      reservationNote: 'MP-002: a contagem ficou abaixo do reservado; a aprovação reduz 3,0000 UN das reservas das OS com a BOM liberada mais recentemente e gera a falta.',
    },
    canDecide: state.adjustment !== 'Approved', canCancel: false, canReverse: false, decideBlockedReason: null, reverseBlockedReason: null, version: 1,
  })
  const api: Record<string, Handler> = {
    'GET /api/v1/inventory/locations': (r) => json(r, page([
      { id: 'alm', code: 'ALM', name: 'Almoxarifado principal', isActive: true, isDefault: true, seedKey: 'alm', importSource: null, createdAt: '2026-10-01T00:00:00Z' },
    ])),
    'GET /api/v1/inventory/categories': (r) => json(r, []),
    'GET /api/v1/inventory/inventory-counts': (r) => json(r, page(state.opened ? [summary()] : [])),
    'POST /api/v1/inventory/inventory-counts': (r) => { record(r); state.opened = true; return json(r, detail(), 201) },
    'GET /api/v1/inventory/inventory-counts/c1': (r) => json(r, detail()),
    'GET /api/v1/inventory/inventory-counts/c1/counting': (r) => json(r, counting()),
    'GET /api/v1/inventory/inventory-counts/c1/divergences': (r) => json(r, divergences(r.request().url())),
    'GET /api/v1/inventory/inventory-counts/c1/history': (r) => json(r, page([])),
    'PUT /api/v1/inventory/inventory-counts/c1/lines/*': (r) => {
      record(r)
      const id = new URL(r.request().url()).pathname.split('/').pop()
      const line = state.lines.find((l) => l.id === id)!
      line.countedQty = (r.request().postDataJSON() as { quantity: number }).quantity
      line.countedBy = me
      line.version++
      return json(r, countingLine(line))
    },
    'POST /api/v1/inventory/inventory-counts/c1/recount': (r) => {
      record(r)
      state.recount = true
      const original = state.lines.find((l) => l.id === 'l1')!
      state.lines = [state.lines.find((l) => l.id === 'l2')!, { ...original, id: 'l3', sequence: 3, countedQty: null, countedBy: null, isRecount: true, version: 1 }]
      return json(r, divergences(r.request().url()))
    },
    'POST /api/v1/inventory/inventory-counts/c1/close': (r) => { record(r); state.status = 'Closed'; state.adjustment = 'Pending'; state.version++; return json(r, detail()) },
    'GET /api/v1/inventory/adjustments/a9': (r) => json(r, adjustment()),
    'POST /api/v1/inventory/adjustments/a9/approve': (r) => { record(r); state.adjustment = 'Approved'; return json(r, adjustment()) },
    'GET /api/v1/me': (r) => json(r, { userId: me === 'ana' ? 'u1' : me === 'bruno' ? 'u2' : 'u3', tenantId: 't1', name: names[me], email: `${me}@konebras.com.br` }),
  }
  return { api, calls, state }
}
