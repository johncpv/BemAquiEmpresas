import { cleanup, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { NavigationContext, type NavigationValue } from '@/navigation/navigation-context'
import type { NavModule } from '@/navigation/modules'
import { renderWithRouter } from '@/test-utils'
import { ProximosPassos } from './Inicio'

afterEach(cleanup)

const mod = (code: string, actions: string[]) => ({ code, locked: false, actions }) as unknown as NavModule
const nav = (all: NavModule[]): NavigationValue => ({ areas: [], all, isOwner: false, tenantName: 'Konebras', status: 'ready', retry: () => undefined, vocabulary: {}, areaLabels: {} })

function renderWith(all: NavModule[]) {
  return renderWithRouter(() => (
    <NavigationContext.Provider value={nav(all)}>
      <h1>Início</h1>
      <ProximosPassos />
    </NavigationContext.Provider>
  ))
}

describe('Início: próximos passos só para quem administra os acessos (operação DEMO 15/11)', () => {
  it('administrador vê os três cartões', async () => {
    renderWith([mod('identity', ['view', 'create', 'edit']), mod('settings', ['view', 'edit'])])
    expect(await screen.findByRole('heading', { name: 'Próximos passos' })).toBeTruthy()
    for (const title of ['Convidar a equipe', 'Revisar os grupos', 'Proteger as contas']) expect(screen.getByText(title)).toBeTruthy()
  })

  it('sem permissão de acessos nem de configurações, a seção some', async () => {
    renderWith([mod('finance', ['view', 'create', 'edit', 'approve']), mod('identity', ['view']), mod('settings', ['view'])])
    await screen.findByRole('heading', { name: 'Início' })
    expect(screen.queryByRole('heading', { name: 'Próximos passos' })).toBeNull()
    expect(screen.queryByText('Convidar a equipe')).toBeNull()
  })
})
