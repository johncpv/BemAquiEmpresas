/**
 * Service worker e troca de versão do app do caixa (OFF-01 CA 2 e 3). O service worker do /pdv (`/pdv/sw.js`, escopo `/pdv/`, ver
 * `pwa.pdv.config.ts`) guarda o app inteiro para abrir sem rede. Versão nova publicada fica **esperando** (sem `skipWaiting`): com sessão de
 * caixa aberta, o app não troca de versão e avisa "Atualização disponível. Será aplicada na troca de turno."; sem sessão aberta, aplica na hora
 * (manda o SKIP_WAITING para o service worker novo e recarrega quando ele assume).
 */
export const UPDATE_PENDING_MESSAGE = 'Atualização disponível. Será aplicada na troca de turno.'

export const PDV_SW_URL = '/pdv/sw.js'
export const PDV_SCOPE = '/pdv/'

/**
 * Há sessão de caixa (turno) aberta neste aparelho? (CX-01, ponto de extensão da OFF-01.) A tela do caixa marca ao ler o turno do `LocalStore`
 * (onde ele fica, para valer sem rede) e ao abrir ou fechar; fechar o turno chama `applyPendingUpdate()`. Antes de a tela ler, vale `false`.
 */
let cashSessionOpen = false

export function hasOpenCashSession(): boolean {
  return cashSessionOpen
}

/** A tela do caixa avisa se há turno aberto; com versão nova esperando e o turno fechado, ela é aplicada na hora. */
export function markCashSessionOpen(open: boolean): void {
  cashSessionOpen = open
  if (!open && waitingWorker) setWaiting(waitingWorker, hasOpenCashSession)
}

export type UpdateDecision = 'none' | 'apply-now' | 'wait-for-shift-change'

/** Regra pura (testada): só troca de versão sem sessão de caixa aberta. */
export function decideUpdate(waiting: boolean, openSession: boolean): UpdateDecision {
  if (!waiting) return 'none'
  return openSession ? 'wait-for-shift-change' : 'apply-now'
}

type Listener = (pending: boolean) => void
let waitingWorker: ServiceWorker | null = null
const listeners = new Set<Listener>()

function setWaiting(worker: ServiceWorker | null, isOpen: () => boolean): void {
  waitingWorker = worker
  const decision = decideUpdate(!!worker, isOpen())
  if (decision === 'apply-now') applyPendingUpdate()
  for (const l of listeners) l(decision === 'wait-for-shift-change')
}

/** A tela assina para mostrar (ou esconder) o aviso de atualização pendente. */
export function onUpdatePending(listener: Listener): () => void {
  listeners.add(listener)
  listener(!!waitingWorker && decideUpdate(true, hasOpenCashSession()) === 'wait-for-shift-change')
  return () => { listeners.delete(listener) }
}

/** Aplica a versão que espera (troca de turno ou sem sessão aberta): o service worker novo assume e a página recarrega. */
export function applyPendingUpdate(): void {
  waitingWorker?.postMessage({ type: 'SKIP_WAITING' })
}

/**
 * Registra o service worker do /pdv (só no build: em desenvolvimento o Vite serve sem service worker). Confere versão nova ao abrir, a cada 30 min
 * e quando a aba volta a ficar visível.
 */
export function registerPdvServiceWorker(isOpen: () => boolean = hasOpenCashSession): void {
  if (!import.meta.env.PROD || typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return
  const sw = navigator.serviceWorker
  let reloading = false
  const hadController = !!sw.controller
  sw.addEventListener('controllerchange', () => {
    // Primeira instalação (sem controlador antes): o clientsClaim assume sem precisar recarregar.
    if (!hadController || reloading) return
    reloading = true
    window.location.reload()
  })

  void sw.register(PDV_SW_URL, { scope: PDV_SCOPE }).then((registration) => {
    const track = () => setWaiting(registration.waiting, isOpen)
    if (registration.waiting) track()
    registration.addEventListener('updatefound', () => {
      const installing = registration.installing
      installing?.addEventListener('statechange', () => {
        if (installing.state === 'installed' && sw.controller) track()
      })
    })
    const check = () => { void registration.update().catch(() => { /* sem rede: confere na próxima */ }) }
    setInterval(check, 30 * 60_000)
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') check() })
  }).catch(() => {
    // sem service worker o /pdv funciona com rede; só não abre sem ela
  })
}
