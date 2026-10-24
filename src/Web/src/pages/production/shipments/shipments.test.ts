import { describe, expect, it } from 'vitest'
import type { ReadyToShipLine, ShipmentDetail, ShipmentImpact } from '@/api/generated/model'
import { choicesToLines, defaultChoices, formProblems, impactLines, SHIPMENT_STATUS, shipmentActions, shippedNext } from './shipments'

const line = (over: Partial<ReadyToShipLine> = {}): ReadyToShipLine => ({
  itemId: 'i1', itemCode: 'ELV-0001', itemName: 'Elevador', unitCode: 'UN', locationId: 'l1', locationName: 'Almoxarifado', lotId: null, lotNumber: null, tracksSerial: true,
  quantity: 2, onHand: 2, reserved: 0, available: 2, shippable: 2, inOpenShipments: 0, fromGeneralStock: false,
  serials: [
    { id: 's1', serialNumber: 'ELV-0001-2026-00001', productionOrderId: 'os1', openShipmentNumber: null },
    { id: 's2', serialNumber: 'ELV-0001-2026-00002', productionOrderId: 'os2', openShipmentNumber: null },
  ],
  ...over,
})

describe('romaneio (PRD-06b)', () => {
  it('vem com todas as séries livres do projeto e, vindo da OS, só as da OS (CA 1 e 9)', () => {
    expect(defaultChoices([line()])[0].serialIds).toEqual(['s1', 's2'])
    expect(defaultChoices([line()], 'os2')[0].serialIds).toEqual(['s2'])
    const held = line({ serials: [{ id: 's1', serialNumber: 'A', productionOrderId: null, openShipmentNumber: 'ROM-2026-0001' }] })
    expect(defaultChoices([held])[0].serialIds).toEqual([])
  })

  it('sem série, a quantidade é o que dá para expedir menos o que já está em romaneio aberto', () => {
    const plain = line({ tracksSerial: false, serials: [], quantity: 5, shippable: 5, inOpenShipments: 2 })
    expect(defaultChoices([plain])[0].quantity).toBe('3')
  })

  it('monta as linhas só com série marcada ou quantidade (CA 1)', () => {
    const [serial] = defaultChoices([line()])
    const plain = { ...defaultChoices([line({ itemId: 'i2', tracksSerial: false, serials: [] })])[0], quantity: '1,5' }
    const empty = { ...serial, key: 'x', serialIds: [] }
    expect(choicesToLines([serial, plain, empty])).toEqual([
      { itemId: 'i1', locationId: 'l1', lotId: null, quantity: 2, serialIds: ['s1', 's2'], fromGeneralStock: false },
      { itemId: 'i2', locationId: 'l1', lotId: null, quantity: 1.5, serialIds: null, fromGeneralStock: false },
    ])
  })

  it('diz o que falta para salvar', () => {
    const plain = { ...defaultChoices([line({ tracksSerial: false, serials: [] })])[0], quantity: '3' }
    expect(formProblems('', [])).toEqual(['Informe o endereço de entrega.', 'Escolha ao menos uma série ou quantidade.'])
    expect(formProblems('Rua A, 1', [plain])).toEqual(['ELV-0001: há 2 UN para expedir em Almoxarifado.'])
  })

  it('impacto por item com o físico antes e depois e as séries (CA 4 e 5)', () => {
    const impact: ShipmentImpact = {
      id: 'r1', number: 'ROM-2026-0001', status: 'Draft', requiresStockApproval: false, version: 1,
      lines: [{ lineId: 'x', itemCode: 'ELV-0001', itemName: 'Elevador', unitCode: 'UN', locationName: 'Almoxarifado', quantity: 2, onHandBefore: 2, onHandAfter: 0, serialNumbers: ['A', 'B'], fromGeneralStock: false, insufficient: true }],
    }
    expect(impactLines(impact)).toEqual(['Saída de 2 UN de ELV-0001 em Almoxarifado: 2 → 0 · séries A, B passam a Expedido · saldo insuficiente agora'])
  })

  it('situações com o tom do CA 13', () => {
    expect(SHIPMENT_STATUS.AwaitingStockApproval.tone).toBe('warn')
    expect(SHIPMENT_STATUS.Shipped.tone).toBe('ok')
    expect(SHIPMENT_STATUS.Cancelled.tone).toBe('neutral')
  })

  it('ações por situação e permissão (CA 4, 6, 10, 11 e 15)', () => {
    const all = { edit: true, approve: true, stock: true, create: true }
    const next = { canConfirm: true, confirmBlocker: null, canConfirmStock: false, confirmStockBlocker: null, installationOrderId: null, installationOrderNumber: null, projectDelivered: false, projectStatus: 'Fabricated' }
    expect(shipmentActions({ status: 'Draft', next }, all)).toEqual({ edit: true, confirm: true, confirmStock: false, cancel: true, reverse: false })
    expect(shipmentActions({ status: 'Draft', next }, { ...all, edit: false })).toMatchObject({ confirm: false, cancel: false })
    expect(shipmentActions({ status: 'AwaitingStockApproval', next }, all)).toMatchObject({ confirmStock: true, cancel: true })
    expect(shipmentActions({ status: 'Shipped', next }, all)).toMatchObject({ reverse: true, cancel: false })
    expect(shipmentActions({ status: 'Shipped', next }, { ...all, approve: false }).reverse).toBe(false)
  })

  it('próximos passos do expedido: OS de instalação ou registrar entrega (CA 8)', () => {
    const base = { status: 'Shipped' as const, next: { canConfirm: false, confirmBlocker: null, canConfirmStock: false, confirmStockBlocker: null, installationOrderId: null, installationOrderNumber: null, projectDelivered: false, projectStatus: 'Fabricated' } }
    expect(shippedNext(base as Pick<ShipmentDetail, 'next' | 'status'>, true)).toEqual(['entrega', 'projeto'])
    expect(shippedNext(base as Pick<ShipmentDetail, 'next' | 'status'>, false)).toEqual(['projeto'])
    expect(shippedNext({ ...base, next: { ...base.next, installationOrderId: 'os9' } } as Pick<ShipmentDetail, 'next' | 'status'>, true)).toEqual(['instalacao', 'projeto'])
    expect(shippedNext({ ...base, status: 'Draft' } as Pick<ShipmentDetail, 'next' | 'status'>, true)).toEqual([])
  })
})
