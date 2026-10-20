import { describe, expect, it } from 'vitest'
import { emptyEmployeeDraft, employeeDraftErrors, withPosition } from './employee'

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
