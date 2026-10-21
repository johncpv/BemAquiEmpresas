import type { Tone } from '../../finance/receivables/receivables'

export const INSTALLATION_STATUS: Record<string, { label: string; tone: Tone }> = {
  Open: { label: 'Aberta', tone: 'info' },
  InProgress: { label: 'Em instalação', tone: 'warn' },
  Completed: { label: 'Concluída', tone: 'ok' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
}

export const INSTALLATION_SITUATIONS = [
  { value: 'aberta', label: 'Abertas' },
  { value: 'andamento', label: 'Em instalação' },
  { value: 'concluida', label: 'Concluídas' },
  { value: 'cancelada', label: 'Canceladas' },
]

export const INSTALLATION_DICAS = {
  aceite: 'Confirma que a instalação foi entregue ao cliente. Confirma o título do instalador e libera a parcela do cliente ligada ao aceite.',
  valor: 'Valor que a empresa combinou pagar ao instalador por esta OS. Vira um título a pagar previsto até o aceite.',
  estimado: 'Vencimento estimado pela data prevista de fim. Fica certo no aceite técnico.',
}

/** Etapa atual para o indicador: a primeira não concluída; com todas concluídas, a última. */
export function currentStep(steps: { completedAt?: string | null }[]) {
  const index = steps.findIndex((s) => !s.completedAt)
  return index < 0 ? steps.length - 1 : index
}

/** O que falta para o aceite (CA 11): etapas antes da entrega técnica e itens do checklist sem marca. */
export function acceptanceMissing(steps: { name: string; completedAt?: string | null }[], checklist: { text: string; result?: string | null }[]) {
  return [
    ...steps.slice(0, -1).filter((s) => !s.completedAt).map((s) => `Etapa ${s.name}`),
    ...checklist.filter((c) => !c.result).map((c) => `Checklist: ${c.text}`),
  ]
}
