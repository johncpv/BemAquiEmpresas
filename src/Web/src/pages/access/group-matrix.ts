/** Linha da matriz em edição: módulo, chave "Habilitado" e ações marcadas. */
export interface MatrixRow {
  enabled: boolean
  actions: string[]
}

/**
 * Regras da matriz (FND-06b CA 1 a 3), as mesmas do servidor (GroupMatrix.Normalize): habilitar marca "Ver"; desmarcar
 * "Ver" limpa o módulo; marcar outra ação marca "Ver" e "Habilitado".
 */
export function toggleEnabled(on: boolean): MatrixRow {
  return on ? { enabled: true, actions: ['view'] } : { enabled: false, actions: [] }
}

export function toggleAction(row: MatrixRow, action: string, checked: boolean, order: readonly string[]): MatrixRow {
  const set = new Set(row.actions)
  if (checked) {
    set.add(action)
    set.add('view')
  } else if (action === 'view') {
    set.clear()
  } else {
    set.delete(action)
  }

  const actions = order.filter((a) => set.has(a))
  return { enabled: actions.length > 0, actions }
}
