import { useCallback, useEffect, useRef } from 'react'

/** Item ativo da faixa: aba da rota atual (aria-current) ou aba selecionada (aria-selected). */
const ACTIVE = '[aria-current="page"], [aria-selected="true"]'

/**
 * Rola só a faixa (nunca a página) até o item ativo ficar inteiro à vista, com uma folga para o esmaecido da borda.
 * Exportada para teste.
 */
export function revealActive(el: HTMLElement) {
  const item = el.querySelector<HTMLElement>(ACTIVE)
  if (!item) return
  const strip = el.getBoundingClientRect()
  const box = item.getBoundingClientRect()
  const gap = 28
  if (box.left < strip.left) el.scrollLeft -= strip.left - box.left + gap
  else if (box.right > strip.right) el.scrollLeft += box.right - strip.right + gap
}

/**
 * Faixa horizontal de abas sem barra de rolagem nativa (classes `scrollbar-none fade-x`, src/index.css), usada só na
 * barra do topo (abas do módulo): marca `data-fade-start`/`data-fade-end` quando há conteúdo escondido de cada lado (o
 * esmaecido da borda), transforma a roda do mouse (vertical) em rolagem horizontal e garante a aba ativa à vista ao
 * carregar e a cada troca (`active`). Abas internas da página não rolam: quebram linha (UrlTabs), nunca escondem opção.
 * Devolve uma ref de callback para pôr no elemento que rola.
 */
export function useScrollStrip<T extends HTMLElement>(active?: unknown) {
  const cleanup = useRef<(() => void) | null>(null)
  const element = useRef<T | null>(null)

  useEffect(() => {
    if (element.current) revealActive(element.current)
  }, [active])

  return useCallback((el: T | null) => {
    cleanup.current?.()
    cleanup.current = null
    element.current = el
    if (!el) return

    const update = () => {
      const max = el.scrollWidth - el.clientWidth
      el.toggleAttribute('data-fade-start', el.scrollLeft > 1)
      el.toggleAttribute('data-fade-end', max - el.scrollLeft > 1)
    }
    const wheel = (event: WheelEvent) => {
      // Só a roda vertical sobre uma faixa que transborda; o gesto horizontal do trackpad segue o padrão do navegador.
      if (el.scrollWidth <= el.clientWidth || Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return
      const before = el.scrollLeft
      el.scrollLeft += event.deltaY
      if (el.scrollLeft !== before) event.preventDefault()
    }

    revealActive(el)
    update()
    el.addEventListener('scroll', update, { passive: true })
    el.addEventListener('wheel', wheel, { passive: false })
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(update)
    observer?.observe(el)
    for (const child of el.children) observer?.observe(child)
    cleanup.current = () => {
      el.removeEventListener('scroll', update)
      el.removeEventListener('wheel', wheel)
      observer?.disconnect()
    }
  }, [])
}
