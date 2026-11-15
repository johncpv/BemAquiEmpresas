import { describe, expect, it } from 'vitest'
import { employeeLabel, filterUsers } from './users-filter'

const rows = [
  { id: 'a', employeeId: 'e1', employeeName: 'Ana', groups: [{}] },
  { id: 'b', employeeId: null, groups: [{}] },
  { id: 'c', employeeId: 'e2', employeeName: 'Caio', employeeTerminated: true, groups: [] },
]

describe('filtro da lista de usuários (USR-01)', () => {
  it('"Sem funcionário" mostra só os externos', () => expect(filterUsers(rows, 'sem-funcionario').map((r) => r.id)).toEqual(['b']))
  it('"Sem grupo" mostra quem não tem nenhum grupo', () => expect(filterUsers(rows, 'sem-grupo').map((r) => r.id)).toEqual(['c']))
  it('sem filtro, todos', () => expect(filterUsers(rows, undefined)).toHaveLength(3))
  it('a coluna diz "Externo" sem vínculo e marca o desligado', () => {
    expect(employeeLabel(rows[1])).toBe('Externo')
    expect(employeeLabel(rows[0])).toBe('Ana')
    expect(employeeLabel(rows[2])).toBe('Caio (desligado)')
  })
})
