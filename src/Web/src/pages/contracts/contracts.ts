import type { Tone } from '../finance/receivables/receivables'

export const CONTRACT_STATUS: Record<string, { label: string; tone: Tone }> = {
  Active: { label: 'Ativo', tone: 'ok' },
  Completed: { label: 'Concluído', tone: 'info' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
}

/** Etapas do projeto (COM-04a CA 17): letra explícita, porque "Entregue" é T. O cancelado não entra (chip neutro). */
export const PROJECT_STAGES = ['Planejado', 'Em produção', 'Fabricado', 'Instalando', 'Entregue', 'Encerrado'] as const
export const PROJECT_LETTERS = ['P', 'E', 'F', 'I', 'T', 'C'] as const
const ORDER = ['Planned', 'InProgress', 'Fabricated', 'Installing', 'Delivered', 'Closed']

/** Índice da etapa; -1 no cancelado. */
export const projectStage = (status: string) => ORDER.indexOf(status)

/** "Gerando parcelas…" até os títulos existirem; depois de ~30 s, o aviso de demora (CA 11, ADR 0019 §5). */
export function receivablesState(receivables: number, createdAt: string, now = Date.now()): 'ready' | 'pending' | 'slow' {
  if (receivables > 0) return 'ready'
  return now - Date.parse(createdAt) > 30_000 ? 'slow' : 'pending'
}

export const CONTRACT_DICAS = {
  snapshot: 'Cópia congelada do orçamento aprovado; muda só por aditivo.',
  assinatura: 'Data em que o cliente assinou. Padrão hoje; não pode ser no futuro.',
  receita: 'Conta do plano de contas onde entram as parcelas do contrato. Sugerida: 1.1 Venda de equipamentos.',
}
