import { describe, expect, it } from 'vitest'
import { buildNavigation, type PermissionModule } from '@/navigation/modules'
import { defaultStatus, EVENT_STATUS, STATUS_OPTIONS } from './events'

const module = (code: string, actions: string[], released = true): PermissionModule => ({ code, name: code, area: 'Sistema', released, enabled: actions.length > 0, actions })

describe('eventos recebidos (FND-16)', () => {
  it('cada situação tem rótulo e tom; parado é crítico', () => {
    expect(EVENT_STATUS.DeadLettered).toEqual({ label: 'Parado', tone: 'crit' })
    expect(EVENT_STATUS.Failed.tone).toBe('warn')
    expect(STATUS_OPTIONS.find((o) => o.value === 'DeadLettered')?.label).toBe('Parados')
  })

  it('abre em "Parados" só na primeira carga, com parado e sem situação escolhida', () => {
    expect(defaultStatus(undefined, 2, false)).toBe('DeadLettered')
    expect(defaultStatus(undefined, 0, false)).toBeUndefined()
    expect(defaultStatus('Processed', 2, false)).toBeUndefined()
    expect(defaultStatus(undefined, 2, true)).toBeUndefined()
  })

  it('a aba Integrações some sem ver o módulo dono (CA 16)', () => {
    const tabs = (modules: PermissionModule[]) => buildNavigation(modules).all.find((m) => m.code === 'settings')?.tabs.map((t) => t.slug) ?? []
    expect(tabs([module('settings', ['view']), module('finance', ['view'])])).toContain('integracoes')
    expect(tabs([module('settings', ['view']), module('fiscal', ['view'])])).toContain('integracoes')
    expect(tabs([module('settings', ['view']), module('finance', [])])).not.toContain('integracoes')
    expect(tabs([module('settings', ['view']), module('finance', ['view'], false)])).not.toContain('integracoes')
    expect(tabs([module('settings', ['view'])])).toContain('empresa')
  })
})
