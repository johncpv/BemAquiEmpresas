import { describe, expect, it } from 'vitest'
import { certificateStatus, confirmedText, crtOf, formatCnae, parseCnaes, SECTION_FIELDS, turnsOnProduction } from './fiscal-settings'
import type { FiscalSettingsValues } from '@/api/generated/model'

describe('Configurações → Fiscal (FIS-02a)', () => {
  it('CRT sai do regime', () => {
    expect(crtOf('LucroPresumido')).toBe(3)
    expect(crtOf('LucroReal')).toBe(3)
    expect(crtOf('SimplesNacional')).toBe(1)
    expect(crtOf('SimplesNacionalExcess')).toBe(2)
    expect(crtOf('Mei')).toBe(4)
  })

  it('CNAE formatado e lista digitada', () => {
    expect(formatCnae('4329103')).toBe('4329-1/03')
    expect(parseCnaes('2822-4/01, 4329-1/03;  ')).toEqual(['2822401', '4329103'])
  })

  it('contador e situação do certificado', () => {
    expect(confirmedText(3, 40)).toBe('3 de 40 campos conferidos')
    expect(certificateStatus('Expiring', 12)).toEqual({ label: 'Vence em 12 dias', tone: 'warn' })
    expect(certificateStatus('Expiring', 1).label).toBe('Vence em 1 dia')
    expect(certificateStatus('Expired', -2).tone).toBe('crit')
    expect(certificateStatus('Valid', 200).tone).toBe('ok')
    expect(certificateStatus('None', null).label).toBe('Não enviado')
  })

  it('só a troca para produção abre o diálogo', () => {
    const base = { environment: 'Homologation' } as FiscalSettingsValues
    expect(turnsOnProduction(base, { ...base, environment: 'Production' })).toBe(true)
    expect(turnsOnProduction({ ...base, environment: 'Production' }, { ...base, environment: 'Production' })).toBe(false)
    expect(turnsOnProduction({ ...base, environment: 'Production' }, base)).toBe(false)
  })

  it('cada campo da tela tem uma chave única', () => {
    const keys = Object.values(SECTION_FIELDS).flat().map((f) => f.key)
    expect(new Set(keys).size).toBe(keys.length)
    expect(keys).toHaveLength(41)
  })
})
