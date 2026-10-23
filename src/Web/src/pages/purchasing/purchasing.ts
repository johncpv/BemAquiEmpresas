import type { PurchaseRequestOrigin, PurchaseRequestStatus } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Solicitações de compra no front (CMP-01): rótulos, dicas e a urgência da necessidade; as regras de verdade estão no backend. */
export const ORIGIN: Record<PurchaseRequestOrigin, string> = {
  Manual: 'Manual',
  BomShortage: 'Falta da BOM',
  MinStock: 'Estoque mínimo',
}

export const STATUS: Record<PurchaseRequestStatus, { label: string; tone: StatusTone }> = {
  Open: { label: 'Aberta', tone: 'info' },
  Quoting: { label: 'Em cotação', tone: 'warn' },
  Ordered: { label: 'Pedida', tone: 'ok' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
}

export const PURCHASE_DICAS = {
  minimo: 'Estoque mínimo é a quantidade do item que nunca deve faltar. Todo dia às 6h o sistema confere o disponível mais o que já está em compra e, abaixo do mínimo, pede até o mínimo.',
  faltaBom: 'Falta da BOM é o que a BOM liberada de uma OS pede e o estoque não tem livre. A solicitação acompanha a falta sozinha: cresce, diminui ou é cancelada quando o material chega.',
  necessidade: 'Necessidade é a data em que o material precisa estar na fábrica. Na falta da BOM é a data prometida do projeto menos o prazo padrão de compra; sem data prometida, hoje mais o prazo.',
  implantacao: 'Linha calculada na implantação das compras, a partir das faltas que já existiam antes desta versão.',
  editada: 'Linha automática alterada à mão: o sistema não muda mais a quantidade nem a data dela.',
}

export const SHOW_OPTIONS = [
  { value: 'aguardando-voce', label: 'Aguardando você' },
  { value: 'abertas', label: 'Abertas e em cotação' },
  { value: 'todas', label: 'Todas' },
]

export const ORIGIN_OPTIONS = (Object.keys(ORIGIN) as PurchaseRequestOrigin[]).map((value) => ({ value, label: ORIGIN[value] }))
export const STATUS_OPTIONS = (Object.keys(STATUS) as PurchaseRequestStatus[]).map((value) => ({ value, label: STATUS[value].label }))

/** Hoje em São Paulo, "aaaa-mm-dd". */
export const todayIso = (now = new Date()) => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(now)

/** Dias de hoje até a data (negativo = já passou). */
export function daysUntil(date: string, today = todayIso()): number {
  const [y1, m1, d1] = today.split('-').map(Number)
  const [y2, m2, d2] = date.split('-').map(Number)
  return Math.round((Date.UTC(y2, m2 - 1, d2) - Date.UTC(y1, m1 - 1, d1)) / 86_400_000)
}

/**
 * Chip da necessidade (CA 12, padrão 2): crítico "Atrasada {n} d" quando já passou, alerta "Precisa em {n} dias" quando faltam até 7
 * dias ("Precisa hoje" no dia); depois disso, nada.
 */
export function urgency(neededBy: string | null | undefined, today = todayIso()): { tone: StatusTone; label: string } | null {
  if (!neededBy) return null
  const days = daysUntil(neededBy, today)
  if (days < 0) return { tone: 'crit', label: `Atrasada ${-days} d` }
  if (days === 0) return { tone: 'warn', label: 'Precisa hoje' }
  if (days <= 7) return { tone: 'warn', label: `Precisa em ${days} ${days === 1 ? 'dia' : 'dias'}` }
  return null
}

/** Faixa lateral da linha da lista (padrão 2: sempre com o texto do chip junto). */
export function rowAccent(neededBy: string | null | undefined, today = todayIso()): string | undefined {
  const u = urgency(neededBy, today)
  return u?.tone === 'crit' ? 'border-l-4 border-l-crit' : u ? 'border-l-4 border-l-warn' : undefined
}

/** Destino da SC (R-CMP-6): o projeto ou "Estoque geral". */
export const destination = (projectNumber: string | null | undefined, projectName?: string | null) =>
  projectNumber ? `${projectNumber}${projectName ? ` — ${projectName}` : ''}` : 'Estoque geral'
