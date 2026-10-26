import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/api/http'
import { TvConnectionStatus, TvHeader, TvNotice } from '@/components/tv/kit'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { awaitingTooLong, parseNetworks, revokeImpact, seenText } from '@/pages/kiosk/kiosk-screens'
import { clockText, stateOfEnd, stateOfError, takeFragmentToken, TV_TEXT } from './tv'

vi.mock('@microsoft/signalr', () => {
  class Builder {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() {
      return { start: () => Promise.reject(new Error('sem hub no teste')), stop: () => Promise.resolve(), on: () => undefined, onreconnecting: () => undefined, onreconnected: () => undefined, onclose: () => undefined, state: 'Disconnected' }
    }
  }
  return { HubConnectionBuilder: Builder, HubConnectionState: { Disconnected: 'Disconnected' }, LogLevel: { None: 6 } }
})

describe('superfície /tv (PNL-01a CA 5 e 17)', () => {
  it('o token sai do fragmento e o fragmento sai da barra antes de qualquer chamada', () => {
    const replaceState = vi.fn()
    const token = takeFragmentToken({ hash: '#abc123', pathname: '/tv' }, { replaceState, state: null })
    expect(token).toBe('abc123')
    expect(replaceState).toHaveBeenCalledWith(null, '', '/tv')
    expect(takeFragmentToken({ hash: '', pathname: '/tv' }, { replaceState, state: null })).toBeNull()
    expect(replaceState).toHaveBeenCalledTimes(1)
  })

  it('cada resposta do quiosque vira um estado da TV, nunca um erro técnico', () => {
    const problem = (code: string, data: Record<string, unknown> = {}) => new ApiError(401, { code, ...data })
    expect(stateOfError(problem('kiosk_revoked', { companyName: 'Konebras' }), 'x')).toEqual({ kind: 'revoked', company: 'Konebras' })
    expect(stateOfError(problem('kiosk_link_invalid'), 'x').kind).toBe('invalid')
    expect(stateOfError(problem('kiosk_session_invalid'), 'x').kind).toBe('reconnect')
    expect(stateOfError(problem('kiosk_network'), 'x').kind).toBe('network')
    expect(stateOfError(new ApiError(403, { code: 'module_not_released' }), 'x').kind).toBe('unavailable')
    expect(stateOfError(new ApiError(403, { code: 'tenant_suspended' }), 'x').kind).toBe('unavailable')
    expect(stateOfError(new Error('rede'), 'x').kind).toBe('reconnect')
    expect(stateOfEnd('revoked', 'Konebras')).toEqual({ kind: 'revoked', company: 'Konebras' })
    expect(stateOfEnd('unavailable', 'K').kind).toBe('unavailable')
    expect(stateOfEnd('session_ended', 'K').kind).toBe('reconnect')
  })
})

describe('kit TV base (PNL-01a CA 18)', () => {
  it('cabeçalho com a linha, o relógio de 48 px e "Ao vivo"', () => {
    const now = new Date('2026-10-25T13:05:00Z')
    render(<TvHeader title="Linha 1" status="live" updatedAt={now} now={now} />)
    expect(screen.getByRole('heading', { name: 'Linha 1' }).className).toContain('--tv-title')
    expect(screen.getByText(clockText(now)).className).toContain('--tv-count')
    expect(screen.getByText(TV_TEXT.live)).toBeTruthy()
    expect(screen.queryByText(/Atualizado às/)).toBeNull()
  })

  it('sem conexão: "Reconectando…" e "Atualizado às {hh:mm}"', () => {
    const at = new Date('2026-10-25T13:05:00Z')
    render(<TvConnectionStatus status="offline" updatedAt={at} />)
    expect(screen.getByText(TV_TEXT.reconnecting)).toBeTruthy()
    expect(screen.getByText(`Atualizado às ${clockText(at)}`)).toBeTruthy()
    expect(clockText(at)).toBe('10:05')
  })

  it('StageIndicator size="tv" usa a letra de leitura a distância', () => {
    const { container } = render(<StageIndicator stages={['Corte', 'Solda', 'Pintura']} current={1} size="tv" />)
    const root = container.querySelector('[data-size="tv"]')!
    expect(root.getAttribute('aria-label')).toBe('Etapa Solda, 2 de 3')
    expect(root.firstElementChild!.className).toContain('--tv-line')
    expect(root.querySelectorAll('[data-state]')).toHaveLength(3)
  })

  it('aviso de tela inteira em letra grande', () => {
    render(<TvNotice title={TV_TEXT.unavailable} />)
    expect(screen.getByRole('alert').textContent).toContain('Painel indisponível nesta versão do sistema.')
  })
})

describe('gestão das telas (PNL-01a CA 2)', () => {
  it('último acesso, aguardando há mais de 1 dia, redes e impacto da revogação', () => {
    const now = new Date('2026-10-25T13:00:00Z')
    expect(seenText(null, now)).toBe('Nunca conectada')
    expect(seenText('2026-10-25T12:53:00Z', now)).toBe('Visto há 7 min')
    expect(seenText('2026-10-25T12:59:40Z', now)).toBe('Visto agora')
    expect(seenText('2026-10-24T12:00:00Z', now)).toMatch(/^Visto em /)
    expect(awaitingTooLong({ status: 'AwaitingConnection', createdAt: '2026-10-23T12:00:00Z' }, now)).toBe(true)
    expect(awaitingTooLong({ status: 'Active', createdAt: '2026-10-23T12:00:00Z' }, now)).toBe(false)
    expect(parseNetworks(' 192.168.10.0/24\n\n10.0.0.5, ')).toEqual(['192.168.10.0/24', '10.0.0.5'])
    expect(revokeImpact('TV 1')).toBe('A TV TV 1 desliga em até 1 minuto. Para voltar a usar, crie uma tela nova.')
  })
})

