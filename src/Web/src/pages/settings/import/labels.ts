import type { StatusTone } from '@/components/ui/status-chip'

export const MODE_LABELS: Record<string, string> = { DryRun: 'Simulação', Real: 'Real' }

export const STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Succeeded: { label: 'Concluída', tone: 'ok' },
  Failed: { label: 'Falhou', tone: 'crit' },
  Running: { label: 'Em andamento', tone: 'info' },
}

export const KIND: Record<string, { label: string; tone: StatusTone }> = {
  error: { label: 'Erro', tone: 'crit' },
  conflict: { label: 'Conflito', tone: 'warn' },
  warning: { label: 'Aviso', tone: 'info' },
}

export const KIND_OPTIONS = Object.entries(KIND).map(([value, k]) => ({ value, label: k.label }))

export const ENTITY_LABELS: Record<string, string> = {
  parties: 'Terceiros',
  contacts: 'Contatos',
  'payment-terms': 'Condições de pagamento',
  'bank-accounts': 'Contas bancárias',
  units: 'Unidades',
  categories: 'Categorias',
  locations: 'Locais',
  items: 'Itens',
  'supplier-codes': 'Códigos do fornecedor',
}

export const entityLabel = (entity: string) => ENTITY_LABELS[entity] ?? entity

/** Link para o registro no BemAqui, quando o destino já tem tela (CA 19). */
export function targetHref(type: string | null, id: string | null): string | undefined {
  if (!type || !id) return undefined
  if (type === 'party') return `/app/cadastros/terceiros/${id}`
  if (type === 'bank_account') return `/app/financeiro/contas?conta=${id}`
  if (type === 'payment_term') return `/app/financeiro/condicoes-de-pagamento/${id}`
  return undefined
}
