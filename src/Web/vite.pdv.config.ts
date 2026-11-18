import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'
import { pdvPwaOptions } from './pwa.pdv.config.ts'

/**
 * Build do app do caixa (OFF-01 CA 1): entrada própria (`pdv/index.html` → `src/pdv-app/main.tsx`), saída em `dist/pdv/` ao lado do build
 * principal e PWA próprio (`pwa.pdv.config.ts`). Roda depois do `vite build` principal (ver `pnpm build`), sem apagar o que ele gerou.
 * O `public/` do backoffice não entra: os ícones do caixa vêm de `pdv/public/pdv/`. Em desenvolvimento, o próprio `pnpm dev` já serve
 * `/pdv/` (o Vite acha `pdv/index.html`), sem service worker.
 */
export default defineConfig({
  plugins: [react(), tailwindcss(), VitePWA(pdvPwaOptions)],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  publicDir: 'pdv/public',
  build: {
    outDir: 'dist',
    emptyOutDir: false,
    assetsDir: 'pdv/assets',
    rollupOptions: { input: fileURLToPath(new URL('./pdv/index.html', import.meta.url)) },
  },
})
