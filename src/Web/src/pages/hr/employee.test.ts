import { describe, expect, it } from 'vitest'
import { emptyEmployeeDraft, emptyLeaveDraft, employeeDraftErrors, leaveDraftError, returnDate, STATUS_OPTIONS, withPosition } from './employee'

describe('sugestão pelo cargo (CAD-04b CA 1)', () => {
  const montador = { id: 'p1', workScheduleId: 's44', referenceSalary: 3300 }
  const lider = { id: 'p2', workScheduleId: 's40', referenceSalary: 4500 }

  it('preenche jornada e salário do cargo enquanto não foram editados', () => {
    const d = withPosition(emptyEmployeeDraft('2026-10-19'), montador)
    expect(d.workScheduleId).toBe('s44')
    expect(d.baseSalary).toBe('3300')
    const again = withPosition(d, lider)
    expect(again.workScheduleId).toBe('s40')
    expect(again.baseSalary).toBe('4500')
  })

  it('não troca o que o usuário já mexeu', () => {
    const d = { ...withPosition(emptyEmployeeDraft('2026-10-19'), montador), baseSalary: '3600', touched: { salary: true, schedule: false } }
    const next = withPosition(d, lider)
    expect(next.baseSalary).toBe('3600')
    expect(next.workScheduleId).toBe('s40')
  })

  it('sem salário de referência no cargo, o salário fica vazio e é obrigatório', () => {
    const d = withPosition(emptyEmployeeDraft('2026-10-19'), { id: 'p3', workScheduleId: null, referenceSalary: null })
    expect(d.baseSalary).toBeNull()
    expect(employeeDraftErrors(d).baseSalary).toBe('Informe o salário base.')
  })
})

describe('afastamento (CAD-04c1)', () => {
  it('o diálogo vem com o início em hoje e exige tipo e fim previsto', () => {
    const d = emptyLeaveDraft('2026-10-22')
    expect(d.startDate).toBe('2026-10-22')
    expect(leaveDraftError(d)).toBe('Escolha o tipo do afastamento.')
    expect(leaveDraftError({ ...d, type: 'Illness' })).toBe('Informe o fim previsto do afastamento.')
    expect(leaveDraftError({ ...d, type: 'Illness', expectedEndDate: '2026-10-21' })).toBe('O fim previsto precisa ser igual ou depois do início.')
    expect(leaveDraftError({ ...d, type: 'Illness', expectedEndDate: '2026-10-22' })).toBeUndefined()
    expect(leaveDraftError({ ...d, type: 'Other', expectedEndDate: '2026-10-30', reason: 'x'.repeat(201) })).toBe('O motivo aceita até 200 caracteres.')
  })

  it('a volta prevista é o dia seguinte ao fim, inclusive na virada do mês', () => {
    expect(returnDate('2026-10-31')).toBe('2026-11-01')
    expect(returnDate('2026-12-31')).toBe('2027-01-01')
  })

  it('o filtro de situação oferece Afastado', () => {
    expect(STATUS_OPTIONS.map((o) => o.label)).toEqual(['Ativo', 'Afastado', 'Desligado'])
  })
})