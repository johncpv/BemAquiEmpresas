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
  go_live: 'virada para produção (registre de novo)',
  replaced: 'trocado por outro aparelho no caixa',
}

export function revokedText(row: Pick<ShopfloorDeviceRow, 'revokedReason' | 'revokedByName'>) {
  if (!row.revokedReason) return 'Revogado'
  return row.revokedReason === 'manual' ? `Revogado por ${row.revokedByName ?? 'um usuário'}` : 'Revogado: ' + (REASON[row.revokedReason] ?? row.revokedReason)
}

/** PDV-01: os caixas da loja aparecem na lista só no Comércio (recurso settings.pos_devices no servidor). */
export const POS_DEVICE_PROFILES = ['retail'] as const

export const KIND_LABEL: Record<string, string> = { Factory: 'Fábrica', Register: 'Caixa' }

/** Loja do aparelho de caixa, do rótulo "Loja Centro · Caixa 02"; null no aparelho da fábrica. */
export function storeOf(row: Pick<ShopfloorDeviceRow, 'registerLabel'>): string | null {
  const label = row.registerLabel ?? null
  return label ? label.split(' · ')[0] : null
}

/** Lojas dos aparelhos de caixa, para o filtro (sem repetir, em ordem). */
export function storesOf(rows: readonly Pick<ShopfloorDeviceRow, 'registerLabel'>[]): string[] {
  return [...new Set(rows.map(storeOf).filter((s): s is string => !!s))].sort((a, b) => a.localeCompare(b, 'pt-BR'))
}
