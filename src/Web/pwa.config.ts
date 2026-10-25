import type { VitePWAOptions } from 'vite-plugin-pwa'

/**
 * PWA do app do operador (PRD-03b CA 1 e 2, ADR 0026 §4): instalável no tablet ("BemAqui Fábrica", start_url e escopo /fab) e um
 * service worker que guarda só o shell (HTML, JS, CSS, fontes e ícones). Ele nunca guarda /api/* nem /hubs/*: não há cache em
 * tempo de execução, a navegação só cai no shell dentro de /fab, e o /app (backoffice) não fica sob o service worker. Sem
 * funcionamento sem rede (ADR 0026 §6): sem rede, a tela diz "Sem conexão".
 */
export const FAB_SCOPE = '/fab'

export const pwaOptions: Partial<VitePWAOptions> = {
  registerType: 'autoUpdate',
  injectRegister: false, // registrado só pelo layout do /fab (src/pwa/register.ts), com o escopo /fab
  scope: FAB_SCOPE,
  filename: 'sw.js',
  includeAssets: ['favicon.svg'],
  manifest: {
    id: FAB_SCOPE,
    name: 'BemAqui Fábrica',
    short_name: 'Fábrica',
    description: 'App do operador: fila de etapas e apontamento no chão de fábrica.',
    lang: 'pt-BR',
    start_url: FAB_SCOPE,
    scope: FAB_SCOPE,
    display: 'standalone',
    orientation: 'any',
    background_color: '#0f1419',
    theme_color: '#0f1419',
    icons: [
      { src: '/fab-icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
      { src: '/fab-icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
      { src: '/fab-icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    ],
  },
  workbox: {
    globPatterns: ['index.html', 'assets/*.{js,css}', 'assets/*.woff2', 'favicon.svg', 'fab-icon-*.png'],
    navigateFallback: '/index.html',
    navigateFallbackAllowlist: [/^\/fab(\/|$)/],
    // PNL-01a CA 11 (ADR 0030 §2): o painel da TV (/tv) e a API do quiosque nunca passam pelo service worker.
    navigateFallbackDenylist: [/^\/api\//, /^\/hubs\//, /^\/health/, /^\/app(\/|$)/, /^\/tv(\/|$)/],
    runtimeCaching: [],
    maximumFileSizeToCacheInBytes: 4 * 1024 * 1024, // o bundle do app passa de 2 MiB (padrão do Workbox)
    cleanupOutdatedCaches: true,
    clientsClaim: true,
    skipWaiting: true,
  },
  devOptions: { enabled: false },
}