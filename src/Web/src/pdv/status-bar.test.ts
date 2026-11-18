import { describe, expect, it } from 'vitest'
import { ageText, statusView } from './status-bar'

/** OFF-01 CA 2 e 13: a faixa de estado fixa no topo do /pdv. Datas no fuso local, como a tela mostra. */
const now = new Date(2026, 10, 18, 15, 0)
const at = (h: number, m: number, day = 18) => new Date(2026, 10, day, h, m).toISOString()
const base = { online: true, pendingCount: 0, oldestPendingAt: null, lastSyncAt: at(14, 55), offlineWindowDays: null, now }

describe('faixa de estado do caixa', () => {
  it('sem rede e com fila: "Sem conexão: N vendas a enviar" com a idade da mais antiga', () => {
    const view = statusView({ ...base, online: false, pendingCount: 3, oldestPendingAt: at(14, 20) })
    expect(view.title).toBe('Sem conexão: 3 vendas a enviar')
    expect(view.tone).toBe('crit')
    expect(view.details).toContain('a mais antiga há 40 min')
    expect(view.details).toContain('última sincronização às 14:55')
  })

  it('singular: "1 venda a enviar"', () => {
    expect(statusView({ ...base, online: false, pendingCount: 1, oldestPendingAt: at(13, 0) }).title).toBe('Sem conexão: 1 venda a enviar')
  })

  it('sem rede e sem fila: "Sem conexão" com a hora da última sincronização', () => {
    const view = statusView({ ...base, online: false })
    expect(view).toMatchObject({ tone: 'warn', title: 'Sem conexão' })
    expect(view.details).toEqual(['última sincronização às 14:55'])
  })

  it('com rede e fila vazia: a hora da última sincronização', () => {
    expect(statusView(base)).toEqual({ tone: 'ok', title: 'Sincronizado às 14:55', details: [] })
  })

  it('com rede e com fila: as vendas a enviar e a idade da mais antiga', () => {
    const view = statusView({ ...base, pendingCount: 2, oldestPendingAt: at(14, 58) })
    expect(view.title).toBe('2 vendas a enviar')
    expect(view.details[0]).toBe('a mais antiga há 2 min')
  })

  it('sincronização de outro dia mostra a data', () => {
    expect(statusView({ ...base, lastSyncAt: at(9, 5, 16) }).title).toBe('Sincronizado em 16/11 às 09:05')
  })

  it('nunca sincronizou', () => {
    expect(statusView({ ...base, lastSyncAt: null })).toMatchObject({ title: 'Conectado', details: ['ainda não sincronizou'] })
  })

  it('PP-10: sem rede e com a janela da loja vinda da foto, mostra até quando vende sem conexão', () => {
    const view = statusView({ ...base, online: false, offlineWindowDays: 7 })
    expect(view.details).toContain('vende sem conexão até 25/11 às 14:55')
    expect(statusView({ ...base, online: false }).details.some((d) => d.startsWith('vende sem conexão'))).toBe(false)
  })

  it('idade curta', () => {
    expect(ageText(now.toISOString(), now)).toBe('agora')
    expect(ageText(at(12, 50), now)).toBe('há 2 h 10 min')
    expect(ageText(at(13, 0), now)).toBe('há 2 h')
    expect(ageText(at(15, 0, 17), now)).toBe('há 1 dia')
    expect(ageText(at(15, 0, 15), now)).toBe('há 3 dias')
  })
})
