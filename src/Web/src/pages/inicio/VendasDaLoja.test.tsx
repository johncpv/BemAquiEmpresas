import { cleanup, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type { SaleTrailEntry, StoreSalesCard } from '@/api/generated/model'
import { Trail } from '@/pages/store/SalePage'
import { renderWithRouter } from '@/test-utils'
import { VendasDaLojaCards } from './VendasDaLoja'

afterEach(cleanup)

const card = (over: Partial<StoreSalesCard> = {}): StoreSalesCard => ({
  today: '2026-11-25', todayCount: 4, todayAmount: 200, lastWeekCount: 2, lastWeekAmount: 160, changePct: 25, ticketToday: 50, ticketWeek: 42.5, weekCount: 30,
  openRegisters: [{
    sessionId: 's1', storeId: 'l1', storeName: 'Loja Centro', registerId: 'r1', registerNumber: 2, openedAt: '2026-11-25T11:00:00Z', openMinutes: 125, sales: 20, cancelled: 2,
    cancelledPercent: 6.5, cancelAlertPercent: 3, cancelAlert: true,
  }],
  maybeUnsent: false, updatedAt: '2026-11-25T13:00:00Z', ...over,
})

describe('cards da loja no Início (PDV-07)', () => {
  it('mostra as vendas de hoje, os caixas abertos com o alerta de cancelamentos e o ticket médio (CA 8 e 9)', async () => {
    renderWithRouter(() => <VendasDaLojaCards card={card()} />)
    expect(await screen.findByText('4 vendas')).toBeTruthy()
    expect(screen.getByText('+25%')).toBeTruthy()
    expect(screen.getByText(/Loja Centro · Caixa 02/)).toBeTruthy()
    expect(screen.getByText('há 2 h 05 min')).toBeTruthy()
    expect(screen.getByText('Cancelamentos acima de 3%')).toBeTruthy()
    expect(screen.getByRole('link', { name: /Caixas abertos/ }).getAttribute('href')).toBe('/app/loja/caixas?status=Open')
    expect(screen.getByRole('link', { name: /Vendas de hoje/ }).getAttribute('href')).toBe('/app/loja/vendas')
    expect(screen.queryByText(/Pode haver vendas ainda não enviadas/)).toBeNull()
  })

  it('avisa das vendas que podem não ter chegado e mostra o vazio do dia (CA 10)', async () => {
    renderWithRouter(() => <VendasDaLojaCards card={card({ todayCount: 0, todayAmount: 0, ticketToday: null, changePct: null, openRegisters: [], maybeUnsent: true })} />)
    expect(await screen.findByText(/Pode haver vendas ainda não enviadas pelos caixas/)).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Pendências do PDV' }).getAttribute('href')).toBe('/app/loja/pendencias')
    expect(screen.getAllByText('Nenhuma venda hoje ainda.')).toHaveLength(2)
    expect(screen.getByText('Nenhum caixa aberto agora.')).toBeTruthy()
  })

  it('mostra a trilha da venda em ordem, com o grupo de cada passo (CA 4)', async () => {
    const entries: SaleTrailEntry[] = [
      { at: '2026-11-25T12:00:00Z', kind: 'device', title: 'Venda começou', detail: 'Ana', tone: 'info' },
      { at: '2026-11-25T12:01:00Z', kind: 'approval', title: 'Supervisor aprovou: desconto', detail: 'Bia para Ana', tone: 'info' },
      { at: '2026-11-25T12:30:00Z', kind: 'sync', title: 'Chegou ao servidor: a venda', detail: '(feito sem internet)', tone: 'warn' },
      { at: '2026-11-25T12:31:00Z', kind: 'print', title: 'Reimpressão (2ª via)', detail: null, tone: 'warn' },
    ]
    renderWithRouter(() => <Trail entries={entries} />)
    const items = await screen.findAllByRole('listitem')
    expect(items.map((i) => i.getAttribute('data-kind'))).toEqual(['device', 'approval', 'sync', 'print'])
    expect(screen.getByText('Supervisor')).toBeTruthy()
    expect(screen.getByText('Reimpressão (2ª via)')).toBeTruthy()
  })
})
