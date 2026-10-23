import type { ShopfloorDeviceRow } from '@/api/generated/model'

/** Aviso perto da revogação automática (30 dias sem uso, ADR 0026 §1): a partir de 20 dias a linha avisa. */
export const UNUSED_WARNING_DAYS = 20

export const STATUS: Record<string, { label: string; tone: 'ok' | 'warn' | 'crit' | 'info' | 'neutral' }> = {
  Active: { label: 'Ativo', tone: 'ok' },
  Revoked: { label: 'Revogado', tone: 'neutral' },
  Expired: { label: 'Vencido', tone: 'neutral' },
}

const REASON: Record<string, string> = {
  manual: 'revogado por',
  unused: 'sem uso por 30 dias',
  expired: '180 dias vencidos',
  secret_reused: 'segredo antigo reapresentado',
}

export function revokedText(row: Pick<ShopfloorDeviceRow, 'revokedReason' | 'revokedByName'>) {
  if (!row.revokedReason) return 'Revogado'
  return row.revokedReason === 'manual' ? `Revogado por ${row.revokedByName ?? 'um usuário'}` : 'Revogado: ' + (REASON[row.revokedReason] ?? row.revokedReason)
}
