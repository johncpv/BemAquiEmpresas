import { describe, expect, it } from 'vitest'
import type { WarrantyDefaultDetail } from '@/api/generated/model'
import { draftErrors, emptyDraft, toInput, withType } from './item-draft'

const warranties: WarrantyDefaultDetail[] = [
  { id: '1', itemType: 'Finished', months: 12 }, { id: '2', itemType: 'Component', months: 3 }, { id: '3', itemType: 'RawMaterial', months: null },
]

describe('rascunho do item (CAD-02a CA 7, 9, 10 e 12)', () => {
  it('o tipo sugere lote, série e garantia enquanto o usuário não mexeu', () => {
    const raw = withType(emptyDraft(), 'RawMaterial', warranties)
    expect([raw.tracksLot, raw.tracksSerial, raw.warranty]).toEqual([true, false, ''])
    const finished = withType(raw, 'Finished', warranties)
    expect([finished.tracksLot, finished.tracksSerial, finished.warranty]).toEqual([false, true, '12'])
  })

  it('depois de mexer, a troca de tipo não sobrescreve', () => {
    const raw = withType(emptyDraft(), 'RawMaterial', warranties)
    const edited = { ...raw, tracksLot: false, warranty: '6', touched: { lot: true, serial: false, warranty: true } }
    const component = withType(edited, 'Component', warranties)
    expect([component.tracksLot, component.warranty]).toEqual([false, '6'])
  })

  it('NCM obrigatório só para item ativo dos tipos de produção', () => {
    const base = { ...emptyDraft(), code: 'X', name: 'Y', unitId: 'u' }
    expect(draftErrors({ ...base, type: 'RawMaterial' }, true).ncm).toBe('Preencha o NCM (8 dígitos) para o item ativo.')
    expect(draftErrors({ ...base, type: 'RawMaterial' }, false).ncm).toBeUndefined()
    expect(draftErrors({ ...base, type: 'Consumable' }, true).ncm).toBeUndefined()
  })

  it('serviço vai sem lote, série e mínimo', () => {
    const service = { ...emptyDraft(), type: 'Service' as const, code: 'S1', name: 'Instalação', unitId: 'u', tracksLot: true, minStock: '5' }
    expect(toInput(service)).toMatchObject({ tracksLot: false, tracksSerial: false, minStock: 0, warrantyMonths: null })
  })
})
