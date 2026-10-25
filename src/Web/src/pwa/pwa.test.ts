import { describe, expect, it } from 'vitest'
import { FAB_SCOPE, pwaOptions } from '../../pwa.config'

/** PRD-03b CA 1 e 2 (ADR 0026 §4): manifesto instalável do /fab e service worker que nunca guarda /api/*. */
describe('PWA do app do operador', () => {
  it('manifesto "BemAqui Fábrica", standalone, start_url e escopo /fab, ícones 192 e 512', () => {
    const manifest = pwaOptions.manifest
    expect(manifest && typeof manifest === 'object').toBe(true)
    if (!manifest) return
    expect(manifest.name).toBe('BemAqui Fábrica')
    expect(manifest.display).toBe('standalone')
    expect(manifest.start_url).toBe(FAB_SCOPE)
    expect(manifest.scope).toBe(FAB_SCOPE)
    expect(manifest.icons?.map((i) => i.sizes)).toEqual(expect.arrayContaining(['192x192', '512x512']))
    expect(pwaOptions.scope).toBe(FAB_SCOPE)
  })

  it('service worker sem cache em tempo de execução e sem /api, /hubs nem /app', () => {
    const workbox = pwaOptions.workbox!
    expect(workbox.runtimeCaching ?? []).toEqual([])
    expect(workbox.globPatterns?.some((p) => p.includes('api'))).toBe(false)
    const deny = workbox.navigateFallbackDenylist ?? []
    for (const path of ['/api/v1/shopfloor/queue', '/api/v1/auth/pin', '/hubs/production', '/app', '/app/producao/os']) {
      expect(deny.some((r) => r.test(path)), path).toBe(true)
    }

    const allow = workbox.navigateFallbackAllowlist ?? []
    expect(allow.some((r) => r.test('/fab/entrar'))).toBe(true)
    expect(allow.some((r) => r.test('/app'))).toBe(false)
    expect(allow.some((r) => r.test('/api/v1/me'))).toBe(false)
  })

  it('PNL-01a CA 11: o service worker não guarda /tv nem /api/v1/kiosk', () => {
    const workbox = pwaOptions.workbox!
    const deny = workbox.navigateFallbackDenylist ?? []
    const allow = workbox.navigateFallbackAllowlist ?? []
    for (const path of ['/tv', '/tv/', '/tv/abc', '/api/v1/kiosk/screen', '/api/v1/kiosk/session', '/hubs/kiosk']) {
      expect(deny.some((r) => r.test(path)), path).toBe(true)
      expect(allow.some((r) => r.test(path)), path).toBe(false)
    }

    expect(workbox.runtimeCaching ?? []).toEqual([])
    expect(workbox.globPatterns?.some((p) => p.includes('tv') || p.includes('kiosk'))).toBe(false)
    expect(pwaOptions.scope).toBe(FAB_SCOPE)
  })

  it('o registro é manual, só pelo /fab', () => {
    expect(pwaOptions.injectRegister).toBe(false)
  })
})