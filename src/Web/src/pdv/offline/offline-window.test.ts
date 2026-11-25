import { describe, expect, it } from 'vitest'
import { statusView } from '@/pdv/status-bar'
import { advanceClock, CLOCK_BEHIND_MESSAGE, deadlineText, expiredMessage, openBlockedMessage, warnMessage, windowState } from './offline-window'

// Hora local do aparelho (o CI roda em UTC): o prazo aparece no relógio da loja.
const local = (day: number, h: number, m = 0) => new Date(2026, 10, day, h, m)

describe('janela sem sincronizar com piso de horário do servidor (OFF-03 CA 9, 10 e 14)', () => {
  const synced = { serverAt: local(20, 14).toISOString(), highWater: local(20, 14).toISOString() }

  it('conta do último horário do servidor: ok, alerta a 80% com o prazo e vencida bloqueia o turno novo', () => {
    expect(windowState(synced, 168, local(22, 9))).toMatchObject({ kind: 'ok' })
    const warn = windowState(synced, 168, local(26, 10))
    expect(warn.kind).toBe('warn')
    if (warn.kind === 'warn') expect(warnMessage(warn.deadline, local(26, 10))).toBe('Conecte à internet até amanhã às 14h.')
    const expired = windowState(synced, 168, local(27, 15))
    expect(expired).toMatchObject({ kind: 'expired', days: 7 })
    expect(openBlockedMessage(expired)).toBe('Este caixa está há 7 dias sem enviar as vendas. Conecte à internet para abrir o caixa.')
    expect(expiredMessage(1)).toContain('há 1 dia sem')
    expect(windowState({ serverAt: null, highWater: null }, 168, local(22, 9))).toEqual({ kind: 'unknown' })
  })

  it('relógio atrasado de propósito não estende a janela: vale o piso, registra e não abre turno novo', () => {
    const seen = advanceClock(synced, { deviceNow: local(26, 10) })
    expect(seen.highWater).toBe(local(26, 10).toISOString())
    const back = windowState(seen, 168, local(21, 10))
    expect(back.kind).toBe('clock-behind')
    expect(openBlockedMessage(back)).toBe(CLOCK_BEHIND_MESSAGE)
    // O relógio atrasado não rebaixa o maior horário visto; o horário novo do servidor avança o piso.
    expect(advanceClock(seen, { deviceNow: local(21, 10) }).highWater).toBe(seen.highWater)
    expect(advanceClock(seen, { serverAt: local(26, 11).toISOString() }).serverAt).toBe(local(26, 11).toISOString())
  })

  it('a janela menor que um dia vale em horas', () => {
    expect(windowState(synced, 4, local(20, 17, 30)).kind).toBe('warn')
    expect(windowState(synced, 4, local(20, 18, 1)).kind).toBe('expired')
    expect(deadlineText(local(20, 18, 30), local(20, 9))).toBe('hoje às 18h30')
    expect(deadlineText(local(24, 8), local(20, 9))).toBe('24/11 às 8h')
  })

  it('a faixa de estado fica em alerta a 80% e crítica com a janela vencida (CA 10)', () => {
    const base = { online: false, pendingCount: 0, oldestPendingAt: null, lastSyncAt: local(20, 14).toISOString(), offlineWindowDays: 7, now: local(26, 10) }
    const warn = statusView({ ...base, syncWindow: windowState(synced, 168, local(26, 10)) })
    expect(warn.tone).toBe('warn')
    expect(warn.details[0]).toBe('Conecte à internet até amanhã às 14h.')
    const expired = statusView({ ...base, now: local(27, 15), syncWindow: windowState(synced, 168, local(27, 15)) })
    expect(expired.tone).toBe('crit')
    expect(expired.details[0]).toContain('sem enviar as vendas')
    expect(statusView({ ...base, online: true, syncWindow: { kind: 'ok', deadline: local(27, 14) } }).tone).toBe('ok')
  })
})
