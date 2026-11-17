import { describe, expect, it } from 'vitest'
import { areaOf, buildNavigation, type PermissionModule } from './modules'
import { SAMPLE_MODULES } from './sample'

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

describe('Painel do supervisor no menu da Produção (DSH-03)', () => {
  const production = (actions: string[]) =>
    buildNavigation([{ code: 'production', name: 'Produção', area: 'Fábrica', released: true, enabled: true, actions }]).all[0].tabs.map((t) => t.slug)

  it('é a primeira aba para quem aprova na produção ou decide a qualidade', () => {
    expect(production(['view', 'approve'])[0]).toBe('supervisor')
    expect(production(['view', 'approve_quality'])[0]).toBe('supervisor')
  })

  it('fica no fim para quem só vê a produção', () => {
    const tabs = production(['view'])
    expect(tabs[0]).toBe('os')
    expect(tabs.at(-1)).toBe('supervisor')
  })
})

describe('perfil de negócio: liberado, cadeado e oculto (PERF-01, ADR 0046)', () => {
  const nav = buildNavigation([
    m('finance', 'Financeiro', true, true),
    m('fiscal', 'Financeiro', false, false),
    { ...m('production', 'Fábrica', false, false), hidden: true },
    { ...m('engineering', 'Fábrica', true, true), hidden: false },
  ])
  const visible = nav.areas.flatMap((a) => a.modules).map((x) => x.code)

  it('liberado aparece, no perfil e fora do plano fica com cadeado, fora do perfil some do menu e da busca', () => {
    expect(visible).toEqual(expect.arrayContaining(['finance', 'fiscal', 'engineering']))
    expect(visible).not.toContain('production')
  })

  it('oculto continua conhecido pela rota e cai na tela de não liberado (CA 9)', () => {
    const production = nav.all.find((x) => x.code === 'production')!
    expect(production.locked).toBe(true)
  })
})

describe('área Loja e rótulo de área por perfil (PERF-03)', () => {
  it('a Konebras (fábrica, areaLabels vazio) vê o mesmo dock de antes: sem Loja e com Fábrica (CA 8)', () => {
    const nav = buildNavigation(SAMPLE_MODULES)
    expect(nav.areas.map((a) => [a.id, a.name, a.short])).toEqual([
      ['overview', 'Visão geral', 'Visão geral'],
      ['commercial', 'Comercial', 'Comercial'],
      ['factory', 'Fábrica', 'Fábrica'],
      ['financial', 'Financeiro', 'Financeiro'],
      ['people', 'Pessoas', 'Pessoas'],
      ['dedicated', 'Telas dedicadas', 'Telas'],
      ['system', 'Sistema', 'Sistema'],
    ])
  })

  it('sem fábrica, a área de Estoque e Compras vira Retaguarda e continua achando os módulos (CA 2)', () => {
    const nav = buildNavigation(SAMPLE_MODULES, false, ['retail'], { Fábrica: 'Retaguarda' })
    const backoffice = nav.areas.find((a) => a.id === 'factory')!
    expect([backoffice.name, backoffice.short]).toEqual(['Retaguarda', 'Retaguarda'])
    expect(backoffice.modules.map((x) => x.code)).toEqual(expect.arrayContaining(['inventory', 'purchasing']))
    expect(areaOf(nav.areas, nav.all.find((x) => x.code === 'inventory')!)?.name).toBe('Retaguarda')
  })

  it('a área Loja aparece entre Comercial e Fábrica quando tem módulo (CA 1)', () => {
    const nav = buildNavigation([...SAMPLE_MODULES, { ...SAMPLE_MODULES[0], code: 'dashboard', area: 'Loja' }])
    expect(nav.areas.map((a) => a.id).slice(0, 4)).toEqual(['overview', 'commercial', 'store', 'factory'])
  })
})

describe('recurso por perfil dentro do Financeiro (PAG-01, decisão do PO de 17/11/2026)', () => {
  const tabs = (profiles: string[]) => buildNavigation([m('finance', 'Financeiro', true, true)], false, profiles).all[0].tabs.map((t) => t.slug)

  it('a fábrica (Konebras) não vê Formas de pagamento; Comércio vê', () => {
    expect(tabs(['factory'])).not.toContain('formas-de-pagamento')
    expect(tabs([])).not.toContain('formas-de-pagamento')
    expect(tabs(['factory', 'retail'])).toContain('formas-de-pagamento')
    expect(tabs(['factory'])).toContain('condicoes-de-pagamento')
  })
})
