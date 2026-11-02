import type { CostType, PayableDetail, PaymentDetailsInput, PaymentMethodKind } from '@/api/generated/model'
import { listFinanceProjects } from '@/api/generated/bemaqui'
import type { ComboOption } from '@/components/form/combobox'
import { type Tone, toNumber } from '../receivables/receivables'

/** Situação exibida (mapa de chips da Fase 2, FIN-02): "Vencido" e "Vence hoje" calculados no servidor. */
export function payableSituation(p: Pick<PayableDetail, 'status' | 'isOverdue' | 'isDueToday'>): { label: string; tone: Tone } {
  if (p.status === 'Cancelled') return { label: 'Cancelado', tone: 'neutral' }
  if (p.status === 'Paid') return { label: 'Pago', tone: 'ok' }
  if (p.isOverdue) return { label: 'Vencido', tone: 'crit' }
  if (p.isDueToday) return { label: 'Vence hoje', tone: 'warn' }
  switch (p.status) {
    case 'Planned': return { label: 'Previsto', tone: 'neutral' }
    case 'AwaitingApproval': return { label: 'Em OP', tone: 'info' }
    case 'Approved': return { label: 'Aprovado', tone: 'ok' }
    case 'PartiallyPaid': return { label: 'Parcial', tone: 'info' }
    default: return { label: 'Em aberto', tone: 'info' }
  }
}

export const PAYABLE_SITUATIONS = [
  { value: 'aberto', label: 'Em aberto' },
  { value: 'vencido', label: 'Vencidos' },
  { value: 'previsto', label: 'Previstos' },
  { value: 'em-op', label: 'Em ordem de pagamento' },
  { value: 'pago', label: 'Pagos' },
  { value: 'cancelado', label: 'Cancelados' },
]

export const METHOD_LABELS: Record<string, string> = { Pix: 'Pix', Boleto: 'Boleto', Ted: 'TED', Other: 'Outro', EmployeeAccount: 'Conta do funcionário' }

export const ORIGIN_LABELS: Record<string, string | undefined> = {
  Recurring: 'Recorrente', Installer: 'Instalação', Purchase: 'Compra', GoodsReceipt: 'Recebimento de compra', IncomingInvoice: 'Nota de entrada', Payroll: 'Folha', PayrollForecast: 'Folha prevista', Maintenance: 'Manutenção', Opening: 'Implantação',
}

/** Link do documento de origem (CMP-04 CA 18): o título do recebimento aponta para o recebimento; o previsto, para o pedido. */
export const ORIGIN_LINKS: Record<string, string | undefined> = { Purchase: '/app/compras/pedidos/', GoodsReceipt: '/app/compras/recebimentos/', IncomingInvoice: '/app/fiscal/entrada/', Maintenance: '/app/patrimonio/manutencao/' }

export const PAYABLE_DICAS = {
  retencoes: 'Preencha quando a nota do prestador destacar retenção. O sistema não calcula.',
  forma: 'Para onde o dinheiro vai. É obrigatória para o título entrar numa ordem de pagamento, e quem aprova e quem paga veem estes dados.',
  titular: 'Quem recebe o dinheiro. Se não for o fornecedor, o sistema avisa na ordem de pagamento.',
  antecedencia: 'Quantos dias antes do vencimento o título do mês é lançado em A pagar.',
}

export type MethodDraft = Required<{ [K in keyof PaymentDetailsInput]: string }> & { kind: '' | PaymentMethodKind }

export const emptyMethod = (): MethodDraft => ({ kind: '', pixKeyType: 'Cnpj', pixKey: '', holderName: '', holderDocument: '', barcode: '', bankCode: '', agency: '', account: '', accountType: 'Checking', description: '' })

/** Rascunho → corpo da API; sem tipo, sem forma de pagamento. */
export function toDetails(m: MethodDraft): PaymentDetailsInput | null {
  if (!m.kind) return null
  const t = (s: string) => s.trim() || null
  switch (m.kind) {
    case 'Pix': return { kind: 'Pix', pixKeyType: m.pixKeyType as PaymentDetailsInput['pixKeyType'], pixKey: t(m.pixKey), holderName: t(m.holderName), holderDocument: t(m.holderDocument) }
    case 'Boleto': return { kind: 'Boleto', barcode: t(m.barcode) }
    case 'Ted': return { kind: 'Ted', bankCode: t(m.bankCode), agency: t(m.agency), account: t(m.account), accountType: m.accountType, holderName: t(m.holderName), holderDocument: t(m.holderDocument) }
    default: return { kind: 'Other', description: t(m.description) }
  }
}

export interface RetentionDraft { iss: string | null; inss: string | null; ir: string | null; pcc: string | null }

export const retentionSum = (r: RetentionDraft) => toNumber(r.iss) + toNumber(r.inss) + toNumber(r.ir) + toNumber(r.pcc)

const COST_BY_ACCOUNT: Record<string, CostType> = { '2.1': 'Material', '2.2': 'Labor', '2.3': 'Installation', '2.4': 'Freight', '2.5': 'Tax', '2.6': 'Other' }

/** Natureza sugerida pela conta (COM-04a CA 18): 2.1 Materiais → Material … 2.6 Outros custos → Outros; a subconta segue a mãe. */
export function suggestCostType(accountCode: string | undefined): CostType | null {
  if (!accountCode) return null
  const key = accountCode.split('.').slice(0, 2).join('.')
  return COST_BY_ACCOUNT[key] ?? null
}

/** Projetos abertos para o campo "Projeto" dos títulos (COM-04a CA 18). */
export const searchProjects = async (q: string): Promise<ComboOption[]> =>
  (await listFinanceProjects({ q: q || undefined })).map((p) => ({ id: p.id, label: `${p.number} — ${p.name}` }))
