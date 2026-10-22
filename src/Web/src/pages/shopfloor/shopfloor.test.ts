import { describe, expect, it } from 'vitest'
import { laborMarks } from '@/pages/production/orders/labor'
import { filterQueue, finishImpact, floorDate, floorHours, formatTimer, loadMyCenter, missingLabel, MY_CENTER_KEY, orderedReasons, saveMyCenter, viewableKind, workedSeconds } from './shopfloor'

const step = (over: Record<string, unknown> = {}) => ({ stepId: 's', isMyCenter: false, stepName: 'Montagem', ...over }) as never

describe('app do operador (PRD-03a)', () => {
  it('Só meu centro filtra sem mudar a ordem do servidor (CA 2)', () => {
    const steps = [step({ stepId: 'a', isMyCenter: true }), step({ stepId: 'b' }), step({ stepId: 'c', isMyCenter: true })]
    expect(filterQueue(steps, true).map((s: { stepId: string }) => s.stepId)).toEqual(['a', 'c'])
    expect(filterQueue(steps, false)).toHaveLength(3)
  })

  it('o filtro fica no aparelho e o padrão é desligado (CA 2)', () => {
    const store = new Map<string, string>()
    const storage = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v) }
    expect(loadMyCenter(storage)).toBe(false)
    saveMyCenter(true, storage)
    expect(store.get(MY_CENTER_KEY)).toBe('1')
    expect(loadMyCenter(storage)).toBe(true)
    expect(loadMyCenter({ getItem: () => { throw new Error('bloqueado') } })).toBe(false)
    expect(() => saveMyCenter(true, { setItem: () => { throw new Error('cheio') } })).not.toThrow()
  })

  it('cronômetro desconta as pausas e usa o relógio do servidor (CA 7, 11, 12)', () => {
    const start = Date.parse('2026-10-22T14:28:00Z')
    const mine = { startedAt: '2026-10-22T14:28:00Z', closedPauseSeconds: 600, pauseStartedAt: null }
    expect(workedSeconds(mine, start + 3_600_000)).toBe(3000)
    expect(workedSeconds({ ...mine, pauseStartedAt: '2026-10-22T15:18:00Z' }, start + 3_600_000)).toBe(2400, 'a pausa aberta para o cronômetro')
    expect(workedSeconds(mine, start + 3_600_000 - 5000, 5000)).toBe(3000, 'diferença do relógio do aparelho')
    expect(formatTimer(2 * 3600 + 14 * 60 + 36)).toBe('02:14:36')
    expect(formatTimer(-3)).toBe('00:00:00')
    expect(floorHours(2.25)).toBe('2,3')
  })

  it('motivo mais usado primeiro e textos da finalização (CA 11, 13, 14)', () => {
    expect(orderedReasons('Break').map((r) => r.value)).toEqual(['Break', 'Material', 'Machine', 'EndOfShift', 'Other'])
    expect(missingLabel(1)).toBe('Marque o item que falta')
    expect(missingLabel(3)).toBe('Marque os 3 itens que faltam')
    const detail = { step: { stepName: 'Montagem' }, requiresApproval: false, nextStepName: 'Qualidade' } as never
    expect(finishImpact(detail)).toEqual(['Montagem: Em andamento → Concluída', 'Próxima: Qualidade fica pronta'])
    expect(finishImpact({ step: { stepName: 'Montagem' }, requiresApproval: true, nextStepName: null } as never, ['Beto'])[0]).toBe('Montagem: Em andamento → Aguardando aprovação')
    expect(finishImpact(detail, ['Beto', 'Caio']).at(-1)).toBe('Encerra também: Beto, Caio')
  })

  it('data do cabeçalho e anexos que abrem na tela (CA 1, 6)', () => {
    expect(floorDate('2026-10-16')).toBe('sexta 16/10')
    expect(viewableKind('application/pdf')).toBe('pdf')
    expect(viewableKind('image/jpeg')).toBe('image')
    expect(viewableKind('application/acad')).toBeNull()
  })

  it('marcas do apontamento na aba Apontamentos (CA 31)', () => {
    const base = { onBehalf: false, recordedByName: 'Ana', closeKind: null, closedByName: null, correctedAt: null, selfApproved: false, endedAt: '2026-10-22T15:00:00Z' }
    expect(laborMarks({ ...base, onBehalf: true, closeKind: 'Supervisor' })).toEqual(['Apontado por Ana'])
    expect(laborMarks({ ...base, closeKind: 'StepCompleted', closedByName: 'Beto' })).toEqual(['Encerrado por Beto'])
    expect(laborMarks({ ...base, closeKind: 'Supervisor', correctedAt: '2026-10-22T16:00:00Z', selfApproved: true })).toEqual(['Encerrado pelo supervisor', 'Corrigido', 'Autoaprovado'])
    expect(laborMarks({ ...base, endedAt: null })).toEqual(['Em aberto'])
  })
})