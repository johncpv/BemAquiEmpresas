import type { ReadyToShipLine, ShipmentDetail, ShipmentImpact, ShipmentLineInput, ShipmentState } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Situação do romaneio (CA 13): Rascunho neutro, Aguardando confirmação alerta, Expedido ok, Cancelado e Estornado neutros. */
export const SHIPMENT_STATUS: Record<ShipmentState, { label: string; tone: StatusTone }> = {
  Draft: { label: 'Rascunho', tone: 'neutral' },
  AwaitingStockApproval: { label: 'Aguardando confirmação do estoque', tone: 'warn' },
  Shipped: { label: 'Expedido', tone: 'ok' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
  Reversed: { label: 'Estornado', tone: 'neutral' },
}

export const SHIPMENT_SITUATIONS = [
  { value: 'rascunho', label: 'Rascunho' },
  { value: 'aguardando', label: 'Aguardando confirmação' },
  { value: 'expedido', label: 'Expedido' },
  { value: 'cancelado', label: 'Cancelado' },
  { value: 'estornado', label: 'Estornado' },
] as const

export const SHIPMENT_DICAS = {
  romaneio: 'Lista do que sai da fábrica; não é nota fiscal.',
  estoqueGeral: 'Produto acabado sem projeto: outra pessoa do estoque confirma a saída.',
  disponivel: 'Físico menos o reservado: o que dá para expedir agora.',
} as const

export const SHIPMENT_TEXTS = {
  salvo: 'Romaneio salvo',
  confirmada: 'Expedição confirmada',
  estoqueConfirmado: 'Estoque geral confirmado',
  cancelado: 'Romaneio cancelado',
  estornada: 'Expedição estornada',
  aguardando: 'Enviado para a confirmação do estoque geral',
  vazio: 'Nada pronto para expedir. As OS concluídas aparecem aqui.',
  nfe: 'Emitir NF-e: disponível na Fase 5',
} as const

export const quantity = (v: number) => v.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

/** Seleção de uma linha do formulário: as séries marcadas (com série) ou a quantidade (sem série). */
export interface LineChoice {
  key: string
  line: ReadyToShipLine
  serialIds: string[]
  quantity: string
  general: boolean
}

export const lineKey = (l: Pick<ReadyToShipLine, 'itemId' | 'locationId' | 'lotId' | 'fromGeneralStock'>) => `${l.itemId}|${l.locationId}|${l.lotId ?? ''}|${l.fromGeneralStock ? 'g' : 'p'}`

/**
 * Padrão do "Novo romaneio" (CA 1 e 9, padrão 6): todas as séries do projeto que não estão em outro romaneio e, sem série, o que dá para
 * expedir menos o que já está em romaneio aberto. Vindo da OS, só as séries da OS.
 */
export function defaultChoices(available: readonly ReadyToShipLine[], productionOrderId?: string | null): LineChoice[] {
  return available.map((line) => {
    const free = line.serials.filter((s) => !s.openShipmentNumber && (!productionOrderId || s.productionOrderId === productionOrderId))
    const rest = Math.max(0, Math.min(line.shippable, line.quantity) - line.inOpenShipments)
    return {
      key: lineKey(line), line, serialIds: line.tracksSerial ? free.map((s) => s.id) : [], quantity: line.tracksSerial ? '' : quantity(rest).replace(/\./g, ''), general: line.fromGeneralStock,
    }
  })
}

const toNumber = (text: string) => {
  const value = Number(text.trim().replace(/\./g, '').replace(',', '.'))
  return Number.isFinite(value) ? value : NaN
}

/** Linhas do pedido a partir da seleção: só as que têm série marcada ou quantidade maior que zero. */
export function choicesToLines(choices: readonly LineChoice[]): ShipmentLineInput[] {
  return choices.flatMap((c): ShipmentLineInput[] => {
    if (c.line.tracksSerial) {
      return c.serialIds.length === 0 ? [] : [{ itemId: c.line.itemId, locationId: c.line.locationId, lotId: c.line.lotId, quantity: c.serialIds.length, serialIds: c.serialIds, fromGeneralStock: c.general }]
    }
    const q = toNumber(c.quantity)
    return Number.isNaN(q) || q <= 0 ? [] : [{ itemId: c.line.itemId, locationId: c.line.locationId, lotId: c.line.lotId, quantity: q, serialIds: null, fromGeneralStock: c.general }]
  })
}

/** O que falta para salvar (CA 1): endereço, ao menos um item e quantidades dentro do que dá para expedir. */
export function formProblems(address: string, choices: readonly LineChoice[]): string[] {
  const problems: string[] = []
  if (address.trim() === '') problems.push('Informe o endereço de entrega.')
  if (choicesToLines(choices).length === 0) problems.push('Escolha ao menos uma série ou quantidade.')
  for (const c of choices.filter((x) => !x.line.tracksSerial && x.quantity.trim() !== '')) {
    const q = toNumber(c.quantity)
    if (Number.isNaN(q) || q < 0) problems.push(`Quantidade inválida em ${c.line.itemCode}.`)
    else if (q > c.line.shippable) problems.push(`${c.line.itemCode}: há ${quantity(c.line.shippable)} ${c.line.unitCode} para expedir em ${c.line.locationName}.`)
  }
  return problems
}

/** Impacto da confirmação por linha (CA 4): "Saída de 2 UN de ELV-0001 em Almoxarifado: 2 → 0 · séries ELV-…" e o aviso de saldo. */
export function impactLines(impact: ShipmentImpact): string[] {
  return impact.lines.map((l) => {
    const serials = l.serialNumbers.length > 0 ? ` · séries ${l.serialNumbers.join(', ')} passam a Expedido` : ''
    const general = l.fromGeneralStock ? ' · estoque geral' : ''
    const short = l.insufficient ? ' · saldo insuficiente agora' : ''
    return `Saída de ${quantity(l.quantity)} ${l.unitCode} de ${l.itemCode} em ${l.locationName}: ${quantity(l.onHandBefore)} → ${quantity(l.onHandAfter)}${serials}${general}${short}`
  })
}

/**
 * Próximos passos do romaneio expedido (CA 8): com OS de instalação, "Ver OS de instalação"; sem ela e com projects.approve, "Registrar
 * entrega"; "Ver projeto" sempre. A NF-e aparece à parte, como "disponível na Fase 5".
 */
export function shippedNext(s: Pick<ShipmentDetail, 'next' | 'status'>, canApproveProjects: boolean): ('instalacao' | 'entrega' | 'projeto')[] {
  if (s.status !== 'Shipped') return []
  const steps: ('instalacao' | 'entrega' | 'projeto')[] = []
  if (s.next.installationOrderId) steps.push('instalacao')
  else if (canApproveProjects && !s.next.projectDelivered) steps.push('entrega')
  steps.push('projeto')
  return steps
}

/** Ações do romaneio por situação e permissão (CA 4, 6, 10, 11 e 15). */
export function shipmentActions(s: Pick<ShipmentDetail, 'status' | 'next'>, can: { edit: boolean; approve: boolean; stock: boolean; create: boolean }) {
  return {
    edit: s.status === 'Draft' && can.create,
    confirm: s.status === 'Draft' && can.edit,
    confirmStock: s.status === 'AwaitingStockApproval' && can.stock,
    cancel: (s.status === 'Draft' || s.status === 'AwaitingStockApproval') && can.edit,
    reverse: s.status === 'Shipped' && can.approve,
  }
}
