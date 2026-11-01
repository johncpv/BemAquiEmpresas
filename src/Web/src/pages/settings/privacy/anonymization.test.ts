import { describe, expect, it } from 'vitest'
import { anonymizationOf, partialText, planBlocks, responseSuggestion, retainedText } from './anonymization'
import { nextStepOf } from './privacy'
import { RETENTION_SUBJECTS, ruleText } from './retention'

describe('anonimização do titular (LGPD-01b)', () => {
  it('a prévia em três blocos, com o módulo e o retido com data e motivo (CA 2 e 3)', () => {
    const blocks = planBlocks([
      { module: 'parties', title: 'Terceiros', items: [
        { action: 'Anonymize', label: 'Telefone, e-mail e observações do cadastro', source: 'parties.parties', count: 1 },
        { action: 'Retain', label: 'Nome e CPF do cadastro', source: 'parties.parties', count: 1, retainedUntil: '2030-12-31', reason: 'obrigação fiscal e contábil' },
        { action: 'Anonymize', label: 'Endereços', source: 'parties.party_addresses', count: 0 },
      ] },
      { module: 'finance', title: 'Financeiro', items: [{ action: 'Keep', label: 'Títulos a receber: mantidos', source: 'finance.receivables', count: 12 }] },
    ])
    expect(blocks.anonymize.map((l) => l.label)).toEqual(['Telefone, e-mail e observações do cadastro'])
    expect(blocks.retain[0].detail).toBe('Retido até 31/12/2030: obrigação fiscal e contábil')
    expect(blocks.keep[0]).toMatchObject({ module: 'Financeiro', count: 12 })
    expect(retainedText({ retainedUntil: null, reason: 'obrigação trabalhista e previdenciária' })).toContain('sem prazo')
  })

  it('situação, parcial e o próximo passo (CA 4 e 5)', () => {
    expect(anonymizationOf('Partial')?.tone).toBe('warn')
    expect(anonymizationOf('Done')?.label).toBe('Dados anonimizados')
    expect(anonymizationOf('None')).toBeNull()
    expect(partialText([
      { module: 'commercial', title: 'Comercial', status: 'Done' },
      { module: 'production', title: 'Produção', status: 'Failed' },
      { module: 'parties', title: 'Terceiros', status: 'Pending' },
    ])).toBe('Anonimização parcial: Comercial / Produção, Terceiros')
    expect(nextStepOf('Deletion', true)).toContain('Preveja a anonimização')
    expect(responseSuggestion('Deletion', null, 0)).toBe('Eliminação atendida por anonimização: documentos com movimento não podem ser apagados.')
    expect(responseSuggestion('Anonymization', null, 3)).toContain('retida pelo prazo legal')
  })

  it('a tabela de retenção por tipo de titular (CA 1)', () => {
    expect(RETENTION_SUBJECTS.map((s) => s.value)).toEqual(['Party', 'Contact', 'Employee', 'User'])
    expect(ruleText({ rule: 'Anytime', years: 0, baseDate: 'None' })).toBe('A qualquer momento')
    expect(ruleText({ rule: 'AfterYears', years: 5, baseDate: 'Termination' })).toBe('5 anos depois do desligamento')
    expect(ruleText({ rule: 'AfterYears', years: 0, baseDate: 'LastPayment' })).toBe('Depois do último pagamento')
    expect(ruleText({ rule: 'AfterYears', years: 1, baseDate: 'FiscalYearEnd' })).toBe('1 ano depois do fim do exercício')
  })
})
