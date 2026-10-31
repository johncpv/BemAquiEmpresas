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
