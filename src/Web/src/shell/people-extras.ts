import { Clock, FileText, type LucideIcon } from 'lucide-react'
import { useGetMyEmployeeLink } from '@/api/generated/bemaqui'
import type { NavArea } from '@/navigation/modules'

/** Link pessoal fora do catálogo de módulos, listado junto com a área (Pessoas). */
export interface ExtraLink {
  to: '/app/meu-ponto' | '/app/meus-holerites'
  name: string
  icon: LucideIcon
}

const PEOPLE_EXTRAS: readonly ExtraLink[] = [
  { to: '/app/meu-ponto', name: 'Meu ponto', icon: Clock },
  { to: '/app/meus-holerites', name: 'Meus holerites', icon: FileText },
]

const NONE: readonly ExtraLink[] = []

/**
 * Pessoas (RH-02b, RH-01a): "Meu ponto" e "Meus holerites" para quem está ligado a funcionário ativo ou afastado (ADR 0035).
 * Vale no menu da área (dock do celular e menu lateral recolhido) e no menu lateral aberto.
 */
export function usePeopleExtras(area: NavArea): readonly ExtraLink[] {
  const employee = useGetMyEmployeeLink({ query: { retry: false, staleTime: 300_000, enabled: area.id === 'people' } })
  return area.id === 'people' && employee.isSuccess ? PEOPLE_EXTRAS : NONE
}
