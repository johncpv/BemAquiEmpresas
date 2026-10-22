import { describe, expect, it } from 'vitest'
import { ageLabel, canApprove, decisionNext, defaultReturn, draftDescription, missingQuality, missingText, NC_STATUS, nonConforming, resultLabel } from './quality'

const item = (text: string, result: 'Done' | 'NotApplicable' | 'NonConforming' | null) => ({ id: text, text, result, markedAt: null, markedByName: null })

describe('qualidade (PRD-05)', () => {
  it('aprovar exige todos os itens marcados, sem "Não conforme" e sem bloqueio (CA 3 e 5)', () => {
    const list = [item('Nível', 'Done'), item('Ruído', 'NotApplicable')]
    expect(canApprove(list, false)).toBe(true)
    expect(canApprove(list, true)).toBe(false)
    expect(canApprove([...list, item('Porta', null)], false)).toBe(false)
    expect(canApprove([...list, item('Porta', 'NonConforming')], false)).toBe(false)
    expect(missingQuality([item('a', null), item('b', null), item('c', 'Done')])).toBe(2)
    expect(missingText(1)).toBe('Marque o item que falta.')
    expect(missingText(3)).toBe('Marque os 3 itens que faltam.')
  })

  it('itens "Não conforme" viram o rascunho da descrição (propositivo)', () => {
    const list = [item('Painel riscado', 'NonConforming'), item('Nível', 'Done'), item('Ruído no motor', 'NonConforming')]
    expect(nonConforming(list).map((c) => c.text)).toEqual(['Painel riscado', 'Ruído no motor'])
    expect(draftDescription(list)).toBe('Não conforme: Painel riscado; Ruído no motor.')
    expect(draftDescription([item('Nível', 'Done')])).toBe('')
  })

  it('etapa de retorno padrão é a sugerida (CA 6)', () => {
    const options = [{ stepId: 'm', code: 'M', name: 'Montagem', suggested: false, waiting: 0, impact: '' }, { stepId: 'c', code: 'C', name: 'Corte', suggested: true, waiting: 1, impact: '' }]
    expect(defaultReturn(options)).toBe('c')
    expect(defaultReturn([{ ...options[0] }])).toBe('m')
    expect(defaultReturn([])).toBeNull()
  })

  it('próximo passo: "Concluir OS" só para quem conclui a OS (CA 4)', () => {
    const base = { result: 'Approved' as const, readyToComplete: true, canCompleteOrder: true, nextStepName: null, returnToStepName: null }
    expect(decisionNext(base)).toEqual({ text: 'A OS está pronta para concluir.', complete: true, openNc: false })
    expect(decisionNext({ ...base, canCompleteOrder: false })).toEqual({ text: 'A OS está pronta para concluir. O supervisor conclui.', complete: false, openNc: false })
    expect(decisionNext({ ...base, readyToComplete: false, nextStepName: 'Embalagem' }).text).toBe('A OS vai para Embalagem.')
    expect(decisionNext({ ...base, result: 'Rejected', readyToComplete: false, returnToStepName: 'Montagem' })).toEqual({ text: 'A OS voltou para Montagem.', complete: false, openNc: true })
  })

  it('rótulos da situação da NC e do resultado (CA 11)', () => {
    expect(NC_STATUS.Open).toEqual({ label: 'Aberta', tone: 'crit' })
    expect(NC_STATUS.InAction.tone).toBe('warn')
    expect(NC_STATUS.Closed.tone).toBe('ok')
    expect(resultLabel('NonConforming')).toBe('Não conforme')
    expect(resultLabel(null)).toBe('Sem marca')
    expect(ageLabel(0)).toBe('Hoje')
    expect(ageLabel(1)).toBe('1 dia')
    expect(ageLabel(12)).toBe('12 dias')
  })
})