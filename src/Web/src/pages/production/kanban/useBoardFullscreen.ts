import { useCallback, useEffect, useState } from 'react'

/** Preferência "tela cheia" do Kanban nesta aba do navegador. */
export const FULLSCREEN_KEY = 'bq.kanban.telaCheia'

function remembered() {
  try {
    return sessionStorage.getItem(FULLSCREEN_KEY) === '1'
  } catch {
    return false
  }
}

function remember(on: boolean) {
  try {
    if (on) sessionStorage.setItem(FULLSCREEN_KEY, '1')
    else sessionStorage.removeItem(FULLSCREEN_KEY)
  } catch {
    // Sem armazenamento (janela privada, bloqueio): a preferência só não fica lembrada.
  }
}

/**
 * Tela cheia do Kanban. O quadro vira uma camada fixa (inset-0, fundo do tema) sobre a barra do topo e o dock, e o
 * navegador entra em tela cheia pela Fullscreen API quando existe. A API vai no documento, não no quadro: os menus e as
 * confirmações dos cartões abrem num portal do body, e num quadro em tela cheia nativa eles ficariam invisíveis. Sem a
 * API (ou se o navegador recusar), fica só a camada. Esc sai: nativo na API; na camada, pelo atalho abaixo.
 * A preferência fica no sessionStorage; ao recarregar, volta só a camada (a API exige um clique).
 */
export function useBoardFullscreen() {
  const [full, setFull] = useState(remembered)

  const exit = useCallback(() => {
    setFull(false)
    remember(false)
    if (typeof document !== 'undefined' && document.fullscreenElement && document.exitFullscreen) void document.exitFullscreen().catch(() => undefined)
  }, [])

  const enter = useCallback(() => {
    setFull(true)
    remember(true)
    const root = document.documentElement
    if (typeof root.requestFullscreen === 'function' && !document.fullscreenElement) void root.requestFullscreen().catch(() => undefined)
  }, [])

  const toggle = useCallback(() => (full ? exit() : enter()), [full, enter, exit])

  // Saiu da tela (outra rota) com a tela cheia nativa ligada: devolve a janela; a preferência continua lembrada.
  useEffect(() => () => {
    if (document.fullscreenElement && document.exitFullscreen) void document.exitFullscreen().catch(() => undefined)
  }, [])

  useEffect(() => {
    if (!full) return
    // Esc da tela cheia nativa: o navegador sai e avisa por fullscreenchange; a camada sai junto.
    const change = () => {
      if (!document.fullscreenElement) exit()
    }
    // Esc na camada sem API. Um menu ou diálogo aberto trata o próprio Esc primeiro.
    const key = (event: globalThis.KeyboardEvent) => {
      if (event.key !== 'Escape' || event.defaultPrevented || document.fullscreenElement) return
      if (document.querySelector('[role="dialog"], [role="alertdialog"], [role="menu"]')) return
      exit()
    }
    document.addEventListener('fullscreenchange', change)
    document.addEventListener('keydown', key)
    return () => {
      document.removeEventListener('fullscreenchange', change)
      document.removeEventListener('keydown', key)
    }
  }, [full, exit])

  return { full, toggle }
}
