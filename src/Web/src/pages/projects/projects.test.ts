import { describe, expect, it } from 'vitest'
import { installationDaysError } from '../settings/parameters/production-parameters'
import { delayDays, forecastFix, forecastReasonText, PROJECT_DICAS, riskChip, riskStripe, slackText } from './projects'

describe('projeto 360 (COM-05)', () => {
  it('atraso só enquanto não entregue', () => {
    expect(delayDays('2026-10-10', 'Planned', '2026-10-20')).toBe(10)
    expect(delayDays('2026-10-10', 'Delivered', '2026-10-20')).toBeNull()
    expect(delayDays('2026-10-25', 'Planned', '2026-10-20')).toBeNull()
    expect(delayDays(null, 'Planned', '2026-10-20')).toBeNull()
  })

  it('dicas das três colunas (PRD-04 CA 9)', () => {
    expect(PROJECT_DICAS.comprometido).toBe('Já contratado, mas ainda não gasto: título previsto ou material comprado que não foi baixado para a OS.')
    expect(PROJECT_DICAS.realizado).not.toContain('Fase 3')
  })
})

describe('entrega prevista, folga e risco (PLN-01b)', () => {
  it('folga em dias corridos antes ou depois do prometido', () => {
    expect(slackText(3)).toBe('3 dias antes do prometido')
    expect(slackText(1)).toBe('1 dia antes do prometido')
    expect(slackText(-7)).toBe('7 dias depois do prometido')
    expect(slackText(0)).toBe('No dia prometido')
    expect(slackText(null)).toBeNull()
  })

  it('motivo da previsão vazia traduzido do código, com a OS', () => {
    expect(forecastReasonText('NoRouting', 'OS-2026-0007')).toBe('OS OS-2026-0007 sem roteiro')
    expect(forecastReasonText('NoWorkCenter', 'OS-2026-0007')).toBe('OS OS-2026-0007 com etapa sem centro de trabalho')
    expect(forecastReasonText('NoCapacity')).toBe('Uma OS num centro sem capacidade nos próximos 2 anos')
    expect(forecastReasonText('NoFabricationOrder')).toBe('Nenhuma OS de fabricação no projeto')
    expect(forecastReasonText(null)).toBe('Ainda não calculada')
  })

  it('o sem previsão leva ao que resolve (propositivo)', () => {
    expect(forecastFix('NoRouting', 'o1', 'p1')).toEqual({ label: 'Escolher roteiro', to: '/app/producao/os/$id', params: { id: 'o1' } })
    expect(forecastFix('NoWorkCenter', 'o1', 'p1')?.search).toEqual({ aba: 'etapas' })
    expect(forecastFix('NoCapacity', null, 'p1')?.to).toBe('/app/producao/calendario')
    expect(forecastFix('NoFabricationOrder', null, 'p1')).toEqual({ label: 'Gerar OS', to: '/app/producao/os', search: { semOs: 1, projeto: 'p1' } })
    expect(forecastFix(null, null, 'p1')).toBeNull()
  })

  it('chip e faixa: em risco em âmbar, atrasado em vermelho', () => {
    expect(riskChip('AtRisk')).toEqual({ tone: 'warn', label: 'Em risco' })
    expect(riskChip('Late')).toEqual({ tone: 'crit', label: 'Atrasado' })
    expect(riskChip('None')).toBeNull()
    expect(riskStripe('AtRisk')).toContain('border-l-warn')
    expect(riskStripe('Late')).toContain('border-l-crit')
    expect(riskStripe('None')).toBeUndefined()
  })

  it('dicas de entrega prevista, folga e em risco sem tolerância', () => {
    expect(PROJECT_DICAS.emRisco).toContain('mesmo que por um dia')
    expect(PROJECT_DICAS.folga).toContain('Dias corridos')
  })

  it('prazo de instalação de 0 a 120 dias úteis', () => {
    expect(installationDaysError('5')).toBeUndefined()
    expect(installationDaysError('0')).toBeUndefined()
    expect(installationDaysError('120')).toBeUndefined()
    expect(installationDaysError('121')).toBe('Informe de 0 a 120 dias úteis.')
    expect(installationDaysError('')).toBe('Informe de 0 a 120 dias úteis.')
  })
})
