import type { BillingSlipCancelReason, BillingSlipDraft, BillingSlipStatus } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** 409 da baixa manual com boleto aberto (BNK-01b CA 17): a recuperação reenvia com cancelBillingSlip. */
export const BILLING_SLIP_OPEN = 'billing_slip_open'
export const BILLING_SLIP_EXISTS = 'billing_slip_exists'
export const MISSING_INTEGRATION = 'bank_integration_missing'

/** Padrão 6 (emissão assíncrona no Inter): "Aguardando" depois de 3 s, "Demorando" depois de 30 s. */
export const WAITING_AFTER_MS = 3_000
export const SLOW_AFTER_MS = 30_000
export const POLL_MS = 5_000
export const SLOW_TEXT = 'O Inter está demorando. Avisaremos quando o boleto estiver pronto.'

export type IssuePhase = 'sent' | 'waiting' | 'slow'

export const PHASE_TEXT: Record<IssuePhase, string> = {
  sent: 'Enviado ao Inter.',
  waiting: 'Aguardando o banco confirmar a emissão…',
  slow: SLOW_TEXT,
}

/** Fase do padrão 6 pelo tempo desde o pedido. */
export function issuePhase(requestedAt: string, now: number): IssuePhase {
  const elapsed = now - new Date(requestedAt).getTime()
  return elapsed >= SLOW_AFTER_MS ? 'slow' : elapsed >= WAITING_AFTER_MS ? 'waiting' : 'sent'
}

/** Situação do boleto em chip (o estado nunca é só cor). */
export function slipStatus(status: BillingSlipStatus, cancelPending = false): { label: string; tone: StatusTone } {
  if (cancelPending) return { label: 'Cancelamento enviado', tone: 'warn' }
  switch (status) {
    case 'Requested': return { label: 'Boleto em emissão', tone: 'info' }
    case 'Issued': return { label: 'Boleto emitido', tone: 'ok' }
    case 'Failed': return { label: 'Emissão recusada', tone: 'crit' }
    case 'Paid': return { label: 'Boleto pago', tone: 'ok' }
    case 'Cancelled': return { label: 'Boleto cancelado', tone: 'neutral' }
    case 'Expired': return { label: 'Boleto expirado', tone: 'warn' }
    case 'PaidPendingReview': return { label: 'Aguardando conferência', tone: 'warn' }
    default: return { label: String(status ?? ''), tone: 'neutral' }
  }
}

/** Motivos do "Cancelar boleto" (CA 16): lista mapeada para o Inter no servidor. */
export type CancelReason = NonNullable<BillingSlipCancelReason>

export const CANCEL_REASONS: { value: CancelReason; label: string }[] = [
  { value: 'PaidOtherMeans', label: 'Pagamento por outro meio' },
  { value: 'CustomerRequest', label: 'Pedido do cliente' },
  { value: 'IssueError', label: 'Erro na emissão' },
  { value: 'Replacement', label: 'Substituição por outro boleto' },
]

export const APPROVAL_MESSAGE = 'Desconto acima do limite: só quem aprova no Financeiro pode emitir.'

/** CA 5 no cliente (a API confere de novo): desconto acima do % do valor ou do valor do limite, ou multa e juros acima dos padrões. */
export function needsApproval(draft: Pick<BillingSlipDraft, 'amount' | 'discountLimitPct' | 'discountLimitAmount' | 'finePct' | 'interestMonthPct'>, discount: number, fine: number, interest: number): boolean {
  const pct = draft.amount > 0 ? (discount / draft.amount) * 100 : 100
  return (discount > 0 && (pct > draft.discountLimitPct || discount > draft.discountLimitAmount)) || fine > draft.finePct || interest > draft.interestMonthPct
}

/** Mensagem ao pagador: até 3 linhas de até 78 caracteres. */
export function messageError(message: string): string | undefined {
  const lines = message.split('\n').map((l) => l.trim()).filter((l) => l.length > 0)
  if (lines.length > 3) return 'Use até 3 linhas.'
  if (lines.some((l) => l.length > 78)) return 'Cada linha tem até 78 caracteres.'
  return undefined
}

export const BOLETO_DICAS = {
  seuNumero: 'Número do boleto criado pelo sistema para o banco identificar a cobrança. É curto e não revela o número do título.',
  nossoNumero: 'Número que o banco dá ao boleto. Aparece no extrato e no comprovante do cliente.',
  multa: 'Percentual cobrado uma vez quando o boleto é pago depois do vencimento (até 2%). Vem de Configurações → Cobrança.',
  juros: 'Juros por mês de atraso, proporcionais aos dias (até 1% ao mês). Vem de Configurações → Cobrança.',
  pix: 'Código que o cliente cola no aplicativo do banco para pagar na hora pelo Pix, sem digitar a linha digitável.',
}
