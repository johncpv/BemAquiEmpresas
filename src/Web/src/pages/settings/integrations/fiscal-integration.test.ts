import { describe, expect, it } from 'vitest'
import { formatCnpj, integrationStatus, isTestEnvironment, isTotpCode, pendingText, scopeLabel } from './fiscal-integration'

const base = { configured: true, active: false, environment: null, allowedEnvironment: 'Homologation', lastTestResult: null }

describe('integração com o Emissor Fiscal (FIS-01)', () => {
  it('situação: não configurada, aguardando o teste, recusada e ativa', () => {
    expect(integrationStatus({ ...base, configured: false })).toEqual({ label: 'Não configurada', tone: 'neutral' })
    expect(integrationStatus(base)).toEqual({ label: 'Aguardando o teste', tone: 'warn' })
    expect(integrationStatus({ ...base, lastTestResult: 'admin_scope' })).toEqual({ label: 'Recusada no teste', tone: 'crit' })
    expect(integrationStatus({ ...base, active: true, lastTestResult: 'ok' })).toEqual({ label: 'Ativa', tone: 'ok' })
  })

  it('faixa de homologação pelo emitente ou, antes do teste, pelo permitido no tenant', () => {
    expect(isTestEnvironment({ ...base, environment: 'Homologation' })).toBe(true)
    expect(isTestEnvironment({ ...base, environment: 'Production', allowedEnvironment: 'Production' })).toBe(false)
    expect(isTestEnvironment(base)).toBe(true)
    expect(isTestEnvironment({ ...base, allowedEnvironment: 'Production' })).toBe(false)
  })

  it('CNPJ formatado, escopos em português, pendentes e código do autenticador', () => {
    expect(formatCnpj('47225005000186')).toBe('47.225.005/0001-86')
    expect(formatCnpj(null)).toBe('')
    expect(scopeLabel('nfe:eventos')).toBe('Cancelar e corrigir NF-e')
    expect(scopeLabel('outro')).toBe('outro')
    expect(pendingText(0)).toBe('Nenhuma nota esperando a SEFAZ.')
    expect(pendingText(2)).toBe('2 notas esperando a SEFAZ.')
    expect(isTotpCode('123456')).toBe(true)
    expect(isTotpCode('12345a')).toBe(false)
  })
})
