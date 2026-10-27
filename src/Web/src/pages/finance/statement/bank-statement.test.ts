import { describe, expect, it } from 'vitest'
import { contingencyMax, dayMonth, importSummary, isBusy, LINE_STATUS, reconcileLabel } from './bank-statement'

describe('extrato do banco (BNK-03a)', () => {
  it('resume a importação com novas, já importadas e recusadas', () => {
    expect(importSummary({ newCount: 3, duplicateCount: 1, rejectedCount: 0 })).toBe('3 novas, 1 já importada, 0 recusadas')
    expect(importSummary({ newCount: 1, duplicateCount: 0, rejectedCount: 2 })).toBe('1 nova, 0 já importadas, 2 recusadas')
  })

  it('oferece conciliar só quando há pendentes', () => {
    expect(reconcileLabel(0)).toBeNull()
    expect(reconcileLabel(1)).toBe('Conciliar 1 linha pendente')
    expect(reconcileLabel(12)).toBe('Conciliar 12 linhas pendentes')
  })

  it('situação com cor semântica e rótulo', () => {
    expect(LINE_STATUS.Pending).toEqual({ label: 'Pendente', tone: 'info' })
    expect(LINE_STATUS.Reconciled.tone).toBe('ok')
    expect(LINE_STATUS.Ignored.tone).toBe('neutral')
  })

  it('datas e prazo da contingência sem mudar o dia', () => {
    expect(dayMonth('2026-10-05')).toBe('05/10')
    expect(contingencyMax('2026-10-27')).toBe('2026-11-26')
    expect(isBusy('slow')).toBe(true)
    expect(isBusy('refused')).toBe(false)
  })
})