describe('TvPage (PNL-01a CA 5, 6 e 14)', () => {
  const calls: { url: string; method: string; body?: string }[] = []
  let responses: Record<string, () => Response>

  beforeEach(() => {
    calls.length = 0
    vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
      calls.push({ url, method: init.method ?? 'GET', body: init.body as string | undefined })
      return (responses[`${init.method ?? 'GET'} ${url}`] ?? (() => new Response('{}', { status: 404 })))()
    }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.history.replaceState(null, '', '/')
  })

  const json = (body: unknown, status = 200) => () => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
  const renderTv = async () => {
    const { TvPage } = await import('./TvPage')
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(<QueryClientProvider client={client}><TvPage /></QueryClientProvider>)
  }

  it('troca o token do fragmento pelo cookie, apaga o fragmento e mostra o painel', async () => {
    window.history.replaceState(null, '', '/tv#token-da-tela')
    responses = {
      'POST /api/v1/kiosk/session': json({ screenName: 'TV Montagem' }),
      'GET /api/v1/kiosk/screen': json({ name: 'TV Montagem', line: 'Linha 1', views: ['board'], rotationSeconds: 20, companyName: 'Konebras', serverNow: '2026-10-25T13:00:00Z' }),
    }
    await renderTv()

    expect(await screen.findByRole('heading', { name: 'Linha 1' })).toBeTruthy()
    expect(window.location.hash).toBe('')
    expect(window.location.pathname).toBe('/tv')
    const post = calls.find((c) => c.method === 'POST')!
    expect(post.url).toBe('/api/v1/kiosk/session')
    expect(JSON.parse(post.body!)).toEqual({ token: 'token-da-tela' })
    expect(calls.every((c) => !c.url.includes('token-da-tela')), 'o token nunca vai na URL').toBe(true)
  })

  it('painel conectado: cabeçalho com as contagens, o quadro da linha e nenhuma chamada fora da API do quiosque (PNL-01b)', async () => {
    window.history.replaceState(null, '', '/tv')
    const card = {
      id: 'o1', number: 'OS-2026-0001', projectNumber: 'PRJ-2026-0007', projectName: 'Residencial Aurora', columnKey: 'M', stepCode: 'M', stepName: 'Montagem', operator: 'João S.',
      pausedReason: null, stepStartedAt: null, appliedHours: 1, standardHours: 2, plannedEnd: null, risk: 'Late', delayDays: 2,
    }
    responses = {
      'GET /api/v1/kiosk/screen': json({ name: 'TV Montagem', line: 'Linha 1', views: ['board', 'late'], rotationSeconds: 20, companyName: 'Konebras', serverNow: '2026-10-25T13:00:00Z' }),
      'GET /api/v1/kiosk/board': json({ line: 'Linha 1', inProgressCount: 1, lateCount: 1, atRiskCount: 0, columns: [{ key: 'M', code: 'M', name: 'Montagem', count: 1 }], cards: [card], serverNow: '2026-10-25T13:00:00Z' }),
      'GET /api/v1/kiosk/delays': json({ line: 'Linha 1', rows: [], total: 0 }),
    }
    await renderTv()

    expect(await screen.findByRole('heading', { name: 'Linha 1 — 1 OS em andamento, 1 atrasada' })).toBeTruthy()
    expect(await screen.findByText('João S.')).toBeTruthy()
    expect(screen.getByText('ATRASADA')).toBeTruthy()
    expect(await screen.findByLabelText('Visão 1 de 1: Quadro por etapa')).toBeTruthy()
    expect(calls.every((c) => c.url.startsWith('/api/v1/kiosk/')), 'a TV só chama a API do quiosque').toBe(true)
    expect(calls.some((c) => c.url === '/api/v1/kiosk/queue'), 'visão fora da tela não é pedida').toBe(false)
  })

  it('segundo uso do link: "Link inválido ou desligado." sem chamar o painel', async () => {
    window.history.replaceState(null, '', '/tv#ja-usado')
    responses = { 'POST /api/v1/kiosk/session': json({ code: 'kiosk_link_invalid', detail: 'Link inválido ou desligado.' }, 401) }
    await renderTv()

    expect(await screen.findByText(TV_TEXT.invalid)).toBeTruthy()
    await waitFor(() => expect(calls.some((c) => c.url === '/api/v1/kiosk/screen')).toBe(false))
  })

  it('tela revogada: "Esta tela foi desligada por {empresa}."', async () => {
    window.history.replaceState(null, '', '/tv')
    responses = { 'GET /api/v1/kiosk/screen': json({ code: 'kiosk_revoked', companyName: 'Konebras' }, 401) }
    await renderTv()

    expect(await screen.findByText('Esta tela foi desligada por Konebras.')).toBeTruthy()
  })
})
