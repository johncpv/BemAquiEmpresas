import { describe, expect, it } from 'vitest'
import { dreExportUrl, reconciliationExportUrl, statementExportUrl } from './finance-exports'

/** EXP-02: o menu "Exportar" repassa os filtros da tela; o PDF das telas com documento próprio vai para o documento do período. */
describe('Exportação do financeiro (EXP-02)', () => {
  it('extrato: planilha com os filtros e PDF do extrato do período', () => {
    const url = statementExportUrl('c1', { from: '2026-10-01', to: '2026-10-31', withoutOrder: true })
    expect(url('xlsx')).toContain('/finance/bank-accounts/c1/statement/export?')
    expect(url('xlsx')).toContain('format=xlsx')
    expect(url('xlsx')).toContain('withoutOrder=true')
    expect(url('pdf')).toContain('/finance/bank-accounts/c1/statement/pdf?')
    expect(url('pdf')).toContain('from=2026-10-01')
    expect(url('pdf')).not.toContain('format=')
  })

  it('conciliação: relatório em PDF com a conta e o período', () => {
    const url = reconciliationExportUrl('c2', { from: '2026-10-01', to: '2026-10-31' })
    expect(url('xlsx')).toContain('/finance/bank-accounts/c2/reconciliation/export?')
    expect(url('pdf')).toContain('/finance/bank-accounts/c2/reconciliation/pdf?')
    expect(url('pdf')).toContain('to=2026-10-31')
  })

  it('DRE: o período da tela nos dois formatos', () => {
    const url = dreExportUrl({ from: '2025-12-01', to: '2026-11-01' })
    expect(url('xlsx')).toContain('/finance/dre/export?')
    expect(url('xlsx')).toContain('from=2025-12-01')
    expect(url('pdf')).toContain('/finance/dre/pdf?')
  })
})
