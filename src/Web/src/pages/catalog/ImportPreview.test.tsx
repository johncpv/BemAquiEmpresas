import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ProductImportView } from '@/api/generated/model'
import { ImportPreview, ImportReport } from './ImportPreview'
import { progressPercent, validRows } from './catalog'

const view = (patch: Partial<ProductImportView> = {}): ProductImportView => ({
  id: 'r1', fileName: 'produtos.xlsx', status: 'Previewed', totalRows: 120, toCreate: 100, toUpdate: 17, errorCount: 3,
  errors: ['Linha 14: código de barras inválido.', 'Linha 20: informe o nome do produto.', 'Linha 31: NCM inválido. Use os 8 dígitos.'],
  pricesIgnored: false, stockRows: 0, canLaunchStock: true, locations: [], suggestedLocationId: null, newCategories: [], inBackground: false,
  processed: 0, toProcess: 117, created: 0, updated: 0, skipped: 0, stockPosted: 0, stockAlreadyPosted: 0, launchStock: false, locationName: null,
  createdAt: '2026-11-18T10:00:00Z', completedAt: null, failureMessage: null, ...patch,
})

describe('prévia da importação de produtos (CAT-02)', () => {
  it('mostra total, cria, atualiza, erros por linha e baixa as linhas com erro (CA 2 e 3)', () => {
    const onDownload = vi.fn()
    render(<ImportPreview view={view()} onDownloadErrors={onDownload} />)
    expect(screen.getByText('Linhas na planilha').nextSibling?.textContent).toBe('120')
    expect(screen.getByText('Criam produto').nextSibling?.textContent).toBe('100')
    expect(screen.getByText('Atualizam produto').nextSibling?.textContent).toBe('17')
    const errors = screen.getByRole('region', { name: 'Linhas com erro' })
    expect(errors.textContent).toContain('3 linhas com erro')
    expect(screen.getByText('Linha 14: código de barras inválido.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Baixar linhas com erro' }))
    expect(onDownload).toHaveBeenCalled()
  })

  it('avisa preços ignorados, categorias novas e o saldo sem a permissão de ajuste (CA 5 e 6)', () => {
    render(<ImportPreview view={view({ errorCount: 0, errors: [], pricesIgnored: true, newCategories: ['Bebidas'], stockRows: 4, canLaunchStock: false })} onDownloadErrors={() => {}} />)
    expect(screen.getByText(/os preços da planilha serão ignorados e a importação segue sem eles/)).toBeTruthy()
    expect(screen.getByText(/Categoria nova, criada na importação: Bebidas/)).toBeTruthy()
    expect(screen.getByText(/4 linhas têm saldo inicial, que não será lançado/)).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Linhas com erro' })).toBeNull()
  })

  it('relatório final com criados, atualizados, ignorados e o saldo já lançado antes (CA 6 e 8)', () => {
    render(<ImportReport view={view({ status: 'Completed', created: 10, updated: 2, skipped: 3, launchStock: true, stockPosted: 9, stockAlreadyPosted: 1, locationName: 'Loja' })} onDownloadErrors={() => {}} />)
    expect(screen.getByText('Criados').nextSibling?.textContent).toBe('10')
    expect(screen.getByText('Ignorados').nextSibling?.textContent).toBe('3')
    expect(screen.getByText(/Saldo inicial lançado em 9 produtos, no local Loja\. 1 produto já tinha o saldo/)).toBeTruthy()
  })

  it('conta as linhas válidas e o progresso do job', () => {
    expect(validRows(view())).toBe(117)
    expect(progressPercent(view({ processed: 50, toProcess: 200 }))).toBe(25)
    expect(progressPercent(view({ processed: 0, toProcess: 0 }))).toBe(100)
  })
})
