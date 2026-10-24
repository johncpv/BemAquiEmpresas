import type { LotExpiryState, LotTrace, SerialState, SerialTrace, TraceCustomer, TraceEntry } from '@/api/generated/model'
import type { TreeNode } from '@/components/ui/tree-table'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatDate, formatDateTime, formatInstantDate } from '@/lib/format'
import { formatQuantity } from './stock'

/** Textos da rastreabilidade (EST-04), um lugar só para a tela, a ajuda e os testes. */
export const TRACE_TEXT = {
  inicial: 'Digite uma série ou um lote para ver de onde veio e para onde foi.',
  semResultado: (termo: string) => `Nenhuma série ou lote com '${termo}'.`,
  dicaLotesDaOs: 'A OS baixa o material para a OS inteira. Cada série herda todos os lotes da OS que a produziu.',
  dicaIniciais: 'Cliente pessoa física aparece por extenso só para quem tem acesso a terceiros.',
} as const

/** Situação da série na rastreabilidade (CA 1): Em estoque, Expedido, Cancelada. */
export const TRACE_SERIAL_STATUS: Record<SerialState, { label: string; tone: StatusTone }> = {
  InStock: { label: 'Em estoque', tone: 'ok' },
  Shipped: { label: 'Expedido', tone: 'info' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
}

/** Chip da validade (CA 5): alerta "Vence em {n} dias" até 30 dias, crítico "Vencido"; null em dia ou sem validade. */
export function expiryChip(state: LotExpiryState, days: number | null | undefined): { label: string; tone: StatusTone } | null {
  if (state === 'Expired') return { label: 'Vencido', tone: 'crit' }
  if (state !== 'Expiring') return null
  if (days === 0) return { label: 'Vence hoje', tone: 'warn' }
  if (days === 1) return { label: 'Vence em 1 dia', tone: 'warn' }
  return { label: `Vence em ${days ?? 0} dias`, tone: 'warn' }
}

/** Hoje em São Paulo, "AAAA-MM-DD". */
export const todaySaoPaulo = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())

/** Dias até a validade no fuso de São Paulo, para o chip quando o servidor não manda (ex.: os lotes consumidos da série). */
export function daysUntil(expiry: string, today: string): number {
  const [y1, m1, d1] = expiry.split('-').map(Number)
  const [y2, m2, d2] = today.split('-').map(Number)
  return Math.round((Date.UTC(y1, m1 - 1, d1) - Date.UTC(y2, m2 - 1, d2)) / 86_400_000)
}

/**
 * O que falta para salvar a correção do lote (CA 6): motivo de 3 a 500 caracteres, número informado, algo mudou, e com saída só a
 * validade. Null = pode salvar.
 */
export function correctionProblem(lot: { lotNumber: string; expiry?: string | null; hasOutflow: boolean }, number: string, expiry: string, reason: string): string | null {
  const n = number.trim()
  if (!n) return 'Informe o número do lote.'
  if (lot.hasOutflow && n !== lot.lotNumber) return 'Lote com saída só tem a validade corrigida.'
  if (n === lot.lotNumber && (expiry || null) === (lot.expiry ?? null)) return 'Mude o número ou a validade.'
  if (reason.trim().length < 3) return 'Escreva o motivo da correção.'
  return null
}

/** Nome do cliente com a marca de iniciais (CA 9): a tela mostra a Dica "Iniciais do cliente" quando vem mascarado. */
export function customerLabel(customer: TraceCustomer | null | undefined): string {
  return customer ? customer.name : 'Sem cliente'
}

/** Link de uma entrada: o recebimento (CMP-04) pela rota de Compras; o resto pelo movimento do estoque. */
export function entryLink(entry: TraceEntry): { to: string; id: string } {
  return entry.sourceType === 'GoodsReceipt' ? { to: '/app/compras/recebimentos/$id', id: entry.sourceId } : { to: '/app/estoque/movimentos/$id', id: entry.movementId }
}

