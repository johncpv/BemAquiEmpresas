import { describe, expect, it } from 'vitest'
import type { DreRow } from '@/api/generated/model'
import { formatMoney } from '@/lib/format'
import { addMonths, changesText, currentMonth, formatSigned, isCritical, isDrillable, monthDelta, monthLabel, monthPercent, toTree } from './dre'

const row = (key: string, kind: DreRow['kind'], parentKey: string | null = null, extra: Partial<DreRow> = {}): DreRow => ({
  key, kind, label: key, group: null, accountId: null, code: null, parentKey, level: 1, values: [1, 2], total: 3, percentOfNetRevenue: null, delta: 1, deltaPercent: 100,
  criticalWhenNegative: false, hasChildren: false, ...extra,
})

describe('DRE (FIN-08)', () => {
  it('monta a árvore pelas chaves do servidor, com as contas por baixo do grupo', () => {
    const tree = toTree([
      row('group:GrossRevenue', 'Group'), row('account:1', 'Account', 'group:GrossRevenue', { code: '1', label: 'Receitas' }),
      row('account:11', 'Account', 'account:1', { code: '1.1', label: 'Vendas' }), row('subtotal:NetRevenue', 'Subtotal'), row('memo:Advances', 'Memo'),
    ])
    expect(tree.map((n) => n.id)).toEqual(['group:GrossRevenue', 'subtotal:NetRevenue', 'memo:Advances'])
    expect(tree[0].children?.[0].label).toBe('1 Receitas')
    expect(tree[0].children?.[0].children?.[0].label).toBe('1.1 Vendas')
  })

  it('meses: rótulo curto, soma de meses e mês corrente em São Paulo', () => {
    expect(monthLabel('2026-03-01')).toBe('mar/26')
    expect(addMonths('2026-10-01', -11)).toBe('2025-11-01')
    expect(addMonths('2026-01-01', 1)).toBe('2026-02-01')
    expect(currentMonth(new Date('2026-11-01T02:00:00Z'))).toBe('2026-10-01')
  })

  it('crítico só em resultado negativo; drill-down só em conta e grupo', () => {
    const result = row('subtotal:PeriodResult', 'Subtotal', null, { criticalWhenNegative: true })
    expect(isCritical(result, -1)).toBe(true)
    expect(isCritical(result, 0)).toBe(false)
    expect(isCritical(row('group:Costs', 'Group'), -5)).toBe(false)
    expect(isDrillable(row('group:Costs', 'Group'))).toBe(true)
    expect(isDrillable(result)).toBe(false)
    expect(isDrillable(row('memo:Advances', 'Memo'))).toBe(false)
  })

  it('Δ e % do mês escolhido (400 px)', () => {
    expect(monthDelta([100, 150, 120], 2)).toEqual({ delta: -30, percent: -20 })
    expect(monthDelta([0, 150], 1)).toEqual({ delta: 150, percent: null })
    expect(monthDelta([100], 0)).toEqual({ delta: null, percent: null })
    expect(monthPercent(25, 200)).toBe(12.5)
    expect(monthPercent(25, 0)).toBeNull()
    expect(formatSigned(-2500, formatMoney).replace(/\s/g, ' ')).toBe('−R$ 2.500,00')
    expect(formatSigned(null, formatMoney)).toBe('—')
  })

  it('texto do "Mudou desde a sua última consulta"', () => {
    expect(changesText('2026-10-20T13:00:00Z', 3, ['2026-08-01', '2026-09-01'], () => '20/10/2026 10:00'))
      .toBe('Mudou desde a sua última consulta em 20/10/2026 10:00: 3 documentos em ago/26 e set/26.')
    expect(changesText('x', 1, ['2026-09-01'], () => 'ontem')).toBe('Mudou desde a sua última consulta em ontem: 1 documento em set/26.')
  })
})
