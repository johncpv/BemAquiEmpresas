import type { CostType, QuotePricingMode } from '@/api/generated/model'
import type { Tone } from '../finance/receivables/receivables'

export const QUOTE_STATUS: Record<string, { label: string; tone: Tone }> = {
  Draft: { label: 'Rascunho', tone: 'neutral' },
  Sent: { label: 'Enviado', tone: 'info' },
  Rejected: { label: 'Perdido', tone: 'neutral' },
}

export const ITEM_KINDS = [
  { value: 'Equipment', label: 'Equipamento' },
  { value: 'Product', label: 'Produto' },
  { value: 'Service', label: 'Serviço' },
] as const

export const COST_SECTIONS: { type: CostType; label: string }[] = [
  { type: 'Material', label: 'Material' },
  { type: 'Labor', label: 'Mão de obra' },
  { type: 'Installation', label: 'Instalação' },
  { type: 'Freight', label: 'Frete' },
  { type: 'Tax', label: 'Impostos estimados' },
  { type: 'Other', label: 'Outros' },
]

export const QUOTE_DICAS = {
  markup: 'Percentual somado ao custo para chegar ao preço. Custo de R$ 1.000,00 com markup de 40% vira R$ 1.400,00.',
  margem: 'Parte do preço que sobra depois do custo. Preço de R$ 1.666,67 com custo de R$ 1.000,00 tem margem de 40%.',
  custoHora: 'Vem do cargo, calculado pelo salário e pelos encargos. Nunca é o custo de um funcionário.',
  custoUnitario: 'Soma da composição para uma unidade do item.',
  digitado: 'Você digitou o preço. Ele não muda quando o custo muda, até voltar ao sugerido.',
  impostos: 'Valor estimado digitado, enquanto o regime tributário não está configurado.',
  padrao: 'Defina o % que a empresa costuma aplicar em Configurações → Parâmetros.',
}

/** "vence em 5 dias", "vence hoje" ou "venceu" (CA 10). */
export function validityLabel(validUntil: string | null | undefined, today: string) {
  if (!validUntil) return ''
  const days = Math.round((Date.parse(validUntil + 'T00:00:00Z') - Date.parse(today + 'T00:00:00Z')) / 86_400_000)
  return days < 0 ? 'venceu' : days === 0 ? 'vence hoje' : days === 1 ? 'vence amanhã' : `vence em ${days} dias`
}

const fmt = (v: number) => v.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** Rótulo do % (CA 12): "Markup 40,00% · Margem 28,57%" ou "Margem 40,00% · Markup 66,67%". */
export function pctLabel(mode: QuotePricingMode, pct: number) {
  if (mode === 'Markup') return `Markup ${fmt(pct)}% · Margem ${fmt(pct <= -100 ? 0 : (pct / (100 + pct)) * 100)}%`
  return pct >= 100 ? `Margem ${fmt(pct)}%` : `Margem ${fmt(pct)}% · Markup ${fmt((pct / (100 - pct)) * 100)}%`
}

/** Preço sugerido como o servidor calcula (o servidor grava; aqui só para o ao vivo). */
export function suggested(cost: number, mode: QuotePricingMode, pct: number) {
  if (mode === 'Markup') return Math.round(cost * (1 + pct / 100) * 100) / 100
  return pct >= 100 ? null : Math.round((cost / (1 - pct / 100)) * 100) / 100
}

export const lineTotal = (quantity: number, unitCost: number) => Math.round(quantity * unitCost * 100) / 100
