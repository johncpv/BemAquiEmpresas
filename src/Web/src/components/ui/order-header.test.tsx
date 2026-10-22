import { cleanup, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { NavigationContext } from '@/navigation/navigation-context'
import { renderWithRouter } from '@/test-utils'
import { FactsBar } from './facts-bar'
import { OrderHeader } from './order-header'

afterEach(cleanup)

const nav = { areas: [], all: [], tenantName: 'Konebras', status: 'ready' as const, retry: () => undefined }

describe('OrderHeader e FactsBar (PRD-01 CA 10 e 11, padrão 10)', () => {
  it('cabeçalho com número, situação, etapa atual pela letra do roteiro, fatos e o projeto', async () => {
    renderWithRouter(() => (
      <NavigationContext.Provider value={nav}>
        <OrderHeader title="OS-2026-00001 — Aurora" status={{ label: 'Aberta', tone: 'info' }}
          stages={{ names: ['Projeto', 'Corte', 'Montagem'], letters: ['P', 'C', 'M'], current: 1 }}
          project={{ id: 'p1', number: 'PRJ-2026-0001' }} meta={<span>Cliente Aurora</span>} actions={<button type="button">Cancelar OS</button>} />
      </NavigationContext.Provider>
    ))
    expect(await screen.findByRole('heading', { name: 'OS-2026-00001 — Aurora' })).toBeTruthy()
    expect(screen.getByText('Aberta')).toBeTruthy()
    expect(screen.getByRole('img', { name: 'Etapa Corte, 2 de 3' }).textContent).toContain('C')
    expect(screen.getByRole('link', { name: 'Ver projeto PRJ-2026-0001' })).toBeTruthy()
    expect(screen.getByText('Cliente Aurora')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Cancelar OS' })).toBeTruthy()
  })

  it('sem etapas (OS sem roteiro ou cancelada) o indicador some', async () => {
    renderWithRouter(() => (
      <NavigationContext.Provider value={nav}>
        <OrderHeader title="OS-2026-00002 — Aurora" status={{ label: 'Cancelada', tone: 'neutral' }} stages={null} />
      </NavigationContext.Provider>
    ))
    await screen.findByRole('heading', { name: 'OS-2026-00002 — Aurora' })
    expect(screen.queryByRole('img')).toBeNull()
  })

  it('fatos com rótulo, valor e dica, em duas colunas a 400 px', () => {
    const { container } = render(<FactsBar facts={[{ label: 'Prometido', value: '20/12/2026', dica: 'Data prometida ao cliente.' }, { label: 'Horas padrão', value: '5,50 h' }]} />)
    const bar = container.querySelector('dl')!
    expect(bar.className).toContain('grid-cols-2')
    expect(bar.className).toContain('sm:grid-cols-4')
    expect(within(bar as HTMLElement).getByText('Prometido')).toBeTruthy()
    expect(within(bar as HTMLElement).getByText('5,50 h')).toBeTruthy()
    expect(within(bar as HTMLElement).getByRole('button', { name: 'Dica: Prometido' })).toBeTruthy()
  })
})