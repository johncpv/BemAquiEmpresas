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
