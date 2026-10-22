/**
 * Registra o service worker do app do operador, só a partir do /fab e com o escopo /fab (PRD-03b CA 1): o backoffice (/app) nunca
 * fica sob ele. Em desenvolvimento não há service worker (o Vite serve tudo direto).
 */
export function registerFloorServiceWorker(): void {
  if (!import.meta.env.PROD || typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return
  navigator.serviceWorker.register('/sw.js', { scope: '/fab' }).catch(() => {
    // sem service worker o /fab continua funcionando com rede; só não fica instalável
  })
}