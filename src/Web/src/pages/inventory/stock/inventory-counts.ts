import type { InventoryCountingLine, InventoryCountStatus, InventoryDivergenceLine, StockAdjustmentStatus, UncountedPolicy } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Inventário no front (EST-05): rótulos, dicas e contas de tela; as regras de verdade estão no backend. */
export const COUNT_STATUS: Record<InventoryCountStatus, { label: string; tone: StatusTone }> = {
  Counting: { label: 'Em contagem', tone: 'warn' },
  Closed: { label: 'Fechado', tone: 'ok' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

export const UNCOUNTED_POLICY: Record<NonNullable<UncountedPolicy>, string> = { Zero: 'Considerar como zero', KeepSystem: 'Manter o saldo do sistema' }

export const ADJUSTMENT_TONE: Record<StockAdjustmentStatus, StatusTone> = { Pending: 'info', Approved: 'ok', Rejected: 'crit', Cancelled: 'neutral' }

export const INVENTORY_DICAS = {
  cega: 'Contagem cega: quem conta não vê o saldo do sistema, só o item e o lote. Assim o número digitado é o da prateleira, sem influência do que o sistema diz.',
  movimento: 'Movimento durante a contagem: entradas e saídas continuam liberadas enquanto o local é contado. A diferença desconta o que entrou ou saiu entre a abertura e a contagem de cada linha.',
  recontagem: 'Recontagem por outra pessoa: a linha marcada volta para a contagem e só quem não contou a primeira vez pode recontar. A recontagem vale na diferença; a anterior fica no histórico.',
}

/** Texto do teclado numérico ("12,5") para número; vazio ou inválido vira null. */
export function parseCount(text: string): number | null {
  const clean = text.trim().replace(/\./g, '').replace(',', '.')
  if (!clean || !/^\d+(\.\d{1,4})?$/.test(clean)) return null
  return Number(clean)
}

/** Número para o visor do teclado (vírgula decimal, sem milhar). */
export const toKeypad = (value: number | null | undefined) => (value === null || value === undefined ? '' : String(value).replace('.', ','))

/** Teclado numérico com vírgula: acrescenta o dígito ou a vírgula (uma só, até 4 casas). */
export function pressKey(current: string, key: string): string {
  if (key === ',') return current.includes(',') ? current : (current || '0') + ','
  const [, decimals] = current.split(',')
  if (decimals !== undefined && decimals.length >= 4) return current
  if (current === '0') return key
  return current + key
}

/** Ordem da contagem no celular: primeiro o que falta contar (recontagem antes), depois o já contado, sempre pela sequência. */
export function countingOrder(lines: readonly InventoryCountingLine[]): InventoryCountingLine[] {
  const rank = (l: InventoryCountingLine) => (l.countedQty === null ? (l.isRecount ? 0 : 1) : 2)
  return [...lines].sort((a, b) => rank(a) - rank(b) || a.sequence - b.sequence)
}

/** Rótulo da linha: código, nome e lote. */
export const lineLabel = (l: { itemCode: string; itemName: string; lotNumber: string | null }) => `${l.itemCode} — ${l.itemName}${l.lotNumber ? ` · lote ${l.lotNumber}` : ''}`

/** Sobras, faltas e líquido do fechamento (padrão 4), a partir das divergências. */
export function closeTotals(lines: readonly InventoryDivergenceLine[]): { surplus: number; shortage: number; net: number } {
  const surplus = lines.filter((l) => l.difference > 0).reduce((s, l) => s + l.value, 0)
  const shortage = lines.filter((l) => l.difference < 0).reduce((s, l) => s + l.value, 0)
  return { surplus, shortage, net: surplus - shortage }
}

/** "1 linha" / "3 linhas". */
export const linesLabel = (count: number) => (count === 1 ? '1 linha' : `${count} linhas`)
