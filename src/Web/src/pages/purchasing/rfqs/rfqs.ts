import type { RfqLineView, RfqSelectionReason, RfqStatus, RfqSupplierView } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { orderTotals } from '../orders/purchase-orders'

/** Cotações no front (CMP-02): rótulos, dicas e as contas ao vivo; o servidor recalcula e decide tudo ao salvar. */
export const RFQ_STATUS: Record<RfqStatus, { label: string; tone: StatusTone }> = {
  Open: { label: 'Aberta', tone: 'info' },
  Closed: { label: 'Concluída', tone: 'ok' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
}

type Reason = NonNullable<RfqSelectionReason>

export const REASONS: Record<Reason, string> = {
  LeadTime: 'Prazo',
  Quality: 'Qualidade',
  PaymentTerm: 'Condição de pagamento',
  ApprovedSupplier: 'Fornecedor homologado',
  Other: 'Outro',
}

export const REASON_OPTIONS = (Object.keys(REASONS) as Reason[]).map((value) => ({ value, label: REASONS[value] }))

export const SHOW_OPTIONS = [
  { value: 'abertas', label: 'Abertas' },
  { value: 'todas', label: 'Todas' },
]

export const STATUS_OPTIONS = (Object.keys(RFQ_STATUS) as RfqStatus[]).map((value) => ({ value, label: RFQ_STATUS[value].label }))

export const RFQ_DICAS = {
  custoTotal: 'Itens + frete + IPI, como entra no custo do estoque.',
  motivo: 'Quando o vencedor não é o de menor custo total da linha, diga por quê: prazo, qualidade, condição de pagamento, fornecedor homologado ou outro motivo. Fica no histórico da cotação.',
  anexo: (limit: string) => `Acima de ${limit} no custo total, a resposta só é salva com a proposta do fornecedor anexada (PDF, imagem ou planilha, até 10 MB).`,
  umaCotacao: 'Só um fornecedor respondeu a esta cotação.',
  sugerido: 'Sugerido pelo menor custo total da linha: o preço, o IPI e o frete do fornecedor rateado pelo valor dos itens dele. Você pode escolher outro, com o motivo.',
  fornecedoresSugeridos: 'Os últimos fornecedores destes itens, pelo pedido aprovado mais recente de cada um (até 3).',
  unidadeFornecedor: 'Preço na unidade em que o fornecedor vende (cadastrada no item). O sistema divide pelo fator e grava o preço na unidade de estoque.',
}

/** Situação da resposta do fornecedor (chip). */
export function responseState(s: Pick<RfqSupplierView, 'responded' | 'declined'>): { label: string; tone: StatusTone } {
  if (s.responded) return { label: 'Respondida', tone: 'ok' }
  if (s.declined) return { label: 'Não cotou', tone: 'neutral' }
  return { label: 'Aguardando resposta', tone: 'warn' }
}

/** Ids da URL ("?sc=a,b,c"). */
export const idsFromSearch = (value: string | undefined) => (value ? value.split(',').map((v) => v.trim()).filter(Boolean) : [])

/** Vencedor por linha (CA 8): o já escolhido na cotação concluída; senão o de menor custo total (pré-marcado, tag "Sugerido"). */
export function initialSelection(lines: Pick<RfqLineView, 'id' | 'selectedSupplierId' | 'cheapestSupplierId'>[]): Record<string, string> {
  const picked: Record<string, string> = {}
  for (const l of lines) {
    const choice = l.selectedSupplierId ?? l.cheapestSupplierId
    if (choice) picked[l.id] = choice
  }
  return picked
}

/** "Escolher tudo de {fornecedor}": as linhas que ele cotou passam para ele; as outras ficam como estavam. */
export function chooseAll(selection: Record<string, string>, supplier: Pick<RfqSupplierView, 'id' | 'quotes'>): Record<string, string> {
  const next = { ...selection }
  for (const q of supplier.quotes) next[q.lineId] = supplier.id
  return next
}

/** O vencedor escolhido não é o de menor custo: o motivo é obrigatório (CA 8). */
export const needsReason = (line: Pick<RfqLineView, 'cheapestSupplierId'>, chosen: string | undefined) => !!line.cheapestSupplierId && !!chosen && chosen !== line.cheapestSupplierId

export interface ReasonDraft {
  reason: Reason | ''
  note: string
}

/** Linhas cotadas sem vencedor ou com o motivo faltando, para o botão "Concluir cotação" dizer o que falta. */
export function missingForClose(
  lines: Pick<RfqLineView, 'id' | 'itemCode' | 'cheapestSupplierId'>[], quotedLines: Set<string>, selection: Record<string, string>, reasons: Record<string, ReasonDraft>,
): string[] {
  const missing: string[] = []
  for (const l of lines) {
    if (!quotedLines.has(l.id)) continue
    const chosen = selection[l.id]
    if (!chosen) missing.push(`Escolha o vencedor de ${l.itemCode}.`)
    else if (needsReason(l, chosen)) {
      const r = reasons[l.id]
      if (!r?.reason) missing.push(`Informe o motivo da escolha de ${l.itemCode}.`)
      else if (r.reason === 'Other' && !r.note.trim()) missing.push(`Escreva o motivo da escolha de ${l.itemCode}.`)
    }
  }
  return missing
}

/** Preço na unidade de estoque a partir da do fornecedor: dividido pelo fator, 6 casas (a mesma conta do servidor). */
export function toStockPrice(price: number, factor: number): number {
  if (factor <= 0) return 0
  return Math.round(Number(((price / factor) * 1_000_000).toFixed(6))) / 1_000_000
}

export interface QuoteDraft {
  price: string | null
  inSupplierUnit: boolean
  available: string | null
  leadDays: string
}

/** Custo total ao vivo da resposta (CA 6, 7): Σ (qtd × preço na unidade de estoque) + frete + IPI. */
export function responseTotal(
  lines: Pick<RfqLineView, 'id' | 'itemId' | 'quantity'>[], quotes: Record<string, QuoteDraft>, units: { itemId: string; factor: number }[], freight: number, ipiPct: number,
) {
  const priced = lines.flatMap((l) => {
    const q = quotes[l.id]
    const price = q?.price ? Number(q.price) : 0
    if (price <= 0) return []
    const factor = units.find((u) => u.itemId === l.itemId)?.factor
    return [{ quantity: l.quantity, unitPrice: q.inSupplierUnit && factor ? toStockPrice(price, factor) : price }]
  })
  return orderTotals(priced, freight, ipiPct)
}
