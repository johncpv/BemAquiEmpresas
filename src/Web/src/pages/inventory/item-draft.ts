import type { ItemDetail, ItemInput, ItemType, WarrantyDefaultDetail } from '@/api/generated/model'
import { requiresNcm, suggestedTracking } from './item-labels'

/** Rascunho do item na tela; <c>touched</c> marca o que o usuário mexeu, para a sugestão do tipo não sobrescrever (CA 10 e 12). */
export interface ItemDraft {
  type: ItemType | ''
  code: string
  name: string
  unitId: string
  categoryId: string
  ncm: string
  cest: string
  origin: number
  minStock: string | null
  tracksLot: boolean
  tracksSerial: boolean
  warranty: string
  touched: { lot: boolean; serial: boolean; warranty: boolean }
}

export const emptyDraft = (): ItemDraft => ({
  type: '', code: '', name: '', unitId: '', categoryId: '', ncm: '', cest: '', origin: 0, minStock: '0', tracksLot: false, tracksSerial: false, warranty: '',
  touched: { lot: false, serial: false, warranty: false },
})

/** Do item gravado; com <paramref name="duplicate"/>, sem código (CA 18) e tudo marcado como escolhido. */
export const fromItem = (i: ItemDetail, duplicate = false): ItemDraft => ({
  type: i.type, code: duplicate ? '' : i.code, name: i.name, unitId: i.unitId, categoryId: i.categoryId ?? '', ncm: i.ncm ?? '', cest: i.cest ?? '', origin: i.origin,
  minStock: String(i.minStock), tracksLot: i.tracksLot, tracksSerial: i.tracksSerial, warranty: i.warrantyMonths === null ? '' : String(i.warrantyMonths),
  touched: { lot: true, serial: true, warranty: true },
})

/** Troca de tipo: lote, série e garantia seguem o tipo enquanto o usuário não mexeu neles (CA 10 e 12). */
export function withType(d: ItemDraft, type: ItemType, warranties: WarrantyDefaultDetail[]): ItemDraft {
  const s = suggestedTracking(type)
  const months = warranties.find((w) => w.itemType === type)?.months
  return {
    ...d,
    type,
    tracksLot: d.touched.lot ? d.tracksLot : s.lot,
    tracksSerial: d.touched.serial ? d.tracksSerial : s.serial,
    warranty: d.touched.warranty ? d.warranty : months === null || months === undefined ? '' : String(months),
  }
}

/** Erros que a tela antecipa; o servidor confere de novo. */
export function draftErrors(d: ItemDraft, active: boolean): Partial<Record<string, string>> {
  const e: Partial<Record<string, string>> = {}
  if (!d.type) e.type = 'Escolha o tipo do item.'
  if (!d.code.trim()) e.code = 'Informe o código do item.'
  if (!d.name.trim()) e.name = 'Informe o nome do item.'
  if (!d.unitId) e.unitId = 'Escolha a unidade de estoque.'
  if (d.ncm && d.ncm.length !== 8) e.ncm = 'O NCM tem 8 dígitos (ex.: 8431.31.10).'
  else if (!d.ncm && active && d.type && requiresNcm(d.type)) e.ncm = 'Preencha o NCM (8 dígitos) para o item ativo.'
  if (d.cest && d.cest.length !== 7) e.cest = 'O CEST tem 7 dígitos (ex.: 01.001.00).'
  if (d.warranty && (!/^\d+$/.test(d.warranty) || Number(d.warranty) > 120)) e.warranty = 'Informe de 0 a 120 meses, ou deixe vazio.'
  return e
}

export const toInput = (d: ItemDraft): ItemInput => ({
  type: d.type as ItemType, code: d.code.trim(), name: d.name.trim(), unitId: d.unitId, categoryId: d.categoryId || null, ncm: d.ncm || null, cest: d.cest || null,
  origin: d.origin, minStock: d.type === 'Service' ? 0 : Number(d.minStock ?? 0), tracksLot: d.type !== 'Service' && d.tracksLot, tracksSerial: d.type !== 'Service' && d.tracksSerial,
  warrantyMonths: d.warranty === '' ? null : Number(d.warranty),
})
