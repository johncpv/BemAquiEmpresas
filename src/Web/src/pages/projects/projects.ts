import type { DeliveryRisk } from '@/api/generated/model'

export const PROJECT_DICAS = {
  margemProjetada: 'Receita contratada menos o custo projetado (realizado mais o que falta do orçado ou o comprometido, o maior), sobre a receita.',
  realizado: 'Custo já gasto: títulos a pagar confirmados com o projeto (instalador, frete, outros), horas apontadas na produção e o material baixado para as OS, menos as devoluções.',
  comprometido: 'Já contratado, mas ainda não gasto: título previsto ou material comprado que não foi baixado para a OS.',
  projetado: 'Realizado mais o maior entre o que ainda falta do orçado e o comprometido. Se o realizado passou do orçado e nada está comprometido, é o próprio realizado.',
  implantacao: 'Custo calculado na implantação: horas com o custo-hora do cargo daquele dia e saídas de material lançadas antes do cálculo do custo do projeto.',
  desvio: 'Projetado menos orçado. Positivo quer dizer que o custo vai passar do orçado.',
  // PLN-01b: entrega prevista, folga e risco.
  entregaPrevista: 'Calculada pelo plano da fábrica: o fim da última OS de fabricação mais o prazo padrão de instalação (Configurações → Parâmetros → Produção) quando o projeto tem instalação. Muda sozinha a cada apontamento, etapa ou mudança no calendário.',
  folga: 'Dias corridos entre a entrega prevista e a prometida ao cliente. Antes do prometido, há folga; depois, o projeto vai atrasar.',
  emRisco: 'A entrega prevista passou da data prometida ao cliente, mesmo que por um dia. Renegocie antes: veja qual etapa empurra a data.',
} as const

export const pct = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${v.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`)

/** Dias de atraso da entrega prometida (COM-05): só enquanto o projeto não foi entregue. */
export function delayDays(promisedDate: string | null | undefined, status: string | null | undefined, today: string) {
  if (!promisedDate || status === 'Delivered' || status === 'Closed' || status === 'Cancelled' || promisedDate >= today) return null
  return Math.round((Date.parse(today + 'T00:00:00Z') - Date.parse(promisedDate + 'T00:00:00Z')) / 86_400_000)
}

const days = (n: number) => (n === 1 ? '1 dia' : `${n} dias`)

/** Folga (PLN-01b CA 8): "{n} dias antes do prometido", "{n} dias depois do prometido" ou "No dia prometido". */
export function slackText(slack: number | null | undefined): string | null {
  if (slack === null || slack === undefined) return null
  if (slack === 0) return 'No dia prometido'
  return slack > 0 ? `${days(slack)} antes do prometido` : `${days(-slack)} depois do prometido`
}

/** Motivo da previsão vazia, traduzido do código do plano (PLN-01b regras). */
export function forecastReasonText(reason: string | null | undefined, orderNumber?: string | null): string {
  const os = orderNumber ? `OS ${orderNumber}` : 'Uma OS'
  switch (reason) {
    case 'NoRouting':
      return `${os} sem roteiro`
    case 'NoWorkCenter':
      return `${os} com etapa sem centro de trabalho`
    case 'NoCapacity':
      return `${os} num centro sem capacidade nos próximos 2 anos`
    case 'NoFabricationOrder':
      return 'Nenhuma OS de fabricação no projeto'
    default:
      return 'Ainda não calculada'
  }
}

export interface ForecastFix {
  label: string
  to: string
  params?: Record<string, string>
  search?: Record<string, string | number>
}

/** Propositivo: o "Sem previsão" leva ao que resolve (roteiro ou centro na OS, calendário, OS do projeto). */
export function forecastFix(reason: string | null | undefined, orderId: string | null | undefined, projectId: string): ForecastFix | null {
  switch (reason) {
    case 'NoRouting':
      return orderId ? { label: 'Escolher roteiro', to: '/app/producao/os/$id', params: { id: orderId } } : null
    case 'NoWorkCenter':
      return orderId ? { label: 'Informar centro', to: '/app/producao/os/$id', params: { id: orderId }, search: { aba: 'etapas' } } : null
    case 'NoCapacity':
      return { label: 'Revisar calendário', to: '/app/producao/calendario' }
    case 'NoFabricationOrder':
      return { label: 'Gerar OS', to: '/app/producao/os', search: { semOs: 1, projeto: projectId } }
    default:
      return null
  }
}

/** Chip e faixa do prazo (PLN-01b CA 9): atrasado em vermelho, em risco em âmbar. */
export function riskChip(risk: DeliveryRisk | null | undefined): { tone: 'crit' | 'warn'; label: string } | null {
  if (risk === 'Late') return { tone: 'crit', label: 'Atrasado' }
  if (risk === 'AtRisk') return { tone: 'warn', label: 'Em risco' }
  return null
}

export function riskStripe(risk: DeliveryRisk | null | undefined): string | undefined {
  if (risk === 'Late') return 'border-l-4 border-l-crit'
  if (risk === 'AtRisk') return 'border-l-4 border-l-warn'
  return undefined
}
