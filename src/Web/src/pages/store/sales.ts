import type { StatusTone } from '@/components/ui/status-chip'

/** Mesmos estados da API (SaleListState) e os rótulos do filtro. */
export const STATE_OPTIONS = [
  { value: 'Completed', label: 'Concluídas' },
  { value: 'Cancelled', label: 'Canceladas' },
  { value: 'Returned', label: 'Devolvidas' },
  { value: 'StockShortfall', label: 'Estoque: falta de saldo' },
  { value: 'Divergence', label: 'Com divergência' },
  { value: 'Offline', label: 'Feitas sem internet' },
] as const

export type SaleState = (typeof STATE_OPTIONS)[number]['value']

export function isSaleState(value: unknown): value is SaleState {
  return STATE_OPTIONS.some((o) => o.value === value)
}

const STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Completed: { label: 'Concluída', tone: 'ok' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
  PartiallyReturned: { label: 'Devolvida em parte', tone: 'info' },
  Returned: { label: 'Devolvida', tone: 'info' },
  PartiallyPaid: { label: 'Paga em parte', tone: 'warn' },
  Open: { label: 'Aberta', tone: 'warn' },
}

export interface SaleChipSource {
  status: string
  stockStatus: string
  hasDivergence: boolean
  offline: boolean
}

/** Chips da venda na ordem da tela (PDV-07 CA 1): situação, "Estoque: falta de saldo", "Com divergência" e "Feita sem internet". */
export function saleChips(s: SaleChipSource): { label: string; tone: StatusTone }[] {
  const chips = [STATUS[s.status] ?? { label: s.status, tone: 'neutral' as StatusTone }]
  if (s.stockStatus === 'Shortfall') chips.push({ label: 'Estoque: falta de saldo', tone: 'warn' })
  if (s.hasDivergence) chips.push({ label: 'Com divergência', tone: 'crit' })
  if (s.offline) chips.push({ label: 'Feita sem internet', tone: 'info' })
  return chips
}

/** Tom do passo da trilha (vem da API: ok, warn, crit, info). */
export function trailTone(tone: string): StatusTone {
  return tone === 'ok' || tone === 'warn' || tone === 'crit' ? tone : 'info'
}

export const TRAIL_KIND: Record<string, string> = {
  device: 'Caixa',
  approval: 'Supervisor',
  sync: 'Sincronização',
  stock: 'Estoque',
  finance: 'Financeiro',
  print: 'Impressão',
  pending: 'Pendência',
  sale: 'Venda',
}

/** "Há 2 h 05 min" para o card dos caixas abertos. */
export function openFor(minutes: number): string {
  if (minutes < 60) return `há ${minutes} min`
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return m === 0 ? `há ${h} h` : `há ${h} h ${String(m).padStart(2, '0')} min`
}

/** Variação em relação ao mesmo dia da semana passada, com o sinal ("+12,5%", "−3%"); sem base, nulo. */
export function changeText(pct: number | null | undefined): string | null {
  if (pct == null) return null
  const abs = Math.abs(pct).toLocaleString('pt-BR', { maximumFractionDigits: 1 })
  return pct > 0 ? `+${abs}%` : pct < 0 ? `−${abs}%` : '0%'
}

export const DICAS = {
  divergencia: 'O valor pago no caixa não bateu com o total da venda. A venda entrou assim mesmo e a diferença ficou registrada; confira nas Pendências do PDV.',
  semInternet: 'A venda foi feita com o caixa sem internet e chegou ao servidor mais de 5 minutos depois. Isso é normal; o que importa é ela ter chegado.',
  trilha: 'Tudo o que aconteceu com a venda, em ordem: os passos no caixa, as aprovações do supervisor, quando chegou ao servidor, o estoque, o financeiro e as reimpressões.',
  cancelamentos: 'O caixa ganha o alerta quando o valor das vendas canceladas no turno passa do limite da loja (padrão 3% das vendas do turno). O limite fica em Loja → Lojas.',
} as const
