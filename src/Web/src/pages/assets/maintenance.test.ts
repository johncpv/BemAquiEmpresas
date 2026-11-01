import { describe, expect, it } from 'vitest'
import { emptyPlanDraft, formatMeter, frequencyText, missingText, nextDueText, parseNumber, planChip, planErrors, planInput } from './maintenance'

describe('manutenção (PAT-03a)', () => {
  it('próximo vencimento: a data no plano por dias; horímetro e estimativa no plano por horas (CA 1 e 2)', () => {
    expect(nextDueText({ frequencyType: 'Days', nextDueAt: '2026-12-01', nextDueMeter: null })).toBe('01/12/2026')
    expect(nextDueText({ frequencyType: 'Hours', nextDueAt: '2026-11-16', nextDueMeter: 1250 })).toBe('1.250 h (previsto para 16/11/2026)')
    expect(nextDueText({ frequencyType: 'Hours', nextDueAt: null, nextDueMeter: 1250 })).toBe('1.250 h (registre o horímetro para prever a data)')
    expect(formatMeter(1234.5)).toBe('1.234,5 h')
    expect(frequencyText('Days', 30)).toBe('A cada 30 dias')
    expect(frequencyText('Hours', 1)).toBe('A cada 1 hora de uso')
  })

  it('chip do plano: Vencida crítico, A vencer alerta, Inativo neutro (CA 9)', () => {
    expect(planChip({ isActive: true, overdue: true, dueSoon: false })).toEqual({ tone: 'crit', label: 'Vencida' })
    expect(planChip({ isActive: true, overdue: false, dueSoon: true })).toEqual({ tone: 'warn', label: 'A vencer' })
    expect(planChip({ isActive: false, overdue: true, dueSoon: false })).toEqual({ tone: 'neutral', label: 'Inativo' })
    expect(planChip({ isActive: true, overdue: false, dueSoon: false })).toBeNull()
  })

  it('botão de concluir diz quantos itens faltam (CA 6)', () => {
    expect(missingText([{ result: 'Done' }, { result: null }, {}])).toEqual({ missing: 2, text: 'Marque os 2 itens que faltam' })
    expect(missingText([{ result: null }]).text).toBe('Marque o item que falta')
    expect(missingText([{ result: 'NotApplicable' }])).toEqual({ missing: 0, text: null })
  })

  it('plano: antecedência padrão 7, data da última execução no plano por dias e horímetro no por horas', () => {
    const draft = emptyPlanDraft('2026-11-01')
    expect(draft.leadDays).toBe('7')
    expect(planErrors({ ...draft, description: '' }).description).toBeDefined()
    expect(planErrors({ ...draft, description: 'Óleo', lastDoneAt: '' }).lastDoneAt).toBe('Informe a data da última execução.')
    expect(planErrors({ ...draft, description: 'Óleo', frequencyType: 'Hours', lastDoneMeter: '' }).lastDoneMeter).toBe('Informe o horímetro da última execução.')
    expect(planErrors({ ...draft, description: 'Óleo' })).toEqual({})
    const body = planInput({ ...draft, description: ' Óleo ', checklist: ['Trocar', ' ', 'Limpar'], frequencyType: 'Hours', lastDoneMeter: '1.000,5' })
    expect(body).toMatchObject({ description: 'Óleo', frequencyValue: 30, leadDays: 7, checklist: ['Trocar', 'Limpar'], lastDoneMeter: 1000.5, isActive: undefined })
    expect(parseNumber('1234.5')).toBe(1234.5)
    expect(parseNumber('abc')).toBeNull()
  })
})
