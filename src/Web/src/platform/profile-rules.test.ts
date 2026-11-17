import { describe, expect, it } from 'vitest'
import { profileChoices, profilesWithSegments, toggleProfile, uncoveredModules, type ProfileOption } from './profile-rules'

const catalog: ProfileOption[] = [
  { code: 'factory', name: 'Fábrica', modules: [{ code: 'finance', name: 'Financeiro' }, { code: 'production', name: 'Produção' }], segments: [] },
  { code: 'retail', name: 'Comércio', modules: [{ code: 'finance', name: 'Financeiro' }, { code: 'kiosk', name: 'Painel da TV' }], segments: [{ code: 'counter', name: 'Balcão' }, { code: 'bakery', name: 'Padaria' }] },
  { code: 'services', name: 'Prestação de serviços', modules: [{ code: 'quotes', name: 'Orçamentos' }], segments: [{ code: 'workshop', name: 'Oficina' }] },
  { code: 'ecommerce', name: 'E-commerce', modules: [{ code: 'finance', name: 'Financeiro' }], segments: [] },
]

describe('passos de perfil e segmento do provisionamento (PERF-02)', () => {
  it('Comércio entra com Balcão escolhido e o passo Segmento mostra só os perfis marcados que têm segmento', () => {
    const [selected, segments] = toggleProfile(['factory'], { factory: null }, 'retail')
    expect(selected).toEqual(['factory', 'retail'])
    expect(segments.retail).toBe('counter')
    expect(profilesWithSegments(catalog, selected).map((p) => p.code)).toEqual(['retail'])
    expect(profilesWithSegments(catalog, ['factory', 'ecommerce'])).toEqual([])

    const [without, rest] = toggleProfile(selected, segments, 'retail')
    expect(without).toEqual(['factory'])
    expect(rest).not.toHaveProperty('retail')
    expect(profileChoices(['retail', 'services'], { retail: 'bakery', services: null })).toEqual([{ code: 'retail', segment: 'bakery' }, { code: 'services', segment: null }])
  })

  it('avisa os módulos dos perfis marcados que o plano não cobre, sem repetir', () => {
    expect(uncoveredModules(catalog, ['factory', 'retail'], ['finance'])).toEqual(['Produção', 'Painel da TV'])
    expect(uncoveredModules(catalog, ['ecommerce'], ['finance'])).toEqual([])
    expect(uncoveredModules(catalog, ['factory'], undefined)).toEqual([])
  })
})