/** Nó da árvore da rastreabilidade: rótulo, detalhe, quantidade, link e chip. */
export interface TraceNode extends TreeNode {
  children?: TraceNode[]
  detail?: string
  quantity?: string
  link?: { to: string; params: Record<string, string>; search?: Record<string, string>; label: string }
  chip?: { label: string; tone: StatusTone }
  masked?: boolean
  dica?: 'lotesDaOs' | 'iniciais'
}

const entryNode = (e: TraceEntry, unit: string): TraceNode => {
  const link = entryLink(e)
  return {
    id: `entry-${e.movementId}`, label: e.label, detail: formatDateTime(e.at), quantity: `${formatQuantity(e.quantity)} ${unit}`,
    link: { to: link.to, params: { id: link.id }, label: e.sourceType === 'GoodsReceipt' ? 'Ver recebimento' : 'Ver movimento' },
    chip: e.reversed ? { label: 'Estornada', tone: 'neutral' } : undefined,
  }
}

/** Série → origem (CA 1 e 2) em árvore: o item, a OS, o projeto e o cliente, o romaneio e os lotes da OS com as entradas. */
export function serialTree(trace: SerialTrace, today: string): TraceNode[] {
  const status = TRACE_SERIAL_STATUS[trace.status]
  const origin: TraceNode[] = []
  if (trace.productionOrderId) {
    origin.push({
      id: 'order', label: `OS ${trace.productionOrderNumber ?? ''}`.trim(), detail: trace.completedAt ? `Concluída em ${formatInstantDate(trace.completedAt)}` : 'Ainda não concluída',
      link: { to: '/app/producao/os/$id', params: { id: trace.productionOrderId }, search: { aba: 'series' }, label: 'Abrir OS' },
    })
  }
  if (trace.projectId) {
    origin.push({
      id: 'project', label: `Projeto ${trace.projectNumber ?? ''}`.trim(), detail: trace.projectName ?? undefined,
      link: { to: '/app/projetos/$id', params: { id: trace.projectId }, label: 'Abrir projeto' },
      children: [{
        id: 'customer', label: `Cliente ${customerLabel(trace.customer)}`, masked: trace.customer?.masked, dica: trace.customer?.masked ? 'iniciais' : undefined,
        link: trace.customer?.partyId ? { to: '/app/cadastros/terceiros/$id', params: { id: trace.customer.partyId }, label: 'Abrir cliente' } : undefined,
      }],
    })
  }
  if (trace.shipmentId) {
    origin.push({
      id: 'shipment', label: `Romaneio ${trace.shipmentNumber ?? ''}`.trim(), detail: trace.shippedAt ? `Expedido em ${formatInstantDate(trace.shippedAt)}` : undefined,
      link: { to: '/app/producao/expedicao/$id', params: { id: trace.shipmentId }, label: 'Abrir romaneio' },
    })
  }
  const lots: TraceNode[] = trace.lots.map((l, i) => {
    const expiry = l.expiry ? expiryChip(l.expiryState, daysUntil(l.expiry, today)) : null
    return {
      id: `lot-${l.lotId ?? `sem-${l.itemId}-${i}`}`, label: `${l.itemCode} ${l.itemName}${l.lotNumber ? ` · Lote ${l.lotNumber}` : ' · sem lote'}`,
      detail: [l.supplierName ? `Fornecedor ${l.supplierName}` : null, l.expiry ? `Validade ${formatDate(l.expiry)}` : null].filter(Boolean).join(' · ') || undefined,
      quantity: `${formatQuantity(l.quantity)} ${l.unitCode}`, chip: expiry ?? undefined,
      link: l.lotId ? { to: '/app/estoque/rastreabilidade', params: {}, search: { item: l.itemId, lote: l.lotId }, label: 'Ver destino do lote' } : undefined,
      children: l.entries.map((e) => entryNode(e, l.unitCode)),
    }
  })
  return [{
    id: 'serial', label: `Série ${trace.serialNumber}`, detail: `${trace.itemCode} ${trace.itemName}${trace.locationName ? ` · ${trace.locationName}` : ''}`, chip: status,
    children: [
      ...origin,
      {
        id: 'lots', label: 'Lotes de matéria-prima consumidos', detail: trace.sharedLotsNote ?? (lots.length === 0 ? 'Nenhum lote registrado na conclusão da OS.' : undefined),
        dica: trace.sharedLotsNote ? 'lotesDaOs' : undefined, children: lots,
      },
    ],
  }]
}

