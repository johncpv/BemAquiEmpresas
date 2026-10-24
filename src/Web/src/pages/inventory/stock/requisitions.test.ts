import { describe, expect, it } from 'vitest'
import type { RequisitionDraft, RequisitionDraftLine } from '@/api/generated/model'
import { parseFloorQuantity, stepQuantity } from '@/pages/shopfloor/shopfloor'
import { excessOf, extraRow, impactOf, needsApproval, overValue, requestStateLabel, rowProblems, rowsFromDraft, rowsFromRequest, splitLots, splitRow, toInput, totalValue } from './requisitions'

const lots = [
  { lotId: 'A', lotNumber: 'L-A', expiry: '2026-11-01', onHand: 3, suggested: true },
  { lotId: 'B', lotNumber: 'L-B', expiry: '2026-12-01', onHand: 10, suggested: false },
]

const line = (over: Partial<RequisitionDraftLine> = {}): RequisitionDraftLine => ({
  reservationId: 'res-1', itemId: 'item-1', itemCode: 'MP-1', itemName: 'Chapa', unitCode: 'KG', tracksLot: false, locationId: 'loc-1', locationCode: 'ALM', locationName: 'Almoxarifado',
  reserved: 12, onHand: 20, available: 8, averageCost: 100, lots: [], picks: [], ...over,
})

const draft = (lines: RequisitionDraftLine[]): RequisitionDraft => ({
  productionOrderId: 'os-1', orderNumber: 'OS-2026-00001', projectId: 'p-1', projectNumber: 'PRJ-2026-0001', orderClosed: false, bomReleased: true, approvalLimit: 1000, lines,
})

describe('requisição de material (EST-03)', () => {
  it('vem com todo o reservado a baixar e o item com lote já dividido pela sugestão (CA 1)', () => {
    const rows = rowsFromDraft(draft([line(), line({ reservationId: 'res-2', itemId: 'item-2', itemCode: 'MP-2', tracksLot: true, reserved: 5, lots, picks: [
      { lotId: 'A', lotNumber: 'L-A', quantity: 3, suggested: true }, { lotId: 'B', lotNumber: 'L-B', quantity: 2, suggested: false },
    ] })]))
    expect(rows.map((r) => [r.itemCode, r.quantity, r.lotId, r.suggestedLot])).toEqual([['MP-1', '12', null, false], ['MP-2', '3', 'A', true], ['MP-2', '2', 'B', false]])
  })

  it('divide pelos lotes na ordem da sugestão e oferece outro lote ao dividir a linha', () => {
    expect(splitLots(lots, 5)).toEqual([{ lotId: 'A', quantity: 3, suggested: true }, { lotId: 'B', quantity: 2, suggested: false }])
    const [row] = rowsFromDraft(draft([line({ tracksLot: true, lots, picks: [{ lotId: 'A', lotNumber: 'L-A', quantity: 3, suggested: true }] })]))
    const other = splitRow(row)
    expect([other.lotId, other.quantity, other.reservationId]).toEqual(['B', null, 'res-1'])
  })

  it('pedido do operador vira as linhas da reserva com a quantidade pedida (CA 7)', () => {
    const rows = rowsFromRequest(draft([line()]), [{ itemId: 'item-1', quantity: 4 }, { itemId: 'item-1', quantity: 2 }, { itemId: 'fora', quantity: 1 }])
    expect(rows.map((r) => [r.itemId, r.quantity])).toEqual([['item-1', '6']])
  })

  it('excedente sobre a reserva, material extra e o valor acima da BOM (CA 3 e 4)', () => {
    const rows = rowsFromDraft(draft([line()]))
    rows[0].quantity = '14'
    const extra = extraRow({ itemId: 'x', itemCode: 'PF-1', itemName: 'Parafuso', unitCode: 'UN', tracksLot: false, averageCost: 2 }, { locationId: 'loc-1', locationCode: 'ALM', onHand: 4, available: 4, lots: [] })
    extra.quantity = '1'
    const all = [rows[0], extra]
    expect(excessOf(all)).toEqual([2, 1])
    expect(overValue(all)).toBe(202)
    expect(totalValue(all)).toBe(1402)
    expect(needsApproval(1000, 1000)).toBe(false)
    expect(needsApproval(1000.01, 1000)).toBe(true)
    expect(rowProblems(all)).toEqual(['Motivo do consumo acima da BOM para MP-1.', 'Motivo do consumo acima da BOM para PF-1.'])
    all[0].overReason = 'Perda no corte'
    all[1].overReason = 'Fixação'
    expect(rowProblems(all)).toEqual([])
    expect(toInput(all)).toEqual([
      { reservationId: 'res-1', itemId: 'item-1', locationId: 'loc-1', lotId: null, quantity: 14, overReason: 'Perda no corte' },
      { reservationId: null, itemId: 'x', locationId: 'loc-1', lotId: null, quantity: 1, overReason: 'Fixação' },
    ])
  })

  it('impacto: físico e disponível antes → depois; o excedente acima do disponível é pendência', () => {
    const rows = rowsFromDraft(draft([line()]))
    rows[0].quantity = '15'
    rows[0].overReason = 'x'
    expect(impactOf(rows)[0]).toMatchObject({ quantity: 15, onHand: 20, onHandAfter: 5, available: 8, availableAfter: 5, short: false })
    rows[0].quantity = '21'
    expect(rowProblems(rows)).toContain('Disponível suficiente de MP-1 em ALM (há 8 KG livre).')
  })

  it('o que o operador vê no /fab e a quantidade com − e +', () => {
    expect(requestStateLabel('Waiting').label).toBe('Pedido enviado ao almoxarifado')
    expect(requestStateLabel('Separated').label).toBe('Material separado')
    expect(requestStateLabel('Declined', 'Já separado').label).toBe('Recusado: Já separado')
    expect(parseFloorQuantity('2,5')).toBe(2.5)
    expect(parseFloorQuantity('-1')).toBe(0)
    expect(stepQuantity('', 1)).toBe('1')
    expect(stepQuantity('2,5', 1)).toBe('3,5')
    expect(stepQuantity('1', -1)).toBe('')
  })
})
