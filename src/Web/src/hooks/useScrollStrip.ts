import { useCallback, useRef } from 'react'

/**
 * Faixa horizontal de abas ou chips sem barra de rolagem nativa (classes `scrollbar-none fade-x`, src/index.css):
 * marca `data-fade-start`/`data-fade-end` quando há conteúdo escondido de cada lado (o esmaecido da borda) e
 * transforma a roda do mouse (vertical) em rolagem horizontal da faixa. Trackpad e toque rolam normalmente.
 * Devolve uma ref de callback para pôr no elemento que rola.
 */
export function useScrollStrip<T extends HTMLElement>() {
  const cleanup = useRef<(() => void) | null>(null)

  return useCallback((el: T | null) => {
    cleanup.current?.()
    cleanup.current = null
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
