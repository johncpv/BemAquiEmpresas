import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { defineConfig, type Plugin, type ResolvedConfig } from 'vite'
import { VitePWA, type VitePluginPWAAPI } from 'vite-plugin-pwa'
import { pdvPwaOptions } from './pwa.pdv.config.ts'

/**
 * Build do app do caixa (OFF-01 CA 1): entrada própria (`pdv/index.html` → `src/pdv-app/main.tsx`), saída em `dist/pdv/` ao lado do build
 * principal e PWA próprio (`pwa.pdv.config.ts`). Roda depois do `vite build` principal (ver `pnpm build`), sem apagar o que ele gerou.
 * O `public/` do backoffice não entra: os ícones do caixa vêm de `pdv/public/pdv/`. Em desenvolvimento, o próprio `pnpm dev` já serve
 * `/pdv/` (o Vite acha `pdv/index.html`), sem service worker.
 */
/**
 * O vite-plugin-pwa acrescenta ao precache o manifesto e os ícones com caminho relativo (pdv/manifest.webmanifest), fora do
 * modifyURLPrefix do Workbox. Com o service worker em /pdv/sw.js, isso viraria /pdv/pdv/... (404) e a instalação offline falharia. Este
 * plugin, depois do PWA, deixa toda entrada absoluta (/pdv/...).
 */
const absolute = (url: string) => (url.startsWith('/') ? url : '/' + url)
let resolved: ResolvedConfig | undefined
const pdvAbsolutePrecache: Plugin = {
  name: 'pdv-absolute-precache',
  configResolved(config) {
    resolved = config
  },
  // No buildStart (depois de todos os configResolved, que rodam em paralelo): as opções do PWA já estão prontas.
  buildStart() {
    const api = resolved?.plugins.find((p) => p.name === 'vite-plugin-pwa')?.api as VitePluginPWAAPI | undefined
    api?.extendManifestEntries((entries) => entries.map((e) => (typeof e === 'string' ? absolute(e) : { ...e, url: absolute(e.url) })))
  },
}

export default defineConfig({
  plugins: [react(), tailwindcss(), VitePWA(pdvPwaOptions), pdvAbsolutePrecache],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  publicDir: 'pdv/public',
  build: {
    outDir: 'dist',
    emptyOutDir: false,
    assetsDir: 'pdv/assets',
    rollupOptions: { input: fileURLToPath(new URL('./pdv/index.html', import.meta.url)) },
  },
})
