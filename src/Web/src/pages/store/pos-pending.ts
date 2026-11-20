import type { SyncDivergenceKind, SyncResolution as NullableResolution } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

export type SyncResolution = NonNullable<NullableResolution>

/** Tipos de divergência da sincronização do caixa (OFF-02), na ordem dos chips. */
export const DIVERGENCE_KINDS: SyncDivergenceKind[] = [
  'Validation', 'FiscalDocument', 'BrokenSequence', 'ReplacedDevice', 'CashSession', 'Payment', 'PriceOutdated', 'DeviceClock', 'SchemaVersion',
]

export const KIND_LABEL: Record<SyncDivergenceKind, string> = {
  PriceOutdated: 'Preço desatualizado',
  DeviceClock: 'Relógio do aparelho',
  BrokenSequence: 'Sequência quebrada',
  ReplacedDevice: 'Aparelho substituído',
  SchemaVersion: 'Versão do aparelho',
  Validation: 'Recusado na conferência',
  Payment: 'Pagamento',
  CashSession: 'Sessão de caixa',
  FiscalDocument: 'Documento fiscal',
}

/** O que segura venda (quarentena por regra) é crítico; o que entrou com diferença é alerta; o resto, informativo. */
export const KIND_TONE: Record<SyncDivergenceKind, StatusTone> = {
  Validation: 'crit',
  FiscalDocument: 'crit',
  BrokenSequence: 'crit',
  ReplacedDevice: 'crit',
  CashSession: 'warn',
  Payment: 'warn',
  PriceOutdated: 'info',
  DeviceClock: 'info',
  SchemaVersion: 'warn',
}

export const RESOLUTION_LABEL: Record<SyncResolution, string> = {
  AcceptAsIs: 'Aceitar como está',
  ReleaseQuarantine: 'Liberar da quarentena',
  KeepOut: 'Manter fora',
}

/** CA 15: o evento que já entrou só se aceita como está; o da quarentena se libera (tem efeito) ou fica fora (só no registro). */
export function resolutionsFor(quarantined: boolean): SyncResolution[] {
  return quarantined ? ['ReleaseQuarantine', 'KeepOut'] : ['AcceptAsIs']
}

/** Hoje em Brasília, no formato da API (aaaa-mm-dd). */
export function todayInBrazil(now = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now)
}
