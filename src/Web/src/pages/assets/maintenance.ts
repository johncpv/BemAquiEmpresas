import type { MaintenanceChecklistItemView, MaintenanceFrequency, MaintenanceOrderStatus, MaintenanceOrderType, MaintenancePlanView } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { parseBrDecimal } from '@/lib/decimal'
import { formatDate } from '@/lib/format'

/** Patrimônio → Manutenção (PAT-03a): rótulos, tons e regras puras das telas de plano e OS de manutenção. */

export const ORDER_TYPE_LABELS: Record<MaintenanceOrderType, string> = { Preventive: 'Preventiva', Corrective: 'Corretiva' }

export const ORDER_TYPE_OPTIONS = (Object.keys(ORDER_TYPE_LABELS) as MaintenanceOrderType[]).map((value) => ({ value, label: ORDER_TYPE_LABELS[value] }))

export const ORDER_STATUS_LABELS: Record<MaintenanceOrderStatus, string> = { Open: 'Aberta', InProgress: 'Em andamento', Done: 'Concluída', Cancelled: 'Cancelada' }

export const ORDER_STATUS_TONES: Record<MaintenanceOrderStatus, StatusTone> = { Open: 'info', InProgress: 'warn', Done: 'ok', Cancelled: 'neutral' }

export const ORDER_STATUS_OPTIONS = (Object.keys(ORDER_STATUS_LABELS) as MaintenanceOrderStatus[]).map((value) => ({ value, label: ORDER_STATUS_LABELS[value] }))

export const FREQUENCY_LABELS: Record<MaintenanceFrequency, string> = { Days: 'A cada N dias', Hours: 'A cada N horas de uso' }

export const isOrderType = (v: string | undefined): v is MaintenanceOrderType => !!v && v in ORDER_TYPE_LABELS

export const isOrderStatus = (v: string | undefined): v is MaintenanceOrderStatus => !!v && v in ORDER_STATUS_LABELS

export const MAINTENANCE_DICAS = {
  horimetro: 'O contador de horas de uso da máquina. Digite o valor que aparece no painel dela: a próxima preventiva por horas é prevista pela média de uso das últimas leituras.',
  antecedencia: 'Quantos dias antes do vencimento o sistema avisa quem cuida do patrimônio. No dia do vencimento, a OS preventiva é aberta sozinha.',
  maquinaParada: 'Marque quando a máquina não pode trabalhar. Ela fica "Em manutenção" até a OS ser concluída ou cancelada.',
}

/** Horímetro no formato brasileiro, até 4 casas, sem zeros à direita ("1.234,5 h"). */
export const formatMeter = (value: number) => `${value.toLocaleString('pt-BR', { maximumFractionDigits: 4 })} h`

/** "A cada 30 dias" / "A cada 250 horas de uso". */
export const frequencyText = (type: MaintenanceFrequency, value: number) =>
  type === 'Days' ? `A cada ${value} ${value === 1 ? 'dia' : 'dias'}` : `A cada ${value.toLocaleString('pt-BR')} ${value === 1 ? 'hora' : 'horas'} de uso`

/** Próximo vencimento na tela (CA 1, 2 e 9): a data no plano por dias; o horímetro e a estimativa no plano por horas. */
export function nextDueText(plan: Pick<MaintenancePlanView, 'frequencyType' | 'nextDueAt' | 'nextDueMeter'>) {
  if (plan.frequencyType === 'Days') return plan.nextDueAt ? formatDate(plan.nextDueAt) : 'Sem data'
  const meter = plan.nextDueMeter != null ? formatMeter(plan.nextDueMeter) : 'Sem horímetro'
  return plan.nextDueAt ? `${meter} (previsto para ${formatDate(plan.nextDueAt)})` : `${meter} (registre o horímetro para prever a data)`
}

