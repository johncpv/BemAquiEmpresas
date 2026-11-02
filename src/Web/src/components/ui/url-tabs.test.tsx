import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { revealActive, useScrollStrip } from '@/hooks/useScrollStrip'
import { renderWithRouter } from '@/test-utils'
import { type UrlTab, UrlTabs } from './url-tabs'

afterEach(cleanup)

// As abas do funcionário (RH → Funcionários): as que não cabiam na coluna e apareciam cortadas ("…ais").
const LABELS = ['Pessoais', 'Contrato e salário', 'Linha do tempo', 'Dependentes', 'Pagamento e adicionais', 'Documentos', 'Ponto', 'Férias', 'Rescisão', 'Histórico de alterações']
const tabs: UrlTab[] = LABELS.map((label, i) => ({ id: `t${i}`, label, content: <p>Conteúdo {label}</p> }))

describe('UrlTabs: nenhuma aba escondida', () => {
  it('a faixa quebra linha e não rola escondendo opção', async () => {
    renderWithRouter(() => <UrlTabs tabs={tabs} label="Funcionário" />)
    const list = await screen.findByRole('tablist', { name: 'Funcionário' })
    expect(list.className).toContain('flex-wrap')
    for (const cls of ['overflow-x-auto', 'scrollbar-none', 'fade-x']) expect(list.className).not.toContain(cls)
    const all = screen.getAllByRole('tab')
    expect(all.map((t) => t.textContent)).toEqual(LABELS)
    // shrink-0 impediria a quebra; cada aba fica inteira (sem quebrar o rótulo).
    for (const t of all) expect(t.className).not.toContain('shrink-0')
  })

  it('ao carregar com ?aba= a aba ativa é a marcada e o painel dela aparece', async () => {
    renderWithRouter(() => <UrlTabs tabs={tabs} label="Funcionário" />, '/?aba=t9')
    const active = await screen.findByRole('tab', { name: 'Histórico de alterações' })
    expect(active.getAttribute('aria-selected')).toBe('true')
    expect(screen.getByText('Conteúdo Histórico de alterações')).toBeTruthy()
    expect(screen.getAllByRole('tab', { selected: true })).toHaveLength(1)
  })

  it('setas do teclado andam entre as abas', async () => {
    renderWithRouter(() => <UrlTabs tabs={tabs} label="Funcionário" />)
    const first = await screen.findByRole('tab', { name: 'Pessoais' })
    fireEvent.keyDown(first, { key: 'ArrowLeft' })
    expect((await screen.findByRole('tab', { name: 'Histórico de alterações' })).getAttribute('aria-selected')).toBe('true')
  })
})

/** Caixa falsa para o jsdom (sem layout). */
function box(el: Element, left: number, width: number) {
  el.getBoundingClientRect = () => ({ left, right: left + width, width, top: 0, bottom: 20, height: 20, x: left, y: 0, toJSON: () => ({}) })
}

describe('faixa da barra do topo: aba ativa à vista', () => {
  it('rola só a faixa até a aba ativa, à direita ou à esquerda', () => {
    const strip = document.createElement('nav')
    strip.innerHTML = '<a>Um</a><a aria-current="page">Dez</a>'
    box(strip, 0, 300)
    box(strip.children[1], 500, 80)
    strip.scrollLeft = 0
    let left = 0
    Object.defineProperty(strip, 'scrollLeft', { configurable: true, get: () => left, set: (v: number) => { left = v } })
    revealActive(strip)
    expect(left).toBe(500 + 80 - 300 + 28)

    left = 400
    box(strip.children[1], -120, 80)
    revealActive(strip)
    expect(left).toBe(400 - 120 - 28)
  })

  it('o hook revela a aba ativa ao montar', () => {
    let revealed = false
    function Strip() {
      const ref = useScrollStrip<HTMLElement>('dez')
      return (
        <nav aria-label="Telas" ref={(el) => {
          if (el) {
            box(el, 0, 100)
            Object.defineProperty(el, 'scrollLeft', { configurable: true, get: () => 0, set: () => { revealed = true } })
            const active = el.querySelector('[aria-current="page"]')
            if (active) box(active, 400, 50)
          }
          ref(el)
        }}>
          <a>Um</a><a aria-current="page">Dez</a>
        </nav>
      )
    }
    render(<Strip />)
    expect(revealed).toBe(true)
  })
})
