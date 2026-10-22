import { describe, expect, it } from 'vitest'
import { canCancel, canChangeSteps, canShowComplete, completionImpact, delayLabel, reversalImpact, routingImpact } from './fabrication'

describe('OS de fabricação (PRD-01)', () => {
  it('etapas só mudam na OS aberta sem etapa iniciada (CA 7 e 8)', () => {
    expect(canChangeSteps({ status: 'Open', started: false })).toBe(true)
    expect(canChangeSteps({ status: 'Open', started: true })).toBe(false)
    expect(canChangeSteps({ status: 'Cancelled', started: false })).toBe(false)
  })

  it('cancelar a iniciada exige quem aprova (CA 16 e 17)', () => {
    expect(canCancel({ status: 'Open', started: false }, true, false)).toBe(true)
    expect(canCancel({ status: 'Open', started: true }, true, false)).toBe(false)
    expect(canCancel({ status: 'InProgress', started: true }, false, true)).toBe(true)
    expect(canCancel({ status: 'Cancelled', started: false }, true, true)).toBe(false)
  })

  it('faixa com texto e impacto da troca de roteiro (padrões 2 e 4)', () => {
    expect(delayLabel(3)).toBe('Atrasada 3 d')
    expect(delayLabel(null)).toBeNull()
    expect(routingImpact({ steps: 3, hours: 5.5 }, { steps: 5, hours: 12 })).toBe('3 → 5 etapas · 5,50 h → 12,00 h padrão')
  })
})

describe('Conclusão da OS (PRD-06a1)', () => {
  const milestone = { description: 'Fabricação concluída', amount: 40000, dueDate: '2026-11-01', released: false, received: false }
  const base = { orderNumber: 'OS-2026-00041', lastFabricationOrder: true, otherOpenOrders: 0, projectNumber: 'PRJ-2026-0007', projectStatus: 'InProgress', showAmounts: true, milestones: [milestone] }
  const money = (v: number) => `R$ ${v}`
  const date = (iso: string) => iso.split('-').reverse().join('/')

  it('a última OS fabrica o projeto e libera a parcela, com valor só para o financeiro (CA 2)', () => {
    expect(completionImpact(base, money, date)).toEqual([
      'A OS-2026-00041 passa a Concluída.', 'O projeto PRJ-2026-0007 passa a Fabricado.', 'Libera a parcela Fabricação concluída de R$ 40000, vencendo em 01/11/2026.',
    ])
    expect(completionImpact({ ...base, showAmounts: false, milestones: [{ ...milestone, amount: null, dueDate: null }] }, money, date)[2]).toBe('Libera a parcela do marco Fabricação concluída.')
    expect(completionImpact({ ...base, lastFabricationOrder: false, otherOpenOrders: 2 }, money, date)[1]).toBe('Faltam 2 OS de fabricação para o projeto PRJ-2026-0007 passar a Fabricado.')
    expect(completionImpact({ ...base, projectStatus: 'Fabricated' }, money, date)).toHaveLength(2)
  })

  it('o estorno volta o projeto fabricado pelas OS e a parcela fica (CA 10)', () => {
    const lines = reversalImpact({ orderNumber: 'OS-2026-00041', projectNumber: 'PRJ-2026-0007', projectStatus: 'Fabricated', projectFabricatedByOrders: true, milestones: [{ ...milestone, released: true }] })
    expect(lines).toEqual([
      'A OS-2026-00041 volta a Em andamento, na última etapa.', 'O projeto PRJ-2026-0007 volta de Fabricado a Em andamento.',
      'A parcela Fabricação concluída já liberada continua em aberto. O Financeiro é avisado.',
    ])
    expect(reversalImpact({ orderNumber: 'OS-1', projectNumber: 'P', projectStatus: 'Fabricated', projectFabricatedByOrders: false, milestones: [] })).toHaveLength(1)
  })

  it('"Concluir OS" só para quem aprova, na OS com etapas e não concluída (CA 4 e 13)', () => {
    expect(canShowComplete({ status: 'ReadyToComplete', steps: [{}] }, true)).toBe(true)
    expect(canShowComplete({ status: 'ReadyToComplete', steps: [{}] }, false)).toBe(false)
    expect(canShowComplete({ status: 'Completed', steps: [{}] }, true)).toBe(false)
    expect(canShowComplete({ status: 'Open', steps: [] }, true)).toBe(false)
  })
})