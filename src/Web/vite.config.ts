/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'
import { pwaOptions } from './pwa.config.ts'

export default defineConfig({
  // PWA só do /fab (PRD-03b): manifesto "BemAqui Fábrica" e service worker que guarda só o shell, nunca /api/*.
  plugins: [react(), tailwindcss(), VitePWA(pwaOptions)],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 5173,
    // A ajuda da /fab/entrar vem de Ajuda/fab-entrar.md (fora de src/Web), embutida no app: lá ainda não há sessão.
    fs: { allow: [fileURLToPath(new URL('.', import.meta.url)), fileURLToPath(new URL('../../Ajuda', import.meta.url))] },
    proxy: { '/api': 'http://localhost:5080', '/health': 'http://localhost:5080', '/hubs': { target: 'http://localhost:5080', ws: true } },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['src/test-setup.ts'],
    include: ['src/**/*.test.{ts,tsx}', 'scripts/**/*.test.ts'],
  },
})
