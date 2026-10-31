import { describe, expect, it } from 'vitest'
import {
  emptyEmployeeDraft, emptyLeaveDraft, employeeDraftErrors, isHealthLeave, leaveActions, leaveCancelError, leaveDraftError, leaveDraftFrom, leaveReturnError, leaveTitle, leaveTypeOptions,
  previousDay, returnDate, STATUS_OPTIONS, withPosition,
} from './employee'

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
    expect(leaveDraftError({ ...d, type: 'MedicalCertificate' })).toBe('Informe o fim previsto do afastamento.')
    expect(leaveDraftError({ ...d, type: 'MedicalCertificate', expectedEndDate: '2026-10-21' })).toBe('O fim previsto precisa ser igual ou depois do início.')
    expect(leaveDraftError({ ...d, type: 'MedicalCertificate', expectedEndDate: '2026-10-22' })).toBeUndefined()
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

describe('editar, encerrar e cancelar afastamento (CAD-04c1b)', () => {
  const leave = { startDate: '2026-10-10', expectedEndDate: '2026-10-20' }

  it('Atestado médico e Acidente de trabalho só aparecem com hr.view_health (lista única, RH-01b)', () => {
    expect(isHealthLeave('MedicalCertificate')).toBe(true)
    expect(isHealthLeave('WorkAccident')).toBe(true)
    expect(isHealthLeave('Maternity')).toBe(false)
    expect(leaveTypeOptions(true).map((o) => o.label)).toEqual(['Atestado médico', 'Acidente de trabalho', 'Maternidade', 'Paternidade', 'Serviço militar', 'Falta justificada'])
    expect(leaveTypeOptions(false).map((o) => o.label)).toEqual(['Maternidade', 'Paternidade', 'Serviço militar', 'Falta justificada'])
  })

  it('cada estado oferece só o que vale; o de saúde protegido não se edita sem a permissão', () => {
    expect(leaveActions({ state: 'Scheduled', healthProtected: false }, false)).toEqual({ edit: true, end: false, cancel: true })
    expect(leaveActions({ state: 'Current', healthProtected: false }, false)).toEqual({ edit: true, end: true, cancel: false })
    expect(leaveActions({ state: 'Current', healthProtected: true }, false)).toEqual({ edit: false, end: true, cancel: false })
    expect(leaveActions({ state: 'Ended', healthProtected: false }, true)).toEqual({ edit: false, end: false, cancel: false })
    expect(leaveActions({ state: 'Cancelled', healthProtected: false }, true)).toEqual({ edit: false, end: false, cancel: false })
  })

  it('sem hr.view_health, todo afastamento aparece só como Afastado (PO 22/10/2026)', () => {
    expect(leaveTitle({ type: 'Maternity', healthProtected: false })).toBe('Afastamento: Maternidade')
    expect(leaveTitle({ type: null, healthProtected: true })).toBe('Afastado')
    expect(leaveActions({ state: 'Scheduled', healthProtected: true }, false)).toEqual({ edit: false, end: false, cancel: true })
  })

  it('a edição vem com o gravado', () => {
    expect(leaveDraftFrom({ type: 'Military', startDate: '2026-11-01', expectedEndDate: '2026-11-30', reason: null }))
      .toEqual({ type: 'Military', startDate: '2026-11-01', expectedEndDate: '2026-11-30', reason: '' })
  })

  it('a volta fica depois do início, até o fim previsto e até hoje', () => {
    expect(leaveReturnError(leave, '2026-10-15', '2026-10-15')).toBeUndefined()
    expect(leaveReturnError(leave, '', '2026-10-15')).toBe('Informe a data da volta.')
    expect(leaveReturnError(leave, '2026-10-10', '2026-10-15')).toBe('A volta precisa ser depois do início do afastamento.')
    expect(leaveReturnError(leave, '2026-10-21', '2026-10-25')).toContain('até o fim previsto')
    expect(leaveReturnError(leave, '2026-10-16', '2026-10-15')).toBe('A volta não pode ser depois de hoje.')
    expect(previousDay('2026-11-01')).toBe('2026-10-31')
  })

  it('cancelar exige motivo de até 200 caracteres', () => {
    expect(leaveCancelError(' ')).toBe('Informe o motivo do cancelamento.')
    expect(leaveCancelError('x'.repeat(201))).toBe('O motivo aceita até 200 caracteres.')
    expect(leaveCancelError('Lançado em duplicidade')).toBeUndefined()
  })
})