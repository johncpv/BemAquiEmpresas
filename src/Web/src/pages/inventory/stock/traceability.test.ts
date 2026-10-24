import { describe, expect, it } from 'vitest'
import type { LotTrace, SerialTrace } from '@/api/generated/model'
import { correctionProblem, daysUntil, entryLink, expiryChip, lotTree, reconciliationText, serialTree, TRACE_TEXT } from './traceability'

const serial: SerialTrace = {
  serialId: 's1', serialNumber: 'ELV-0001-2026-00001', status: 'Shipped', itemId: 'p1', itemCode: 'ELV-0001', itemName: 'Elevador', locationId: null, locationName: null, createdAt: '2026-10-20T12:00:00Z',
  productionOrderId: 'o1', productionOrderNumber: 'OS-2026-0001', productionOrderStatus: 'Completed', completedAt: '2026-10-20T12:00:00Z', orderUnits: 2, projectId: 'pr1', projectNumber: 'PRJ-2026-0001',
  projectName: 'Obra Centro', customer: { partyId: null, name: 'J. M. R.', kind: 'PF', masked: true }, shipmentId: 'r1', shipmentNumber: 'ROM-2026-0001', shippedAt: '2026-10-22T12:00:00Z',
  sharedLotsNote: 'Lotes da OS OS-2026-0001 (2 unidades): a OS não separa o material por unidade.',
  lots: [{
    itemId: 'm1', itemCode: 'CABO', itemName: 'Cabo', unitCode: 'M', lotId: 'l1', lotNumber: 'L-01', quantity: 12, supplierName: 'Aços Minas', expiry: '2026-11-03', expiryState: 'Expiring',
    entries: [{ movementId: 'mv1', sourceType: 'GoodsReceipt', sourceId: 'rc1', number: 'REC-2026-0001', label: 'Recebimento REC-2026-0001', at: '2026-10-01T12:00:00Z', quantity: 50, reversed: false }],
  }],
}

const lot: LotTrace = {
  lotId: 'l1', lotNumber: 'L-01', itemId: 'm1', itemCode: 'CABO', itemName: 'Cabo', unitCode: 'M', expiry: null, expiryState: 'None', daysToExpiry: null, supplierPartyId: null, supplierName: null,
  receivedAt: null, entries: [], totalReceived: 50, balances: [{ locationId: 'a', locationName: 'Almoxarifado', onHand: 28 }], onHand: 28, totalConsumed: 20, totalOther: -2, balanced: true,
  consumptions: [
    { productionOrderId: 'o1', orderNumber: 'OS-2026-0001', orderStatus: 'Completed', projectId: 'pr1', projectNumber: 'PRJ-2026-0001', projectName: 'Obra', customer: { partyId: 'c1', name: 'Horizonte Ltda', kind: 'PJ', masked: false }, at: '2026-10-10T12:00:00Z', quantity: 12, serials: [{ id: 's1', serialNumber: 'ELV-0001-2026-00001', status: 'Shipped', shipmentId: 'r1', shipmentNumber: 'ROM-2026-0001', shippedAt: '2026-10-22T12:00:00Z' }], noSerialNote: null, sharedLotsNote: null },
    { productionOrderId: 'o2', orderNumber: 'OS-2025-0009', orderStatus: 'Completed', projectId: null, projectNumber: null, projectName: null, customer: null, at: '2026-09-10T12:00:00Z', quantity: 8, serials: [], noSerialNote: 'Sem série: OS concluída antes do registro de séries.', sharedLotsNote: null },
  ],
  others: [{ movementId: 'mv9', number: 'MOV-2026-000009', type: 'Issue', sourceType: 'ManualPosting', sourceId: 'x', sourceNumber: null, at: '2026-10-11T12:00:00Z', quantity: -2, explanation: 'Saída avulsa MOV-2026-000009' }],
}

