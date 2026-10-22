import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { isPinSession, sessionMethods } from '@/api/session'
import { entryHelpSeen, helpSections, IDLE_GRACE_MS, IDLE_WARNING_MS, idleState, isPinComplete, markEntryHelpSeen, themeOf } from './device'

const token = (payload: object) => ['x', btoa(JSON.stringify(payload)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_'), 'y'].join('.')

/** PRD-03b: saída por inatividade (CA 15), tema do aparelho (CA 11), PIN de 6 números (CA 12) e o amr da sessão (CA 15). */
describe('sessão do tablet', () => {
  it('15 min sem toque abrem o aviso de 60 s; depois, sai', () => {
    const t0 = 1_000_000
    expect(idleState(t0, t0 + IDLE_WARNING_MS - 1)).toEqual({ phase: 'active' })
    expect(idleState(t0, t0 + IDLE_WARNING_MS)).toEqual({ phase: 'warning', secondsLeft: 60 })
    expect(idleState(t0, t0 + IDLE_WARNING_MS + 59_500)).toEqual({ phase: 'warning', secondsLeft: 1 })
    expect(idleState(t0, t0 + IDLE_WARNING_MS + IDLE_GRACE_MS)).toEqual({ phase: 'expired' })
  })

  it('o fim absoluto das 12 h encerra mesmo com toques', () => {
    expect(idleState(5000, 5000, 4000)).toEqual({ phase: 'expired' })
    expect(idleState(5000, 5000, 6000)).toEqual({ phase: 'active' })
  })

  it('lê o amr do token: "pin" é sessão por PIN', () => {
    expect(sessionMethods(token({ amr: ['pin'] }))).toEqual(['pin'])
    expect(isPinSession(token({ amr: ['pin'] }))).toBe(true)
    expect(isPinSession(token({ amr: ['pwd', 'otp'] }))).toBe(false)
    expect(sessionMethods(token({ amr: 'pwd' }))).toEqual(['pwd'])
    expect(sessionMethods('não é jwt')).toEqual([])
    expect(sessionMethods(null)).toEqual([])
  })

  it('tema do aparelho e PIN de 6 números', () => {
    expect(themeOf('Dark')).toBe('dark')
    expect(themeOf('Light')).toBe('light')
    expect(themeOf('System')).toBe('system')
    expect(themeOf(undefined)).toBe('system')
    expect(isPinComplete('483920')).toBe(true)
    expect(isPinComplete('48392')).toBe(false)
    expect(isPinComplete('48392a')).toBe(false)
  })

  it('ajuda da entrada: abre só na primeira visita, e as seções vêm do arquivo da ajuda', () => {
    const storage = new Map<string, string>()
    const fake = { getItem: (k: string) => storage.get(k) ?? null, setItem: (k: string, v: string) => void storage.set(k, v) }
    expect(entryHelpSeen(fake)).toBe(false)
    markEntryHelpSeen(fake)
    expect(entryHelpSeen(fake)).toBe(true)

    const text = readFileSync(join(process.cwd(), '..', '..', 'Ajuda', 'fab-entrar.md'), 'utf8')
    const sections = helpSections(text).map((s) => s.title)
    expect(sections).toEqual(expect.arrayContaining(['Para que serve', 'Como usar', 'Perguntas frequentes']))
    expect(helpSections(text).find((s) => s.title === 'Como usar')?.body).toContain('PIN')
  })
})