import type { AssetDetail, AssetStatus, AssetType } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

export const TYPE_LABELS: Record<AssetType, string> = { Machine: 'Máquina', Tool: 'Ferramenta', Furniture: 'Mobiliário', Vehicle: 'Veículo', It: 'TI' }

export const TYPE_OPTIONS = (Object.keys(TYPE_LABELS) as AssetType[]).map((value) => ({ value, label: TYPE_LABELS[value] }))

export const STATUS_LABELS: Record<AssetStatus, string> = { Active: 'Ativo', Maintenance: 'Em manutenção', Disposed: 'Baixado' }

/** Chips da situação (CA 9): Ativo ok, Em manutenção alerta, Baixado neutro. */
export const STATUS_TONES: Record<AssetStatus, StatusTone> = { Active: 'ok', Maintenance: 'warn', Disposed: 'neutral' }

export const STATUS_OPTIONS = (Object.keys(STATUS_LABELS) as AssetStatus[]).map((value) => ({ value, label: STATUS_LABELS[value] }))

export const isAssetType = (value: string | undefined): value is AssetType => !!value && value in TYPE_LABELS

export const isAssetStatus = (value: string | undefined): value is AssetStatus => !!value && value in STATUS_LABELS

/** Máquina e veículo têm plano de manutenção (PAT-03a): o próximo passo só para eles. */
export const hasMaintenancePlan = (type: AssetType) => type === 'Machine' || type === 'Vehicle'

export const ASSET_DICAS = {
  etiqueta: 'Código do bem, gerado ao salvar (PAT-00001). Não muda e não é reaproveitado; é ele que vai na etiqueta com QR code.',
  centro: 'O centro de trabalho onde a máquina produz. Ao escolher, o local e o responsável vêm sugeridos pelos outros bens do centro.',
  foto: 'A localização gravada na foto é apagada.',
}

/** Rascunho do formulário do bem: textos como digitados, dinheiro normalizado ("1234.56") e datas aaaa-mm-dd. */
export interface AssetDraft {
  name: string
  type: AssetType | ''
  brand: string
  model: string
  serialNumber: string
  acquisitionDate: string
  acquisitionValue: string | null
  incomingInvoiceId: string | null
  invoiceLabel: string | null
  invoiceRef: string
  supplierPartyId: string | null
  supplierName: string | null
  siteId: string
  responsibleEmployeeId: string
  workCenterId: string
  notes: string
  /** O usuário mexeu no campo: a sugestão do centro não troca mais (CA 3). */
  touched: { site: boolean; responsible: boolean }
}

export const emptyAssetDraft = (): AssetDraft => ({
  name: '', type: '', brand: '', model: '', serialNumber: '', acquisitionDate: '', acquisitionValue: null, incomingInvoiceId: null, invoiceLabel: null, invoiceRef: '',
  supplierPartyId: null, supplierName: null, siteId: '', responsibleEmployeeId: '', workCenterId: '', notes: '', touched: { site: false, responsible: false },
})

/** Rascunho a partir de um bem (edição); <c>duplicate</c>: "Duplicar bem" copia tudo menos a série e a etiqueta. */
export function draftFromAsset(a: AssetDetail, duplicate = false): AssetDraft {
  return {
    name: a.name, type: a.type, brand: a.brand ?? '', model: a.model ?? '', serialNumber: duplicate ? '' : a.serialNumber ?? '', acquisitionDate: a.acquisitionDate ?? '',
    acquisitionValue: a.acquisitionValue == null ? null : String(a.acquisitionValue), incomingInvoiceId: a.incomingInvoiceId ?? null,
    invoiceLabel: a.incomingInvoice ? invoiceLabel(a.incomingInvoice.number, a.incomingInvoice.series, a.incomingInvoice.issuerName) : null, invoiceRef: a.invoiceRef ?? '',
    supplierPartyId: a.supplierPartyId ?? null, supplierName: a.supplierName ?? null, siteId: a.siteId ?? '', responsibleEmployeeId: a.responsibleEmployeeId ?? '', workCenterId: a.workCenterId ?? '',
    notes: a.notes ?? '', touched: { site: true, responsible: true },
  }
}

export const invoiceLabel = (number: string, series: string, issuer: string) => `NF ${number} série ${series} · ${issuer}`

/**
 * Sugestão do centro (CA 3, propositivo): local e responsável só mudam enquanto o usuário não mexeu no campo; sugestão vazia não
 * apaga o que já estava.
 */
export function applySuggestion(draft: AssetDraft, suggestion: { siteId?: string | null; responsibleEmployeeId?: string | null }): AssetDraft {
  return {
    ...draft,
    siteId: !draft.touched.site && suggestion.siteId ? suggestion.siteId : draft.siteId,
    responsibleEmployeeId: !draft.touched.responsible && suggestion.responsibleEmployeeId ? suggestion.responsibleEmployeeId : draft.responsibleEmployeeId,
  }
}

/** Erros locais (o servidor confere de novo): nome e tipo obrigatórios. */
export function assetDraftErrors(d: AssetDraft): Partial<Record<keyof AssetDraft, string>> {
  const errors: Partial<Record<keyof AssetDraft, string>> = {}
  if (!d.name.trim()) errors.name = 'Informe o nome do bem.'
  if (!d.type) errors.type = 'Escolha o tipo do bem.'
  return errors
}

