import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import { Markdown } from '@/help/markdown'
import { NavigationContext } from './navigation-context'
import { Term } from './Term'
import { applyTerms } from './vocabulary'

const RETAIL = { 'area.factory': 'Retaguarda' }

const withTenant = (vocabulary: Record<string, string>, node: ReactNode) =>
  render(<NavigationContext.Provider value={{ areas: [], all: [], isOwner: false, tenantName: 'Loja', status: 'ready', retry: () => undefined, vocabulary, areaLabels: {} }}>{node}</NavigationContext.Provider>)

describe('vocabulário por perfil (PERF-03)', () => {
  it('o componente de texto traduz a chave conhecida e mantém o padrão nas demais (CA 4)', () => {
    withTenant(RETAIL, <p><Term k="area.factory">Fábrica</Term> e <Term k="chave.sem.termo">Itens</Term></p>)
    expect(screen.getByText('Retaguarda e Itens')).toBeTruthy()
  })

  it('a fábrica (vocabulário vazio) e telas fora do provider ficam com o termo padrão (CA 8)', () => {
    withTenant({}, <p><Term k="area.factory">Fábrica</Term></p>)
    expect(screen.getByText('Fábrica')).toBeTruthy()
    expect(applyTerms('na {{area.factory|fábrica}}', {})).toBe('na fábrica')
  })

  it('a ajuda mostra o termo como o tenant o vê, respeitando a minúscula (CA 7)', () => {
    const { container } = withTenant(RETAIL, <Markdown text={'A área **{{area.factory|Fábrica}}** fica no dock.\n- Abra a {{area.factory|fábrica}}.'} />)
    expect(container.querySelector('strong')?.textContent).toBe('Retaguarda')
    expect(container.querySelector('li')?.textContent).toBe('Abra a retaguarda.')
    expect(container.textContent).not.toContain('{{')
  })
})
