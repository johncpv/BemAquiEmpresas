import type { LaborEntryView } from '@/api/generated/model'
import { CLOSE_KIND } from './fabrication'

/** "2026-10-22T14:30" do campo datetime-local ↔ instante ISO (horário do aparelho, que na fábrica é o de São Paulo). */
export function toLocalInput(iso: string) {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

export const fromLocalInput = (value: string) => new Date(value).toISOString()

/** Marcas do apontamento (CA 18, 20, 21, 31): "Apontado por", "Encerrado por", "Corrigido", "Autoaprovado". */
export function laborMarks(e: Pick<LaborEntryView, 'onBehalf' | 'recordedByName' | 'closeKind' | 'closedByName' | 'correctedAt' | 'selfApproved' | 'endedAt'>) {
  const marks: string[] = []
  if (e.onBehalf) marks.push(`Apontado por ${e.recordedByName ?? 'supervisor'}`)
  if (!e.endedAt) marks.push('Em aberto')
  else if (e.closeKind === 'StepCompleted') marks.push(`Encerrado por ${e.closedByName ?? 'outro operador'}`)
  else if (e.closeKind && e.closeKind !== 'Operator' && !e.onBehalf) marks.push(CLOSE_KIND[e.closeKind] ?? e.closeKind)
  if (e.correctedAt) marks.push('Corrigido')
  if (e.selfApproved) marks.push('Autoaprovado')
  return marks
}