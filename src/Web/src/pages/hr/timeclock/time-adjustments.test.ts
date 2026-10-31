import { describe, expect, it } from 'vitest'
import type { TimeAdjustmentView } from '@/api/generated/model'
import { absenceReasons, durationText, hoursToMinutes, isStatus, justificationError, lastWorkday, selectable, shortDay, STATUS_CHIP, workedText } from './time-adjustments'

describe('ajustes do ponto (RH-01b)', () => {
  it('justificativa de 10 a 200 caracteres, sem CID', () => {
    expect(justificationError('curta')).toContain('Não informe CID nem diagnóstico.')
    expect(justificationError('Esqueci de marcar a saída')).toBeNull()
    expect(justificationError('x'.repeat(201))).not.toBeNull()
  })

  it('horas do abono e do dia', () => {
    expect(hoursToMinutes('04:00')).toBe(240)
    expect(hoursToMinutes('1:30')).toBe(90)
    expect(hoursToMinutes('25:00')).toBeNull()
    expect(hoursToMinutes('00:00')).toBeNull()
    expect(durationText(null)).toBe('o dia inteiro')
    expect(durationText(90)).toBe('1 h 30 min')
    expect(workedText(590)).toBe('9 h 50 min')
    expect(workedText(0)).toBe('0 h')
  })

  it('chips de situação: aguardando info, aprovado ok, reprovado crítico, cancelado neutro', () => {
    expect(STATUS_CHIP.Pending.tone).toBe('info')
    expect(STATUS_CHIP.Approved.tone).toBe('ok')
    expect(STATUS_CHIP.Rejected.tone).toBe('crit')
    expect(STATUS_CHIP.Cancelled.tone).toBe('neutral')
    expect(isStatus('Pending')).toBe(true)
    expect(isStatus('todos')).toBe(false)
  })

  it('só os pendentes que a pessoa pode decidir entram no "Aprovar selecionados"', () => {
    const items = [
      { id: 'a', status: 'Pending', canDecide: true },
      { id: 'b', status: 'Pending', canDecide: false },
      { id: 'c', status: 'Approved', canDecide: false },
    ] as TimeAdjustmentView[]
    expect(selectable(items).map((a) => a.id)).toEqual(['a'])
  })

  it('dia útil anterior e data curta', () => {
    expect(lastWorkday('2026-10-27')).toBe('2026-10-26')
    expect(lastWorkday('2026-10-26')).toBe('2026-10-23')
    expect(shortDay('2026-10-21')).toBe('21/10/2026')
  })

  it('motivos do abono: só os que valem para abono e os de saúde só com a permissão', () => {
    const all = [
      { code: 'MedicalCertificate', label: 'Atestado médico', appliesTo: 'Both', isHealth: true, justifies: true },
      { code: 'Maternity', label: 'Maternidade', appliesTo: 'Leave', isHealth: false, justifies: true },
      { code: 'AttendanceNote', label: 'Declaração de comparecimento', appliesTo: 'Absence', isHealth: false, justifies: true },
    ] as const
    expect(absenceReasons(all, true).map((r) => r.code)).toEqual(['MedicalCertificate', 'AttendanceNote'])
    expect(absenceReasons(all, false).map((r) => r.code)).toEqual(['AttendanceNote'])
  })
})
