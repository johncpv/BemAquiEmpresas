import type { VitePWAOptions } from 'vite-plugin-pwa'

/**
 * PWA do caixa (OFF-01 CA 1 e 2, rascunho A itens 1 a 3): build próprio (`vite.pdv.config.ts`, saída em `dist/pdv/`), manifesto "BemAqui Caixa"
 * e service worker próprio em `/pdv/sw.js` com escopo `/pdv/`. Diferente do /fab (`pwa.config.ts`), este service worker guarda o app inteiro
 * do caixa para **abrir sem rede**; continua sem guardar /api/* (o dado local fica no `LocalStore`, não no Cache Storage). A versão nova não
 * assume sozinha (`skipWaiting: false`): quem decide é `src/pdv/app-update.ts`, que segura a troca com sessão de caixa aberta (CA 3).
 * O /fab, o /app e a /tv nunca ficam sob este service worker (o escopo é só /pdv/).
 */
export const PDV_SCOPE = '/pdv/'

export const pdvPwaOptions: Partial<VitePWAOptions> = {
  registerType: 'prompt',
  injectRegister: false, // registrado por src/pdv/app-update.ts
  scope: PDV_SCOPE,
  filename: 'pdv/sw.js',
  manifestFilename: 'pdv/manifest.webmanifest',
  includeAssets: [],
  manifest: {
    id: PDV_SCOPE,
    name: 'BemAqui Caixa',
    short_name: 'Caixa',
    description: 'Caixa da loja (PDV): vende mesmo sem internet.',
    lang: 'pt-BR',
    start_url: PDV_SCOPE,
    scope: PDV_SCOPE,
    display: 'standalone',
    orientation: 'any',
    background_color: '#0f1419',
    theme_color: '#0f1419',
    icons: [
      { src: '/pdv/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
      { src: '/pdv/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
      { src: '/pdv/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    ],
  },
  workbox: {
    // Só o que o build do caixa gera (dist/pdv/**); nada do backoffice, do /fab nem da /tv.
    globPatterns: ['pdv/index.html', 'pdv/assets/*.{js,css,woff2}', 'pdv/icon-*.png'],
    // As entradas ficam absolutas (/pdv/...): o service worker mora em /pdv/sw.js e resolveria "pdv/x" como /pdv/pdv/x.
    modifyURLPrefix: { '': '/' },
    navigateFallback: '/pdv/index.html',
    navigateFallbackAllowlist: [/^\/pdv\//],
    navigateFallbackDenylist: [/^\/api\//, /^\/hubs\//, /^\/health/, /^\/app(\/|$)/, /^\/fab(\/|$)/, /^\/tv(\/|$)/],
    runtimeCaching: [],
    maximumFileSizeToCacheInBytes: 4 * 1024 * 1024,
    cleanupOutdatedCaches: true,
    // Primeira instalação assume a página na hora (abre sem rede já na segunda visita); versão nova espera a troca de turno.
    clientsClaim: true,
    skipWaiting: false,
  },
  devOptions: { enabled: false },
}
