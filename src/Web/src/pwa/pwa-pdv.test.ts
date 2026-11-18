import { describe, expect, it } from 'vitest'
import { FAB_SCOPE, pwaOptions } from '../../pwa.config'
import { PDV_SCOPE, pdvPwaOptions } from '../../pwa.pdv.config'
import { PDV_SCOPE as APP_SCOPE, PDV_SW_URL } from '../pdv/app-update'

/**
 * OFF-01 CA 1: o /pdv tem manifesto e service worker próprios, de escopo /pdv/, e o /fab e a /tv não mudam. O manifesto do /fab fica copiado
 * aqui de propósito: qualquer mudança nele reprova este teste (o build confere de novo os arquivos gerados, em scripts/check-pwa-build.mjs).
 */
const FAB_MANIFEST = {
  id: '/fab',
  name: 'BemAqui Fábrica',
  short_name: 'Fábrica',
  description: 'App do operador: fila de etapas e apontamento no chão de fábrica.',
  lang: 'pt-BR',
  start_url: '/fab',
  scope: '/fab',
  display: 'standalone',
  orientation: 'any',
  background_color: '#0f1419',
  theme_color: '#0f1419',
  icons: [
    { src: '/fab-icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
    { src: '/fab-icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
    { src: '/fab-icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
  ],
}

describe('PWA do caixa (/pdv) ao lado do /fab', () => {
  it('o manifesto e o service worker do /fab não mudaram', () => {
    expect(pwaOptions.manifest).toEqual(FAB_MANIFEST)
    expect(pwaOptions.scope).toBe(FAB_SCOPE)
    expect(pwaOptions.filename).toBe('sw.js')
    expect(pwaOptions.workbox?.skipWaiting).toBe(true)
    expect(pwaOptions.workbox?.globPatterns).toEqual(['index.html', 'assets/*.{js,css}', 'assets/*.woff2', 'favicon.svg', 'fab-icon-*.png'])
  })

  it('manifesto próprio "BemAqui Caixa", standalone, start_url e escopo /pdv/, ícones 192 e 512', () => {
    const m = pdvPwaOptions.manifest
    if (!m) throw new Error('sem manifesto')
    expect(m).toMatchObject({ id: '/pdv/', name: 'BemAqui Caixa', display: 'standalone', start_url: PDV_SCOPE, scope: PDV_SCOPE })
    expect(m.icons?.map((i) => i.sizes)).toEqual(expect.arrayContaining(['192x192', '512x512']))
    expect(m.icons?.every((i) => i.src.startsWith('/pdv/'))).toBe(true)
    expect(m.id).not.toBe(pwaOptions.manifest && pwaOptions.manifest.id)
  })

  it('service worker próprio em /pdv/sw.js com escopo /pdv/, registrado pelo app com o mesmo caminho', () => {
    expect(pdvPwaOptions.filename).toBe('pdv/sw.js')
    expect(pdvPwaOptions.manifestFilename).toBe('pdv/manifest.webmanifest')
    expect(pdvPwaOptions.scope).toBe(PDV_SCOPE)
    expect(PDV_SW_URL).toBe('/pdv/sw.js')
    expect(APP_SCOPE).toBe(PDV_SCOPE)
    expect(pdvPwaOptions.injectRegister).toBe(false)
  })

  it('guarda o app do caixa para abrir sem rede, nunca /api, e não abraça /fab, /app nem /tv', () => {
    const w = pdvPwaOptions.workbox!
    expect(w.runtimeCaching ?? []).toEqual([])
    expect(w.globPatterns?.every((p) => p.startsWith('pdv/'))).toBe(true)
    expect(w.navigateFallback).toBe('/pdv/index.html')
    const allow = w.navigateFallbackAllowlist ?? []
    const deny = w.navigateFallbackDenylist ?? []
    expect(allow.some((r) => r.test('/pdv/'))).toBe(true)
    for (const path of ['/api/v1/pos/sync/snapshot', '/hubs/x', '/fab', '/fab/entrar', '/app', '/tv', '/tv/abc']) {
      expect(allow.some((r) => r.test(path)) && !deny.some((r) => r.test(path)), path).toBe(false)
    }
  })

  it('versão nova espera (sem skipWaiting): quem decide é a regra da sessão de caixa', () => {
    expect(pdvPwaOptions.workbox?.skipWaiting).toBe(false)
    expect(pdvPwaOptions.registerType).toBe('prompt')
  })
})
