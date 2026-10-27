import type { QuoteScheduleSimulationView } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'

/** Textos da simulação de prazo (PLN-05); a ajuda (Ajuda/comercial-orcamento.md) usa as mesmas palavras. */
export const SIMULATION_TEXT = {
  loading: 'Calculando o prazo…',
  noHours: 'Este orçamento não tem horas por centro de trabalho. Informe as horas na composição de custo para simular.',
  idle: 'A fábrica está livre: o prazo depende só das horas do orçamento.',
  material: 'Não considera o prazo de compra do material.',
  limited: 'Aguarde alguns segundos para simular de novo.',
  error: 'Não foi possível simular o prazo agora. Tente de novo; se continuar, fale com o suporte.',
}

export const SIMULATION_DICAS = {
  simular:
    'Calcula, sem gravar nada, em que data a fábrica termina e entrega se o cliente aprovar hoje: as horas por centro da composição entram na fila de produção junto com os projetos em andamento.',
  posicao:
    'Por padrão o orçamento entra no fim da fila. Escolha uma posição para ver o prazo se ele passar na frente; a simulação mostra quantos projetos passam a atrasar.',
}

/** Motivo de não ter data (código da PLN-01b) em texto. */
export function noForecastText(reason: string | null | undefined): string {
  switch (reason) {
    case 'NoCapacity':
      return 'Algum centro de trabalho do orçamento não tem capacidade nos próximos 2 anos. Confira os turnos e o calendário do centro.'
    case 'NoWorkCenter':
      return 'Alguma etapa ficou sem centro de trabalho. Informe o centro nas linhas de Mão de obra.'
    default:
      return 'Não foi possível calcular a data com o plano de hoje.'
  }
}

/** "Se aprovado hoje, a fabricação termina em {data} e a entrega fica em {data}" (CA 4). */
export function simulationHeadline(view: QuoteScheduleSimulationView): string | null {
  if (!view.fabricationEnd || !view.deliveryDate) return null
  const installation = view.includesInstallation ? ', com o prazo de instalação' : ''
  return `Se aprovado hoje, a fabricação termina em ${formatDate(view.fabricationEnd)} e a entrega fica em ${formatDate(view.deliveryDate)}${installation}.`
}

/** "Prazo prometido no orçamento: {n} dias → {data}" (CA 4); null sem prazo. */
export function promiseLine(view: QuoteScheduleSimulationView): string | null {
  if (view.promisedDays === null || view.promisedDays === undefined || !view.promisedDate) return null
  return `Prazo prometido no orçamento: ${view.promisedDays} ${view.promisedDays === 1 ? 'dia' : 'dias'} → ${formatDate(view.promisedDate)}`
}

/** Chip "Cabe no prazo" ou "{n} dias além do prazo" (CA 4). */
export function promiseChip(view: QuoteScheduleSimulationView): { tone: StatusTone; label: string } | null {
  const beyond = view.daysBeyondPromise
  if (beyond === null || beyond === undefined) return null
  if (beyond <= 0) return { tone: 'ok', label: 'Cabe no prazo' }
  return { tone: 'crit', label: `${beyond} ${beyond === 1 ? 'dia' : 'dias'} além do prazo` }
}

/** "Projetos que passam a atrasar: {n}" (CA 4), só quando a posição não é o fim; os números e nomes com projects.view. */
export function lateLine(view: QuoteScheduleSimulationView): string | null {
  if (view.atEnd && view.newlyLateCount === 0) return null
  const names = view.newlyLateProjects?.map((p) => `${p.number} ${p.name}`.trim()) ?? []
  return `Projetos que passam a atrasar: ${view.newlyLateCount}${names.length > 0 ? ` (${names.join(', ')})` : ''}`
}

/** "No fim da fila (posição {n})" ou "Na posição {n} de {total}". */
export function positionText(view: QuoteScheduleSimulationView): string {
  return view.atEnd ? `No fim da fila (posição ${view.queuePosition})` : `Na posição ${view.queuePosition} de ${view.queueSize + 1}`
}

/** Posição digitada válida (1 até o tamanho da fila + 1). */
export function validPosition(value: string, queueSize: number): number | null {
  const n = Number(value)
  return Number.isInteger(n) && n >= 1 && n <= queueSize + 1 ? n : null
}
