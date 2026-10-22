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

  it('o registro é manual, só pelo /fab', () => {
    expect(pwaOptions.injectRegister).toBe(false)
  })
})