const text = (v: string) => (v.trim() ? v.trim() : null)

/** Corpo do POST/PUT. Sem <c>assets.edit</c> o valor de aquisição não vai (o servidor recusa com 403). */
export function assetInput(d: AssetDraft, canEditValue: boolean, version?: number) {
  return {
    name: d.name.trim(), type: d.type as AssetType, brand: text(d.brand), model: text(d.model), serialNumber: text(d.serialNumber), acquisitionDate: d.acquisitionDate || null,
    acquisitionValue: canEditValue && d.acquisitionValue != null ? Number(d.acquisitionValue) : null, incomingInvoiceId: d.incomingInvoiceId, invoiceRef: d.incomingInvoiceId ? null : text(d.invoiceRef),
    supplierPartyId: d.supplierPartyId, siteId: d.siteId || null, responsibleEmployeeId: d.responsibleEmployeeId || null, workCenterId: d.workCenterId || null, notes: text(d.notes),
    version: version ?? null,
  }
}

// ---- PAT-02: movimentação e baixa ----

export type MovementKind = 'Transfer' | 'Disposal' | 'Reactivation'
export type DisposalReason = 'Sale' | 'Scrap' | 'Loss' | 'Donation' | 'Other'

export const MOVEMENT_KIND_LABELS: Record<MovementKind, string> = { Transfer: 'Transferência', Disposal: 'Baixa', Reactivation: 'Reativação' }

export const DISPOSAL_REASON_LABELS: Record<DisposalReason, string> = { Sale: 'Venda', Scrap: 'Descarte', Loss: 'Perda ou roubo', Donation: 'Doação', Other: 'Outro' }

export const DISPOSAL_REASON_OPTIONS = (Object.keys(DISPOSAL_REASON_LABELS) as DisposalReason[]).map((value) => ({ value, label: DISPOSAL_REASON_LABELS[value] }))

/** Motivo sugerido para o filtro "Responsável desligado" (CA 5, propositivo). */
export const TERMINATED_REASON = 'Responsável desligado'

export const REASON_MAX = 200

/** Hoje em America/Sao_Paulo (AAAA-MM-DD), para a data padrão das movimentações. */
export const todayIso = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

/** Aviso da reativação de um bem vendido (CA 8). */
export const SALE_REVERTED_WARNING = 'Cancele o título da venda no Financeiro, se for o caso.'

export const MOVEMENT_DICAS = {
  movimentacao: 'Registra a troca de local, de responsável ou de centro de trabalho. Fica no histórico do bem e não é apagada; um erro se corrige com outra movimentação.',
  baixa: 'O bem sai da empresa (venda, descarte, perda, doação). Some das listas e dos seletores, mas nada é apagado; dá para reativar se foi engano.',
}

export interface Place {
  siteName?: string | null
  responsibleName?: string | null
  workCenterName?: string | null
}

/** "Local · Responsável · Centro" do movimento, com "—" no que estiver vazio. */
export function placeText(p: Place): string {
  return [p.siteName ?? 'Sem local', p.responsibleName ?? 'Sem responsável', p.workCenterName ?? 'Sem centro'].join(' · ')
}

/** O que mudou de um lado para o outro: só os campos diferentes ("Local: Fábrica → Escritório"). */
export function movementChanges(from: Place, to: Place): string[] {
  const rows: [string, string | null | undefined, string | null | undefined][] = [
    ['Local', from.siteName, to.siteName], ['Responsável', from.responsibleName, to.responsibleName], ['Centro', from.workCenterName, to.workCenterName],
  ]
  return rows.filter(([, a, b]) => (a ?? null) !== (b ?? null)).map(([label, a, b]) => `${label}: ${a ?? '—'} → ${b ?? '—'}`)
}

/** Rascunho de "Registrar movimentação": vem com o lugar atual (o usuário só troca o que muda). */
export interface MovementDraft {
  siteId: string
  responsibleEmployeeId: string
  workCenterId: string
  movedOn: string
  reason: string
}

export function movementDraft(a: { siteId?: string | null; responsibleEmployeeId?: string | null; workCenterId?: string | null }, today: string, reason = ''): MovementDraft {
  return { siteId: a.siteId ?? '', responsibleEmployeeId: a.responsibleEmployeeId ?? '', workCenterId: a.workCenterId ?? '', movedOn: today, reason }
}

/** CA 2 (conferido de novo no servidor): ao menos um de local, responsável ou centro diferente do atual. */
export function movementChanged(d: MovementDraft, a: { siteId?: string | null; responsibleEmployeeId?: string | null; workCenterId?: string | null }): boolean {
  return d.siteId !== (a.siteId ?? '') || d.responsibleEmployeeId !== (a.responsibleEmployeeId ?? '') || d.workCenterId !== (a.workCenterId ?? '')
}

/** Busca do "Novo título a receber" pré-preenchido pela venda (CA 7): nada é gravado no Financeiro sem a confirmação de lá. */
export function receivableSearch(r: { partyId?: string | null; partyName?: string | null; amount: number; issueDate: string; description: string }) {
  return {
    cliente: r.partyId ?? undefined, clienteNome: r.partyName ?? undefined, valor: String(r.amount), emissao: r.issueDate, descricao: r.description,
  }
}
