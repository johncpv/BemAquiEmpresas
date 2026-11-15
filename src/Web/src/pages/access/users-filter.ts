/** Filtro "Mostrar" da lista de usuários (USR-01 CA 7): quem não tem funcionário (externos) e quem não tem grupo (não vê nada). */
export const USER_FILTER_KEY = 'mostrar'

export const USER_FILTER_OPTIONS = [
  { value: 'sem-funcionario', label: 'Sem funcionário' },
  { value: 'sem-grupo', label: 'Sem grupo' },
]

interface FilterableUser {
  employeeId?: string | null
  groups: unknown[]
}

export function filterUsers<T extends FilterableUser>(rows: T[], filter: string | undefined): T[] {
  if (filter === 'sem-funcionario') return rows.filter((u) => !u.employeeId)
  if (filter === 'sem-grupo') return rows.filter((u) => u.groups.length === 0)
  return rows
}

/** Texto da coluna "Funcionário": o nome (desligado marcado) ou "Externo" quando não há vínculo. */
export function employeeLabel(u: { employeeId?: string | null; employeeName?: string | null; employeeTerminated?: boolean }): string {
  if (!u.employeeId) return 'Externo'
  const name = u.employeeName ?? 'Funcionário'
  return u.employeeTerminated ? `${name} (desligado)` : name
}
