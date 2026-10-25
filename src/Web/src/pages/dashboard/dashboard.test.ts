import { describe, expect, it } from 'vitest'
import { homeRouteFor, hasReturn } from '@/auth/home-route'
import { repeatForYear, sumOf } from '@/pages/settings/parameters/revenue-targets'
import { cashFlowHref, cashPoints, greetingOf, longDateOf, meterWidth, MOBILE_ORDER, scenarioOf, scenarioParam, workingDaysLabel } from './dashboard'

const modules = (dashboard: { released?: boolean; enabled?: boolean; actions?: string[] } = {}) => [
  { code: 'dashboard', released: dashboard.released ?? true, enabled: dashboard.enabled ?? true, actions: dashboard.actions ?? ['view'] },
  { code: 'finance', released: true, enabled: true, actions: ['view'] },
]

describe('página inicial (DSH-01a CA 3)', () => {
  it('Proprietário com o painel liberado cai no painel; os demais no Início', () => {
    expect(homeRouteFor({ isOwner: true, modules: modules() })).toBe('/app/painel')
    expect(homeRouteFor({ isOwner: false, modules: modules() })).toBe('/app')
    expect(homeRouteFor({ isOwner: true, modules: modules({ released: false }) })).toBe('/app')
    expect(homeRouteFor({ isOwner: true, modules: modules({ enabled: false }) })).toBe('/app')
    expect(homeRouteFor({ isOwner: true, modules: [] })).toBe('/app')
  })

  it('rota pedida vale; o /app que o login recebe por padrão não conta', () => {
    expect(hasReturn('/app/financeiro')).toBe(true)
    expect(hasReturn('/app')).toBe(false)
    expect(hasReturn('/app/')).toBe(false)
    expect(hasReturn(undefined)).toBe(false)
  })
})

describe('painel (DSH-01a)', () => {
  it('saudação com o primeiro nome e data por extenso', () => {
    expect(greetingOf('Boa tarde', 'John')).toBe('Boa tarde, John')
    expect(greetingOf('Bom dia', '')).toBe('Bom dia')
    expect(longDateOf('2026-10-16')).toBe('Sexta-feira, 16 de outubro')
  })

  it('cenário na URL e o fluxo de caixa no cenário escolhido (CA 7)', () => {
    expect(scenarioOf(undefined)).toBe('both')
    expect(scenarioOf('ajustado')).toBe('adjusted')
    expect(scenarioOf('base')).toBe('base')
    expect(scenarioParam('both')).toBeUndefined()
    expect(scenarioParam('adjusted')).toBe('ajustado')
    expect(cashFlowHref('base')).toContain('cenario=base')
    expect(cashFlowHref('both')).toContain('cenario=ajustado')
  })

  it('gráfico só com as séries do cenário', () => {
    const weeks = [{ weekEnd: '2026-10-23', baseBalance: 100, adjustedBalance: 80 }]
    expect(cashPoints(weeks, 'base')).toEqual([{ semana: '23/10', base: 100, ajustado: undefined }])
    expect(cashPoints(weeks, 'adjusted')).toEqual([{ semana: '23/10', base: undefined, ajustado: 80 }])
    expect(cashPoints(weeks, 'both')[0]).toMatchObject({ base: 100, ajustado: 80 })
  })

  it('medidor de 0 a 100 e dias úteis', () => {
    expect(meterWidth(82.3)).toBe(82.3)
    expect(meterWidth(140)).toBe(100)
    expect(meterWidth(null)).toBe(0)
    expect(workingDaysLabel(11)).toBe('faltam 11 dias úteis')
    expect(workingDaysLabel(1)).toBe('falta 1 dia útil')
  })

  it('a 400 px, a ordem é Saldo, Caixa, Decisões, Faturamento, Vencido e Comercial', () => {
    const order = Object.entries(MOBILE_ORDER).sort((a, b) => a[1].localeCompare(b[1])).map(([code]) => code)
    expect(order).toEqual(['balances', 'cash-forecast', 'decisions', 'revenue', 'overdue', 'commercial'])
  })
})

describe('metas de faturamento (DSH-01a CA 9)', () => {
  it('"Repetir para o ano" copia o primeiro mês preenchido e o total soma os meses', () => {
    const values: (string | null)[] = [null, '380000', ...Array<string | null>(10).fill(null)]
    const repeated = repeatForYear(values)
    expect(repeated).toHaveLength(12)
    expect(repeated.every((v) => v === '380000')).toBe(true)
    expect(sumOf(repeated)).toBe(4_560_000)
    expect(repeatForYear(Array(12).fill(null))).toEqual(Array(12).fill(null))
  })
})
