import { describe, expect, it } from 'vitest'
import { toggleAction, toggleEnabled } from './group-matrix'

const ORDER = ['view', 'create', 'edit', 'approve', 'delete', 'view_sensitive']

describe('regras da matriz no cliente (FND-06b CA 1 a 3)', () => {
  it('habilitar marca Ver', () => expect(toggleEnabled(true)).toEqual({ enabled: true, actions: ['view'] }))

  it('desmarcar Ver limpa o módulo', () =>
    expect(toggleAction({ enabled: true, actions: ['view', 'edit'] }, 'view', false, ORDER)).toEqual({ enabled: false, actions: [] }))

  it('marcar outra ação (inclusive específica) marca Ver e Habilitado', () =>
    expect(toggleAction({ enabled: false, actions: [] }, 'view_sensitive', true, ORDER)).toEqual({ enabled: true, actions: ['view', 'view_sensitive'] }))
})