/** Chip do plano (CA 9): "Vencida" crítico; "A vencer" alerta na antecedência; "Inativo" neutro. */
export function planChip(plan: Pick<MaintenancePlanView, 'isActive' | 'overdue' | 'dueSoon'>): { tone: StatusTone; label: string } | null {
  if (!plan.isActive) return { tone: 'neutral', label: 'Inativo' }
  if (plan.overdue) return { tone: 'crit', label: 'Vencida' }
  if (plan.dueSoon) return { tone: 'warn', label: 'A vencer' }
  return null
}

/** Itens que faltam marcar e o texto do botão desabilitado (CA 6). */
export function missingText(items: { result?: MaintenanceChecklistItemView['result'] }[]) {
  const missing = items.filter((i) => !i.result).length
  return { missing, text: missing === 0 ? null : missing === 1 ? 'Marque o item que falta' : `Marque os ${missing} itens que faltam` }
}

/** Rascunho do plano: textos como digitados; números e datas em texto (aaaa-mm-dd). */
export interface PlanDraft {
  description: string
  frequencyType: MaintenanceFrequency
  frequencyValue: string
  leadDays: string
  supplier: { id: string; label: string } | null
  checklist: string[]
  lastDoneAt: string
  lastDoneMeter: string
  isActive: boolean
}

export const emptyPlanDraft = (today: string): PlanDraft => ({
  description: '', frequencyType: 'Days', frequencyValue: '30', leadDays: '7', supplier: null, checklist: [''], lastDoneAt: today, lastDoneMeter: '', isActive: true,
})

export const draftFromPlan = (p: MaintenancePlanView): PlanDraft => ({
  description: p.description, frequencyType: p.frequencyType, frequencyValue: String(p.frequencyValue), leadDays: String(p.leadDays),
  supplier: p.defaultSupplierPartyId ? { id: p.defaultSupplierPartyId, label: p.defaultSupplierName ?? 'Fornecedor' } : null,
  checklist: p.checklist.length ? [...p.checklist] : [''], lastDoneAt: p.lastDoneAt ?? '', lastDoneMeter: p.lastDoneMeter != null ? String(p.lastDoneMeter) : '', isActive: p.isActive,
})

/** Número digitado ("1.234,5" ou "1234.5") em número; vazio ou inválido vira null. */
export function parseNumber(text: string): number | null {
  // O mesmo parser dos campos numéricos (FND-09d CA 5): "1.234,5", "1.234" (milhar) e "1234.5" (colado ou já normalizado).
  const normalized = parseBrDecimal(text, 4)
  return normalized == null ? null : Number(normalized)
}

/** Erros locais do plano (o servidor confere de novo). */
export function planErrors(d: PlanDraft): Partial<Record<string, string>> {
  const e: Partial<Record<string, string>> = {}
  if (!d.description.trim()) e.description = 'Informe a descrição do plano.'
  const value = parseNumber(d.frequencyValue)
  if (value == null || !Number.isInteger(value) || value < 1) e.frequencyValue = d.frequencyType === 'Days' ? 'Informe a cada quantos dias.' : 'Informe a cada quantas horas de uso.'
  const lead = parseNumber(d.leadDays)
  if (lead == null || !Number.isInteger(lead) || lead < 0 || lead > 90) e.leadDays = 'Informe a antecedência de 0 a 90 dias.'
  if (d.frequencyType === 'Days' && !d.lastDoneAt) e.lastDoneAt = 'Informe a data da última execução.'
  if (d.frequencyType === 'Hours' && (parseNumber(d.lastDoneMeter) ?? -1) < 0) e.lastDoneMeter = 'Informe o horímetro da última execução.'
  return e
}

/** Corpo do plano para a API. */
export function planInput(d: PlanDraft, version?: number) {
  return {
    description: d.description.trim(), frequencyType: d.frequencyType, frequencyValue: parseNumber(d.frequencyValue), leadDays: parseNumber(d.leadDays),
    defaultSupplierPartyId: d.supplier?.id ?? null, checklist: d.checklist.map((c) => c.trim()).filter(Boolean),
    lastDoneAt: d.lastDoneAt || null, lastDoneMeter: d.frequencyType === 'Hours' ? parseNumber(d.lastDoneMeter) : null,
    isActive: version === undefined ? undefined : d.isActive, version,
  }
}
