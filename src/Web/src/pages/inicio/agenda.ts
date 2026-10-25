import type { AgendaFailure, AgendaItem, AgendaTier, AgendaView } from '@/api/generated/model'

/** Os três níveis da pauta (DSH-04 CA 4), na ordem em que aparecem. */
export const TIERS: readonly { tier: AgendaTier; label: string; tip?: string }[] = [
  {
    tier: 'Unblocks',
    label: 'Destrava outra pessoa ou dinheiro',
    tip: 'Aprovações, decisões e pagamentos que alguém está esperando para seguir com o trabalho ou que mexem no caixa. Por isso vêm primeiro.',
  },
  { tier: 'Overdue', label: 'Já passou do prazo' },
  { tier: 'DueSoon', label: 'Vence em breve' },
]

export const SELF_INSPECTION = 'Autoinspeção'

export const SELF_INSPECTION_TIP =
  'Você fez uma etapa desta tentativa e não há outro aprovador de qualidade disponível. Pode decidir, mas a decisão fica marcada como Autoinspeção e o Proprietário é avisado.'

export interface TierGroup {
  tier: AgendaTier
  label: string
  tip?: string
  items: AgendaItem[]
  failures: AgendaFailure[]
}

/** Resposta sem as listas (versão antiga da API ou corpo vazio) vira pauta vazia, nunca erro de tela. */
export function normalize(view: Partial<AgendaView> | undefined): AgendaView {
  return { items: view?.items ?? [], failures: view?.failures ?? [], total: view?.total ?? 0 }
}

/** Agrupa por nível mantendo a ordem do servidor (já ordenada por nível, vencimento e valor); nível sem nada não aparece. */
export function groupByTier(view: AgendaView): TierGroup[] {
  return TIERS.map((t) => ({
    ...t,
    items: view.items.filter((i) => i.tier === t.tier),
    failures: view.failures.filter((f) => f.tier === t.tier),
  })).filter((g) => g.items.length > 0 || g.failures.length > 0)
}

/** Faixa lateral do cartão (Telas e estados): nível 1 âmbar, nível 2 vermelha, nível 3 sem faixa. */
export function stripe(tier: AgendaTier): 'warn' | 'crit' | null {
  return tier === 'Unblocks' ? 'warn' : tier === 'Overdue' ? 'crit' : null
}

/** Nada pendente e nada falhou (CA 9). */
export function isEmpty(view: AgendaView): boolean {
  return view.items.length === 0 && view.failures.length === 0
}

/** Link relativo do app para o roteador: caminho e parâmetros separados ("/app/x?acao=aprovar"). Fora do app, vai para o Início. */
export function splitLink(link: string): { to: string; search: Record<string, string> } {
  if (!link.startsWith('/app') || link.startsWith('//') || link.includes('\\')) return { to: '/app', search: {} }
  const url = new URL(link, 'http://app.local')
  return { to: url.pathname, search: Object.fromEntries(url.searchParams.entries()) }
}
