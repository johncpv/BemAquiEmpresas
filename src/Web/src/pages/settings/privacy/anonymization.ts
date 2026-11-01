import type { StatusTone } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'

/** Item da prévia (LGPD-01b CA 2): o que é, de onde, quantos e, quando retido, até quando e por quê. */
export interface PlanItem {
  action: 'Anonymize' | 'Retain' | 'Keep' | string
  label: string
  source: string
  count: number
  retainedUntil?: string | null
  reason?: string | null
}

export interface PlanModule {
  module: string
  title: string
  items: PlanItem[]
  blocker?: string | null
}

export interface PlanLine {
  key: string
  module: string
  label: string
  count: number
  detail?: string
}

export interface AnonymizationStep {
  module: string
  title: string
  status: 'Pending' | 'Done' | 'Failed' | string
  error?: string | null
}

export const ANONYMIZATION_DICAS = {
  retido: 'Dado que a lei obriga a guardar (fiscal, contábil, trabalhista ou previdenciário). Fica até a data indicada, pela tabela de Retenção, e só então pode ser anonimizado.',
  marcador: 'Texto que entra no lugar do dado pessoal: "Titular anonimizado" e o número do pedido. Datas, documentos, telefones e e-mails ficam vazios.',
} as const

/** Situação da anonimização como chip (CA 4 e 5). */
export function anonymizationOf(status?: string | null): { label: string; tone: StatusTone } | null {
  if (status === 'Running') return { label: 'Anonimizando…', tone: 'info' }
  if (status === 'Partial') return { label: 'Anonimização parcial', tone: 'warn' }
  if (status === 'Done') return { label: 'Dados anonimizados', tone: 'ok' }
  return null
}

/** "Retido até 31/12/2030: obrigação fiscal e contábil" (sem data: enquanto durar o vínculo ou o pagamento). */
export function retainedText(item: Pick<PlanItem, 'retainedUntil' | 'reason'>): string {
  const reason = item.reason ?? 'obrigação legal'
  return item.retainedUntil ? `Retido até ${formatDate(item.retainedUntil)}: ${reason}` : `Retido sem prazo enquanto durar o vínculo ou o pagamento: ${reason}`
}

/** Os três blocos da prévia (CA 2): "Será anonimizado", "Fica retido até…" e "Documentos mantidos", com o módulo em cada linha. */
export function planBlocks(modules: PlanModule[]): { anonymize: PlanLine[]; retain: PlanLine[]; keep: PlanLine[] } {
  const lines = (action: string, detail?: (i: PlanItem) => string) => modules.flatMap((m) => m.items
    .filter((i) => i.action === action && i.count > 0)
    .map((i, index) => ({ key: `${m.module}-${action}-${index}`, module: m.title, label: i.label, count: i.count, detail: detail?.(i) })))
  return { anonymize: lines('Anonymize'), retain: lines('Retain', retainedText), keep: lines('Keep') }
}

/** "Anonimização parcial: Comercial, Financeiro / Produção" (CA 5): os módulos feitos e os que faltam. */
export function partialText(steps: AnonymizationStep[]): string {
  const ok = steps.filter((s) => s.status === 'Done').map((s) => s.title)
  const bad = steps.filter((s) => s.status !== 'Done').map((s) => s.title)
  return `Anonimização parcial: ${ok.join(', ') || 'nenhum módulo'} / ${bad.join(', ')}`
}

/** Resposta sugerida ao concluir o pedido depois de anonimizar (propositivo, CA 4). */
export function responseSuggestion(kind: string | null | undefined, anonymizedAt: string | null | undefined, retained: number): string {
  const when = anonymizedAt ? ` em ${new Date(anonymizedAt).toLocaleDateString('pt-BR', { timeZone: 'America/Sao_Paulo' })}` : ''
  const base = kind === 'Deletion' ? `Eliminação atendida por anonimização${when}: documentos com movimento não podem ser apagados.` : `Dados anonimizados${when}.`
  return retained > 0 ? `${base} Parte dos dados fica retida pelo prazo legal e será anonimizada a pedido depois dele.` : base
}