/** Lote → destino (CA 3) em árvore: entradas, saldo por local, consumo por OS com as séries e os romaneios, e as outras linhas. */
export function lotTree(trace: LotTrace): TraceNode[] {
  const unit = trace.unitCode
  const q = (n: number) => `${formatQuantity(n)} ${unit}`
  return [
    { id: 'entries', label: 'Entradas', quantity: q(trace.totalReceived), children: trace.entries.map((e) => entryNode(e, unit)) },
    {
      id: 'balances', label: 'Saldo atual', quantity: q(trace.onHand), detail: trace.balances.length === 0 ? 'Sem saldo em nenhum local.' : undefined,
      children: trace.balances.map((b) => ({ id: `bal-${b.locationId}`, label: b.locationName, quantity: q(b.onHand) })),
    },
    {
      id: 'uses', label: 'Consumo nas OS', quantity: q(trace.totalConsumed), detail: trace.consumptions.length === 0 ? 'Nenhuma OS consumiu este lote.' : undefined,
      children: trace.consumptions.map((c) => ({
        id: `use-${c.productionOrderId}`, label: `OS ${c.orderNumber ?? ''}`.trim(), quantity: q(c.quantity),
        detail: [c.projectNumber ? `Projeto ${c.projectNumber}` : null, c.customer ? `Cliente ${c.customer.name}` : null, formatInstantDate(c.at)].filter(Boolean).join(' · '),
        masked: c.customer?.masked, dica: c.customer?.masked ? 'iniciais' : undefined,
        link: { to: '/app/producao/os/$id', params: { id: c.productionOrderId }, search: { aba: 'series' }, label: 'Abrir OS' },
        children: c.serials.length === 0
          ? [{ id: `note-${c.productionOrderId}`, label: c.noSerialNote ?? 'Sem série.' }]
          : c.serials.map((s) => ({
            id: `serial-${s.id}`, label: `Série ${s.serialNumber}`, chip: TRACE_SERIAL_STATUS[s.status],
            detail: s.shipmentNumber ? `Romaneio ${s.shipmentNumber}${s.shippedAt ? ` · ${formatInstantDate(s.shippedAt)}` : ''}` : undefined,
            link: s.shipmentId ? { to: '/app/producao/expedicao/$id', params: { id: s.shipmentId }, label: 'Abrir romaneio' } : undefined,
          })),
      })),
    },
    {
      id: 'others', label: 'Outras saídas e ajustes', quantity: q(trace.totalOther), detail: trace.others.length === 0 ? 'Nenhuma.' : undefined,
      children: trace.others.map((o) => ({
        id: `other-${o.movementId}`, label: o.explanation, detail: formatDateTime(o.at), quantity: q(o.quantity),
        link: { to: '/app/estoque/movimentos/$id', params: { id: o.movementId }, label: 'Ver movimento' },
      })),
    },
  ]
}

/** A conta do CA 3 em uma frase: "Recebido 50 − consumido 20 + outras −2 = saldo 28". */
export function reconciliationText(trace: LotTrace): string {
  const u = trace.unitCode
  return `Recebido ${formatQuantity(trace.totalReceived)} − consumido ${formatQuantity(trace.totalConsumed)} + outras ${formatQuantity(trace.totalOther)} = saldo ${formatQuantity(trace.onHand)} ${u}`
}
