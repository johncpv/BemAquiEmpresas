import type { KioskScreenRow } from '@/api/generated/model'
import { formatDateTime } from '@/lib/format'

/** Situação da tela (PNL-01a CA 2). */
export const STATUS: Record<string, { label: string; tone: 'ok' | 'warn' | 'crit' | 'info' | 'neutral' }> = {
  AwaitingConnection: { label: 'Aguardando conexão', tone: 'info' },
  Active: { label: 'Ativa', tone: 'ok' },
  Revoked: { label: 'Revogada', tone: 'neutral' },
}

export const VIEW_LABELS: Record<string, string> = { board: 'Quadro por etapa', queue: 'Fila e previsões', late: 'Atrasos e riscos' }

export const DEFAULT_ROTATION = 20
export const MAX_NETWORKS = 5

/** "Visto há {n} min" (até 1 h), a data depois disso, ou "Nunca conectada". */
export function seenText(lastSeenAt: string | null | undefined, now = new Date()): string {
  if (!lastSeenAt) return 'Nunca conectada'
  const minutes = Math.max(0, Math.floor((now.getTime() - new Date(lastSeenAt).getTime()) / 60_000))
  if (minutes < 1) return 'Visto agora'
  return minutes < 60 ? `Visto há ${minutes} min` : `Visto em ${formatDateTime(lastSeenAt)}`
}

/** Propositivo: "Aguardando conexão" há mais de 1 dia mostra a dica "Abra o link na TV". */
export function awaitingTooLong(row: Pick<KioskScreenRow, 'status' | 'createdAt'>, now = new Date()): boolean {
  return row.status === 'AwaitingConnection' && now.getTime() - new Date(row.createdAt).getTime() > 24 * 60 * 60 * 1000
}

export const viewsText = (views: readonly string[]) => views.map((v) => VIEW_LABELS[v] ?? v).join(', ')

/** Redes do formulário: uma por linha (ou separadas por vírgula), sem vazias. */
export const parseNetworks = (text: string) => text.split(/[\n,;]/).map((n) => n.trim()).filter(Boolean)

/** Impacto da revogação (padrão 7 das Fases 4 a 8). */
export const revokeImpact = (name: string) => `A TV ${name} desliga em até 1 minuto. Para voltar a usar, crie uma tela nova.`

export const LINK_HINT = 'Abra este link no navegador da TV. Ele vale para uma conexão só e não aparece de novo.'
