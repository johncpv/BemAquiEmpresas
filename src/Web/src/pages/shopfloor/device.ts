import type { DeviceTheme } from '@/api/generated/model'
import type { ThemePreference } from '@/theme/theme'

/** Valores da D5 (ADR 0026 §3): saída depois de 15 min sem toque, com aviso de 60 s. */
export const IDLE_WARNING_MS = 15 * 60_000
export const IDLE_GRACE_MS = 60_000

export type IdleState = { phase: 'active' } | { phase: 'warning'; secondsLeft: number } | { phase: 'expired' }

/**
 * Inatividade da sessão por PIN (CA 15): ativo até 15 min sem toque; depois, aviso "Você vai sair em 60 s." com a contagem; passou o
 * aviso, sai. O fim absoluto (12 h) também encerra, mesmo com toques.
 */
export function idleState(lastTouchMs: number, nowMs: number, sessionEndsAtMs?: number | null): IdleState {
  if (sessionEndsAtMs != null && nowMs >= sessionEndsAtMs) return { phase: 'expired' }
  const idle = nowMs - lastTouchMs
  if (idle < IDLE_WARNING_MS) return { phase: 'active' }
  const left = IDLE_WARNING_MS + IDLE_GRACE_MS - idle
  return left > 0 ? { phase: 'warning', secondsLeft: Math.ceil(left / 1000) } : { phase: 'expired' }
}

/** Tema do aparelho (servidor) para a preferência de tema do front. */
export function themeOf(theme: DeviceTheme | null | undefined): ThemePreference {
  return theme === 'Dark' ? 'dark' : theme === 'Light' ? 'light' : 'system'
}

export const DEVICE_THEMES: readonly { value: DeviceTheme; label: string }[] = [
  { value: 'System', label: 'Automático' },
  { value: 'Light', label: 'Claro' },
  { value: 'Dark', label: 'Escuro' },
]

/** Mensagens da entrada do tablet (Telas e estados da PRD-03b). */
export const FAB_ENTRY_TEXT = {
  notRegistered: 'Este aparelho não está registrado. Entre com e-mail e senha.',
  noUsers: 'Ninguém criou PIN neste aparelho ainda.',
  idle: 'Você vai sair em 60 s. Tocar para continuar.',
  pinDica: 'PIN é um número de 6 dígitos só seu, que vale só neste tablet. Ele abre o app do operador por até 12 horas.',
} as const

export const NOT_RELEASED = 'Funcionalidade não liberada nessa versão do sistema.'

/** PIN digitado no teclado grande: 6 números. */
export const PIN_LENGTH = 6
export const isPinComplete = (value: string) => /^\d{6}$/.test(value)

/** Ajuda da /fab/entrar vista neste aparelho (abre sozinha só na primeira visita, padrão 1). */
export const ENTRY_HELP_KEY = 'bq.fab.ajuda-entrar-vista'

export function entryHelpSeen(storage: Pick<Storage, 'getItem'> | undefined = safeStorage()): boolean {
  try {
    return storage?.getItem(ENTRY_HELP_KEY) === '1'
  } catch {
    return false
  }
}

export function markEntryHelpSeen(storage: Pick<Storage, 'setItem'> | undefined = safeStorage()) {
  try {
    storage?.setItem(ENTRY_HELP_KEY, '1')
  } catch {
    // Sem armazenamento (aba privada): a ajuda abre de novo na próxima visita.
  }
}

function safeStorage(): Storage | undefined {
  try {
    return typeof window === 'undefined' ? undefined : window.localStorage
  } catch {
    return undefined
  }
}

/** Seções "## Título" de um arquivo de ajuda (sem o cabeçalho), para a ajuda pública da /fab/entrar. */
export function helpSections(markdown: string): { title: string; body: string }[] {
  const text = markdown.replace(/^---[\s\S]*?---\s*/, '')
  return text.split(/^## /m).slice(1).map((chunk) => {
    const [title, ...rest] = chunk.split('\n')
    return { title: title.trim(), body: rest.join('\n').trim() }
  })
}