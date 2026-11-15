import { afterEach, describe, expect, it, vi } from 'vitest'
import { ensureSession } from '@/auth/session-guard'
import { apiFetch, ApiError, renewSession } from './http'
import { getAccessToken, setAccessToken } from './session'

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const headersOf = (mock: ReturnType<typeof vi.fn>, call: number) => (mock.mock.calls[call][1] as RequestInit).headers as Headers

describe('mutator do Orval (FND-09c CA 3)', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('401 faz um único refresh e repete a requisição com o token novo', async () => {
    setAccessToken('velho')
    const fetchMock = vi.fn().mockResolvedValueOnce(json(401, {})).mockResolvedValueOnce(json(200, { accessToken: 'novo' })).mockResolvedValueOnce(json(200, { ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/v1/me')).resolves.toEqual({ ok: true })
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/auth/refresh')
    expect(headersOf(fetchMock, 2).get('Authorization')).toBe('Bearer novo')
    expect(getAccessToken()).toBe('novo')
  })

  it('refresh que falha leva ao login guardando a rota', async () => {
    const assign = vi.fn()
    vi.stubGlobal('location', { ...window.location, pathname: '/app/acessos', search: '?q=a', assign })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(401, {})).mockResolvedValueOnce(json(422, {})))
    await expect(apiFetch('/api/v1/me')).rejects.toBeInstanceOf(ApiError)
    expect(assign).toHaveBeenCalledWith('/entrar?volta=' + encodeURIComponent('/app/acessos?q=a'))
  })

  it('ao abrir a página, o guarda de rota e a retomada da sessão fazem um único POST /auth/refresh', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json(200, { accessToken: 'novo' }))
    vi.stubGlobal('fetch', fetchMock)
    const [guard, bridge] = await Promise.all([ensureSession(), renewSession()])
    expect([guard, bridge]).toEqual([true, true])
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(getAccessToken()).toBe('novo')
  })

  it('429 do limite de /auth tenta de novo uma vez antes de desistir da sessão; 422 (sessão expirada) não', async () => {
    vi.useFakeTimers()
    try {
      const limited = vi.fn().mockResolvedValueOnce(new Response('{}', { status: 429, headers: { 'Retry-After': '1' } })).mockResolvedValueOnce(json(200, { accessToken: 'novo' }))
      vi.stubGlobal('fetch', limited)
      const renewed = renewSession()
      await vi.advanceTimersByTimeAsync(1000)
      await expect(renewed).resolves.toBe(true)
      expect(limited).toHaveBeenCalledTimes(2)

      const expired = vi.fn().mockResolvedValue(json(422, { code: 'auth.session_expired' }))
      vi.stubGlobal('fetch', expired)
      await expect(renewSession()).resolves.toBe(false)
      expect(expired).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('ProblemDetails vira ApiError com code e campos; POST leva X-CSRF', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(json(422, { detail: 'Inválido.', code: 'validation', errors: { email: ['x'] } }))
    vi.stubGlobal('fetch', fetchMock)
    const error = await apiFetch('/api/v1/x', { method: 'POST' }).catch((e: unknown) => e)
    expect(error).toMatchObject({ status: 422, code: 'validation', message: 'Inválido.', fields: { email: ['x'] } })
    expect(headersOf(fetchMock, 0).get('X-CSRF')).toBe('1')
  })
})
