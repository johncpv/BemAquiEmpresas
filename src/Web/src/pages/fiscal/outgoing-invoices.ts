import type { OutgoingInvoiceStatus } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Situação da nota (CA 17): Rascunho neutro, Processando info, Contingência alerta, Autorizada ok, Rejeitada e Denegada crítico, Cancelada neutro. */
export const INVOICE_STATUS: Record<OutgoingInvoiceStatus, { label: string; tone: StatusTone }> = {
  Draft: { label: 'Rascunho', tone: 'neutral' },
  Processing: { label: 'Processando', tone: 'info' },
  ContingencyPending: { label: 'Contingência', tone: 'warn' },
  Authorized: { label: 'Autorizada', tone: 'ok' },
  Rejected: { label: 'Rejeitada', tone: 'crit' },
  Denied: { label: 'Denegada', tone: 'crit' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
}

export const INVOICE_STATUSES = Object.keys(INVOICE_STATUS) as OutgoingInvoiceStatus[]

/** Textos da tela (verbos e confirmações da story). */
export const INVOICE_TEXTS = {
  saved: 'Rascunho salvo',
  sent: 'Nota enviada à SEFAZ',
  reopened: 'Rascunho reaberto',
  resent: 'XML e DANFE reenviados',
  discarded: 'Rascunho descartado',
  waiting: 'Nota enviada à SEFAZ. Aguardando a autorização…',
  slow: 'A SEFAZ está demorando. Avisaremos quando a nota for autorizada.',
  emptyQueue: 'Nenhum romaneio esperando nota.',
  emptyIssued: 'Nenhuma nota emitida. Emita a nota a partir de um romaneio expedido.',
  installments: 'A parcela do contrato é só cobrança. A receita conta pela data desta nota.',
} as const

/** Dicas da story ("Telas e estados"). */
export const INVOICE_DICAS = {
  chave: 'Os 44 números que identificam a nota na SEFAZ. O cliente consulta a nota por ela.',
  contingencia: 'A SEFAZ não respondeu a tempo. O Emissor Fiscal confere sozinho e a nota é autorizada depois; não emita de novo.',
  rejeitada: 'A SEFAZ recusou a nota por um erro nos dados. Corrija o que o motivo diz e emita de novo; o número gasto será inutilizado.',
  denegada: 'A SEFAZ negou a nota por irregularidade fiscal do destinatário. A nota não pode ser corrigida: fale com a contadora.',
  regra: 'A regra fiscal ativa que vale para este cliente e este item. Ela define o CFOP, os CST e as alíquotas.',
  ambiente: 'Homologação é o ambiente de teste da SEFAZ: a nota não tem valor fiscal. Produção é a nota de verdade.',
  parcelas: 'A parcela do contrato é só cobrança. A receita conta pela data desta nota.',
  vendaMista: 'Venda mista: o equipamento e as peças saem em NF-e na expedição; a instalação sai em NFS-e no aceite técnico.',
} as const

/** FIS-03b: verbos e confirmações do cancelamento, do pedido, da carta e da inutilização. */
export const EVENT_TEXTS = {
  cancelled: 'NF-e cancelada',
  requested: 'Pedido de cancelamento enviado',
  declined: 'Pedido recusado',
  corrected: 'Carta de correção registrada',
  invalidated: 'Números inutilizados',
  onlyOwner: 'Só o Proprietário cancela notas fiscais.',
  outOfWindow: 'Fora do prazo de cancelamento. A devolução fica com a contadora.',
  sent: 'Cancelamento enviado. Aguardando a SEFAZ…',
  slow: 'A SEFAZ está demorando para confirmar o cancelamento. A conferência automática confirma depois; não peça de novo.',
  noEvents: 'Nenhum evento nesta nota.',
  noGaps: 'Nenhum número pendente.',
  correctionLimits: 'A carta de correção não pode mudar valores, impostos, quantidades, destinatário nem a data de emissão.',
  irreversible: 'Esta ação não pode ser desfeita.',
} as const

export const EVENT_DICAS = {
  prazo: 'A SEFAZ aceita o cancelamento só por um tempo depois da autorização (24 horas, em Configurações → Fiscal). Depois, a saída é a nota de devolução, com a contadora.',
  carta: 'A carta de correção (CC-e) corrige um dado simples da nota sem cancelá-la. Cada nota aceita até 20; a última vale sobre as anteriores.',
  inutilizar: 'Inutilizar avisa a SEFAZ que um número gasto não vai virar nota. A SEFAZ exige que a lacuna na numeração seja explicada.',
  devolucao: 'Passado o prazo, a nota não é mais cancelada: a contadora emite a nota de devolução (entrada) que anula a venda.',
} as const

export const EVENT_STATUS: Record<'Requested' | 'Registered' | 'Rejected', { label: string; tone: StatusTone }> = {
  Requested: { label: 'Enviado', tone: 'info' },
  Registered: { label: 'Registrado', tone: 'ok' },
  Rejected: { label: 'Recusado', tone: 'crit' },
}

/**
 * Indício de mudança proibida na carta de correção (CA 9): valor em R$, CNPJ/CPF ou data no texto. Só avisa (âmbar), nunca bloqueia.
 */
export function correctionWarnings(text: string): string[] {
  const warnings: string[] = []
  if (/R\$\s*\d|\b\d{1,3}(\.\d{3})*,\d{2}\b/.test(text)) warnings.push('O texto parece mudar um valor em R$. A carta de correção não muda valores nem impostos.')
  if (/\b\d{2}\.?\d{3}\.?\d{3}\/?\d{4}-?\d{2}\b|\b\d{3}\.\d{3}\.\d{3}-\d{2}\b|\b\d{11}\b/.test(text)) warnings.push('O texto parece ter um CNPJ ou CPF. A carta de correção não muda o destinatário.')
  if (/\b\d{1,2}\/\d{1,2}\/\d{2,4}\b/.test(text)) warnings.push('O texto parece ter uma data. A carta de correção não muda a data de emissão.')
  return warnings
}

/** Faixa de números para o impacto em lista ("10, 11, 12"); longa demais, resume. */
export function rangeNumbers(first: number, last: number, max = 50): number[] {
  const all: number[] = []
  for (let n = first; n <= last && all.length < max; n++) all.push(n)
  return all
}

/** Processando há mais de 30 s ou em contingência: "Demorando" (CA 10). */
export function isSlow(status: OutgoingInvoiceStatus, processingSince: string | null | undefined, now = Date.now()): boolean {
  if (status === 'ContingencyPending') return true
  if (status !== 'Processing' || !processingSince) return false
  return now - new Date(processingSince).getTime() > 30_000
}

/** "NF-e 2/42" ou "Rascunho de nota". */
export function invoiceLabel(series: number | null | undefined, number: number | null | undefined): string {
  return number ? `NF-e ${series ?? ''}/${number}` : 'Rascunho de nota'
}

/** Quantidade em pt-BR com até 4 casas. */
export function quantity(value: number): string {
  return value.toLocaleString('pt-BR', { maximumFractionDigits: 4 })
}
