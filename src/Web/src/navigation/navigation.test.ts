import { describe, expect, it } from 'vitest'
import { buildNavigation, type PermissionModule } from './modules'

const m = (code: string, area: string, released: boolean, enabled: boolean): PermissionModule => ({ code, name: code, area, released, enabled, actions: enabled ? ['view'] : [] })

describe('menu por permissão (FND-06d)', () => {
  const nav = buildNavigation([
    m('finance', 'Financeiro', true, true),
    m('fiscal', 'Financeiro', false, false),
    m('hr', 'Pessoas', true, false),
    m('identity', 'Sistema', true, true),
  ])

  it('não liberado aparece com cadeado (CA 1)', () => {
    const fiscal = nav.areas.find((a) => a.name === 'Financeiro')!.modules.find((x) => x.code === 'fiscal')!
    expect(fiscal.locked).toBe(true)
  })

  it('liberado sem acesso não aparece, mas a rota ainda conhece o módulo (CA 3)', () => {
    expect(nav.areas.flatMap((a) => a.modules).some((x) => x.code === 'hr')).toBe(false)
    expect(nav.all.find((x) => x.code === 'hr')?.enabled).toBe(false)
  })

  it('área sem nenhum módulo visível some do dock (CA 4)', () => {
    expect(nav.areas.map((a) => a.name)).toEqual(['Financeiro', 'Sistema'])
  })
})
