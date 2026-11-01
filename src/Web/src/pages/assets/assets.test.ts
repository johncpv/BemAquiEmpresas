import { describe, expect, it } from 'vitest'
import type { AssetDetail } from '@/api/generated/model'
import {
  applySuggestion, assetDraftErrors, assetInput, draftFromAsset, emptyAssetDraft, hasMaintenancePlan, movementChanged, movementChanges, movementDraft, placeText, receivableSearch,
} from './assets'

const asset = {
  id: 'a1', tag: 'PAT-00007', name: 'Torno CNC', type: 'Machine', status: 'Active', brand: 'Romi', model: 'GL 240', serialNumber: 'SN-1', acquisitionDate: '2025-03-10',
  incomingInvoiceId: null, incomingInvoice: null, invoiceRef: 'NF 12', supplierPartyId: 'p1', supplierName: 'Máquinas', siteId: 's1', siteName: 'Fábrica', responsibleEmployeeId: 'e1',
  responsibleName: 'Ana', responsibleTerminated: false, workCenterId: 'c1', workCenterName: 'Usinagem', notes: null, photoId: null, createdAt: '2026-10-31T12:00:00Z', version: 3,
  acquisitionValue: 185000.5,
} as AssetDetail

describe('bem do patrimônio (PAT-01a)', () => {
  it('a sugestão do centro só muda o local e o responsável que o usuário não mexeu (CA 3)', () => {
    const draft = { ...emptyAssetDraft(), siteId: 'velho', touched: { site: true, responsible: false } }
    const next = applySuggestion(draft, { siteId: 'novo', responsibleEmployeeId: 'e9' })
    expect(next.siteId).toBe('velho')
    expect(next.responsibleEmployeeId).toBe('e9')
    expect(applySuggestion(next, { siteId: null, responsibleEmployeeId: null }).responsibleEmployeeId).toBe('e9')
  })

  it('"Duplicar bem" copia tudo menos a série e a etiqueta', () => {
    const copy = draftFromAsset(asset, true)
    expect(copy.serialNumber).toBe('')
    expect(copy.name).toBe('Torno CNC')
    expect(copy.acquisitionValue).toBe('185000.5')
    expect(draftFromAsset(asset).serialNumber).toBe('SN-1')
  })

  it('sem assets.edit o valor de aquisição não vai no corpo e o texto da nota sai com a nota do sistema (CA 2 e 7)', () => {
    const draft = { ...draftFromAsset(asset), incomingInvoiceId: 'n1' }
    expect(assetInput(draft, false).acquisitionValue).toBeNull()
    expect(assetInput(draft, true, 3)).toMatchObject({ acquisitionValue: 185000.5, invoiceRef: null, version: 3, incomingInvoiceId: 'n1' })
  })

  it('nome e tipo são obrigatórios; plano de manutenção só para máquina e veículo', () => {
    expect(Object.keys(assetDraftErrors(emptyAssetDraft()))).toEqual(['name', 'type'])
    expect(hasMaintenancePlan('Vehicle')).toBe(true)
    expect(hasMaintenancePlan('It')).toBe(false)
  })
})

describe('movimentação e baixa do bem (PAT-02)', () => {
  it('o formulário vem com o lugar atual e a data de hoje; nada mudou não confirma (CA 1 e 2)', () => {
    const draft = movementDraft(asset, '2026-11-01')
    expect(draft).toEqual({ siteId: 's1', responsibleEmployeeId: 'e1', workCenterId: 'c1', movedOn: '2026-11-01', reason: '' })
    expect(movementChanged(draft, asset)).toBe(false)
    expect(movementChanged({ ...draft, responsibleEmployeeId: '' }, asset)).toBe(true)
    expect(movementDraft(asset, '2026-11-01', 'Responsável desligado').reason).toBe('Responsável desligado')
  })

  it('o cartão mostra só o que mudou, de → para (CA 3 e 4)', () => {
    expect(movementChanges({ siteName: 'Fábrica', responsibleName: 'Ana', workCenterName: 'Solda' }, { siteName: 'Escritório', responsibleName: 'Ana', workCenterName: null }))
      .toEqual(['Local: Fábrica → Escritório', 'Centro: Solda → —'])
    expect(placeText({ siteName: 'Fábrica', responsibleName: null, workCenterName: null })).toBe('Fábrica · Sem responsável · Sem centro')
  })

  it('a venda abre o título a receber já preenchido, sem gravar nada (CA 7)', () => {
    expect(receivableSearch({ partyId: 'p1', partyName: 'Metal Sul', amount: 48500.75, issueDate: '2026-11-01', description: 'Venda do bem PAT-00007 — Torno CNC' })).toEqual({
      cliente: 'p1', clienteNome: 'Metal Sul', valor: '48500.75', emissao: '2026-11-01', descricao: 'Venda do bem PAT-00007 — Torno CNC',
    })
    expect(receivableSearch({ partyId: null, amount: 10, issueDate: '2026-11-01', description: 'x' }).cliente).toBeUndefined()
  })
})