describe('rastreabilidade (EST-04)', () => {
  it('chip da validade: alerta até 30 dias e crítico vencido (CA 5)', () => {
    expect(expiryChip('Expired', -1)).toEqual({ label: 'Vencido', tone: 'crit' })
    expect(expiryChip('Expiring', 12)).toEqual({ label: 'Vence em 12 dias', tone: 'warn' })
    expect(expiryChip('Expiring', 1)?.label).toBe('Vence em 1 dia')
    expect(expiryChip('Expiring', 0)?.label).toBe('Vence hoje')
    expect(expiryChip('Ok', 90)).toBeNull()
    expect(daysUntil('2026-11-03', '2026-10-24')).toBe(10)
  })

  it('série → origem: OS, projeto, cliente com iniciais, romaneio e lotes com o recebimento (CA 1, 2 e 9)', () => {
    const [root] = serialTree(serial, '2026-10-24')
    expect(root.label).toBe('Série ELV-0001-2026-00001')
    expect(root.chip?.label).toBe('Expedido')
    const labels = root.children!.map((c) => c.label)
    expect(labels).toEqual(['OS OS-2026-0001', 'Projeto PRJ-2026-0001', 'Romaneio ROM-2026-0001', 'Lotes de matéria-prima consumidos'])
    const customer = root.children![1].children![0]
    expect(customer).toMatchObject({ label: 'Cliente J. M. R.', masked: true, dica: 'iniciais' })
    expect(customer.link).toBeUndefined()
    const lots = root.children![3]
    expect(lots).toMatchObject({ detail: serial.sharedLotsNote, dica: 'lotesDaOs' })
    expect(lots.children![0]).toMatchObject({ label: 'CABO Cabo · Lote L-01', quantity: '12 M', chip: { label: 'Vence em 10 dias', tone: 'warn' } })
    expect(lots.children![0].children![0]).toMatchObject({ label: 'Recebimento REC-2026-0001', link: { to: '/app/compras/recebimentos/$id', params: { id: 'rc1' } } })
  })

  it('lote → destino: consumo por OS com as séries e o romaneio, a OS antiga e a conta fechada (CA 3)', () => {
    const tree = lotTree(lot)
    expect(tree.map((n) => n.label)).toEqual(['Entradas', 'Saldo atual', 'Consumo nas OS', 'Outras saídas e ajustes'])
    const uses = tree[2].children!
    expect(uses[0].children![0]).toMatchObject({ label: 'Série ELV-0001-2026-00001', detail: expect.stringContaining('Romaneio ROM-2026-0001') })
    expect(uses[1].children![0].label).toBe('Sem série: OS concluída antes do registro de séries.')
    expect(tree[3].children![0].label).toBe('Saída avulsa MOV-2026-000009')
    expect(reconciliationText(lot)).toBe('Recebido 50 − consumido 20 + outras −2 = saldo 28 M')
  })

  it('link da entrada: recebimento pela rota de Compras, o resto pelo movimento', () => {
    expect(entryLink({ movementId: 'm', sourceType: 'ManualPosting', sourceId: 's', number: null, label: '', at: '', quantity: 1, reversed: false })).toEqual({ to: '/app/estoque/movimentos/$id', id: 'm' })
  })

  it('correção do lote: com saída só a validade, motivo obrigatório (CA 6)', () => {
    const base = { lotNumber: 'L-01', expiry: null, hasOutflow: false }
    expect(correctionProblem(base, 'L-01', '', 'Motivo')).toBe('Mude o número ou a validade.')
    expect(correctionProblem(base, 'L-02', '', '')).toBe('Escreva o motivo da correção.')
    expect(correctionProblem(base, 'L-02', '', 'Digitado errado')).toBeNull()
    expect(correctionProblem({ ...base, hasOutflow: true }, 'L-02', '', 'Digitado errado')).toBe('Lote com saída só tem a validade corrigida.')
    expect(correctionProblem({ ...base, hasOutflow: true }, 'L-01', '2027-01-31', 'Certificado')).toBeNull()
  })

  it('textos dos estados vazios', () => {
    expect(TRACE_TEXT.semResultado('X1')).toBe("Nenhuma série ou lote com 'X1'.")
  })
})
