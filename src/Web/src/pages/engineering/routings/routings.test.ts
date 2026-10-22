import { describe, expect, it } from 'vitest'
import { draftProblems, exampleDraft, newStep, normalizeLetter, renameStep, suggestCenter, suggestLetter, toStepsInput, totalHours } from './routings'

const centers = [{ id: 'c1', name: 'Corte a laser' }, { id: 'c2', name: 'Montagem' }]

describe('roteiro (ENG-01)', () => {
  it('letra sugerida pela inicial sem acento e só de A a Z (CA 3, D10)', () => {
    expect(suggestLetter('Expedição')).toBe('E')
    expect(suggestLetter('ótima')).toBe('O')
    expect(suggestLetter('3D')).toBe('D')
    expect(suggestLetter('123')).toBe('')
    expect(normalizeLetter('Cx')).toBe('X')
    expect(normalizeLetter('ç')).toBe('C')
  })

  it('a letra acompanha o nome até o usuário editá-la', () => {
    const step = newStep()
    expect(renameStep(step, 'Pintura').code).toBe('P')
    expect(renameStep({ ...step, code: 'Z', codeTouched: true }, 'Pintura').code).toBe('Z')
  })

  it('novo roteiro vem com P C M Q E, a qualidade marcada e o checklist da Expedição (P-04, D4)', () => {
    const draft = exampleDraft([{ stepName: 'corte', workCenterId: 'c1' }], centers)
    expect(draft.steps.map((s) => s.code)).toEqual(['P', 'C', 'M', 'Q', 'E'])
    expect(draft.steps.filter((s) => s.isQualityStep).map((s) => s.name)).toEqual(['Qualidade'])
    expect(draft.steps[4].checklist).toEqual(['Preparar e embalar para o romaneio'])
    expect(draft.steps[1]).toMatchObject({ workCenterId: 'c1', suggested: true })
    expect(draft.steps[0]).toMatchObject({ workCenterId: null, suggested: false })
  })

  it('centro sugerido pelo último usado no mesmo nome; sem histórico, o único centro', () => {
    expect(suggestCenter(' Montagem ', [{ stepName: 'montagem', workCenterId: 'c2' }], centers)?.id).toBe('c2')
    expect(suggestCenter('Solda', [], centers)).toBeNull()
    expect(suggestCenter('Solda', [], [centers[0]])?.id).toBe('c1')
  })

  it('as mesmas regras do servidor antes de enviar, no caminho da linha (CA 5)', () => {
    const steps = [{ ...newStep('Qualidade'), isQualityStep: true, workCenterId: 'c1' }, { ...newStep('Quadro'), workCenterId: 'c1' }, { ...newStep(''), code: '' }]
    const problems = draftProblems({ name: '', productType: '', steps })
    expect(problems.name).toBeDefined()
    expect(problems['steps[0].isQualityStep']).toBe('A primeira etapa não pode ser de qualidade.')
    expect(problems['steps[1].code']).toBe('A letra Q já está na etapa 1.')
    expect(problems['steps[2].code']).toBe('Informe a letra da etapa, de A a Z.')
    expect(problems['steps[2].workCenterId']).toBe('Escolha o centro de trabalho da etapa.')
    expect(draftProblems({ name: 'X', productType: '', steps: [] }).steps).toBe('Inclua ao menos uma etapa.')
  })

  it('total de horas ao vivo e corpo do salvar sem linhas vazias no checklist', () => {
    const steps = [{ ...newStep('Corte'), standardHours: '1.50' }, { ...newStep('Montagem'), standardHours: '2.25', checklist: [' Fixar guias ', ''] }]
    expect(totalHours(steps)).toBe('3,75')
    expect(toStepsInput(steps)[1]).toMatchObject({ standardHours: 2.25, checklist: ['Fixar guias'], code: 'M' })
  })
})