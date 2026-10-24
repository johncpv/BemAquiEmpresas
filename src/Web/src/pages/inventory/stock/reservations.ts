import type { OrderMaterialLine, OrderMaterialView, ReservationChangeKind, ReservationState } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatQuantity } from './stock'

/** Reservas e faltas no front (EST-02): rótulos, dicas e a prévia; as regras de verdade estão no backend. */
export const RESERVATION_DICAS = {
  reservado: 'Reservado é o material separado para OS com BOM liberada. Ele continua no físico, mas não sai em saída avulsa, transferência nem em outra OS.',
  disponivel: 'Disponível é o físico menos o que já está reservado para OS abertas.',
  falta: 'Falta é o que a BOM pede e o estoque não tem livre. Vira solicitação de compra e se completa sozinha quando o material chega.',
  backfill: 'Reserva calculada na implantação das reservas, a partir das BOMs que já estavam liberadas.',
  baixadoAcima: 'Foi baixado mais do que a BOM vigente pede. Devolva o excedente pela requisição de material.',
}

export const RESERVATION_STATUS: Record<ReservationState, { label: string; tone: StatusTone }> = {
  Active: { label: 'Reservada', tone: 'ok' },
  Consumed: { label: 'Baixada', tone: 'info' },
  Released: { label: 'Liberada', tone: 'neutral' },
}

export const CHANGE_KIND: Record<ReservationChangeKind, string> = {
  Reserved: 'Reservou',
  Released: 'Liberou',
  Completed: 'Completou com material que chegou',
  Requested: 'Ajustou a necessidade',
  Issued: 'Baixou pela requisição',
  Returned: 'Voltou ao estoque',
}

/** Aviso de demora do processamento assíncrono (padrão 7 da Fase 3). */
export const SLOW_AFTER_MS = 30_000

/** Chip da falta como no protótipo: "Falta 4, SC gerada" quando já há solicitação; senão "Falta 4". */
export function shortageLabel(shortage: number, purchaseRequestLink?: string | null): string | null {
  if (shortage <= 0) return null
  return purchaseRequestLink ? `Falta ${formatQuantity(shortage)}, SC gerada` : `Falta ${formatQuantity(shortage)}`
}

/** Linhas com falta da aba Material (o cabeçalho da OS mostra quantas são). */
export const shortageLines = (view: OrderMaterialView | undefined): OrderMaterialLine[] => (view?.lines ?? []).filter((l) => l.shortage > 0)

/** "Falta material: 2 itens" no cabeçalho da OS; null sem falta. */
export function orderShortageSummary(view: OrderMaterialView | undefined): string | null {
  const count = shortageLines(view).length
  if (count === 0) return null
  return `Falta material: ${count} ${count === 1 ? 'item' : 'itens'}`
}

/** Aviso do fim da reserva (padrão 7, decisão do PO): "Material reservado: 3 itens, 1 em falta." (o mesmo texto da notificação). */
export function reservedNotice(view: OrderMaterialView): string {
  const items = view.lines.length
  return `Material reservado: ${items} ${items === 1 ? 'item' : 'itens'}, ${shortageLines(view).length} em falta.`
}

/** Locais da linha em texto curto ("ALM 6 · EXP 2"), para a linha expansível a 400 px. */
export const locationsText = (line: OrderMaterialLine) =>
  line.locations.filter((l) => l.reserved > 0 || l.requested > 0).map((l) => `${l.locationCode} ${formatQuantity(l.reserved)}`).join(' · ') || '—'

/** Variação do histórico com sinal ("+6", "−3"). */
export const deltaText = (delta: number) => formatQuantity(delta, true)
