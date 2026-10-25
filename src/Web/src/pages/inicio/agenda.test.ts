import { describe, expect, it } from 'vitest'
import type { AgendaItem, AgendaView } from '@/api/generated/model'
import { homeRouteFor } from '@/auth/home-route'
import { safeReturn } from '@/auth/pending-challenge'
import { groupByTier, isEmpty, splitLink, stripe } from './agenda'

const item = (category: string, tier: AgendaItem['tier']): AgendaItem => ({
  category, title: category, why: '', link: null, dueDate: null, amount: null, tier, count: 1, action: 'Abrir', entries: [],
})

describe('Minha pauta (DSH-04)', () => {
  it('agrupa nos três níveis, na ordem, mantendo a ordem do servidor e sem nível vazio', () => {
    const view: AgendaView = {
      items: [item('a', 'Unblocks'), item('b', 'Unblocks'), item('c', 'DueSoon')],
      failures: [{ category: 'x', label: 'x', tier: 'Overdue', message: 'Não foi possível carregar x.' }],
      total: 3,
    }
    const groups = groupByTier(view)
    expect(groups.map((g) => g.label)).toEqual(['Destrava outra pessoa ou dinheiro', 'Já passou do prazo', 'Vence em breve'])
    expect(groups[0].items.map((i) => i.category)).toEqual(['a', 'b'])
    expect(groups[1].failures).toHaveLength(1)
    expect(groupByTier({ items: [item('c', 'DueSoon')], failures: [], total: 1 }).map((g) => g.tier)).toEqual(['DueSoon'])
  })

  it('faixa âmbar no nível 1, vermelha no nível 2, sem faixa no 3', () => {
    expect(stripe('Unblocks')).toBe('warn')
    expect(stripe('Overdue')).toBe('crit')
    expect(stripe('DueSoon')).toBeNull()
  })

  it('vazio só sem item e sem falha (CA 9)', () => {
    expect(isEmpty({ items: [], failures: [], total: 0 })).toBe(true)
    expect(isEmpty({ items: [], failures: [{ category: 'x', label: 'x', tier: 'Unblocks', message: '' }], total: 0 })).toBe(false)
  })

  it('separa caminho e parâmetros do link e recusa link de fora do app', () => {
    expect(splitLink('/app/financeiro/ordens-de-pagamento/1?acao=aprovar')).toEqual({ to: '/app/financeiro/ordens-de-pagamento/1', search: { acao: 'aprovar' } })
    expect(splitLink('/app/compras/solicitacoes/nova?item=i&os=o')).toEqual({ to: '/app/compras/solicitacoes/nova', search: { item: 'i', os: 'o' } })
    expect(splitLink('https://evil.example/app')).toEqual({ to: '/app', search: {} })
    expect(splitLink('//evil.example')).toEqual({ to: '/app', search: {} })
  })

  it('página inicial: o Início para todos, num ponto só (CA 8)', () => {
    expect(homeRouteFor()).toBe('/app')
    expect(homeRouteFor({ isOwner: true, permissions: ['dashboard.view'] })).toBe('/app')
    expect(safeReturn(undefined)).toBe('/app')
    expect(safeReturn('/app/compras')).toBe('/app/compras')
    expect(safeReturn('//evil.example')).toBe('/app')
    expect(safeReturn('/entrar')).toBe('/app')
  })
})
