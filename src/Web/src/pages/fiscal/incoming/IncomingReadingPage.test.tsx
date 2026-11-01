import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { IncomingReadingView } from '@/api/generated/model'
import { renderWithRouter } from '@/test-utils'
import { IncomingReadingPage } from './IncomingReadingPage'
import { requiredKeys } from './reading'

const state = vi.hoisted(() => ({ reading: undefined as unknown }))

vi.mock('@/api/generated/bemaqui', () => ({
  useGetIncomingInvoiceReading: () => ({ isPending: false, isError: false, data: state.reading, refetch: vi.fn() }),
  getGetIncomingInvoiceReadingQueryKey: (id: string) => ['reading', id],
  getListIncomingInvoicesQueryKey: () => ['incoming'],
  getDownloadIncomingInvoiceReadingPdfUrl: (id: string) => `/api/v1/fiscal/incoming-invoices/${id}/pdf-reading/pdf`,
  checkIncomingInvoiceReading: vi.fn(),
  requestIncomingInvoiceXml: vi.fn(),
  switchIncomingInvoiceReadingToManual: vi.fn(),
}))

// O título da página lê a navegação do shell; aqui basta o título.
vi.mock('@/shell/PageHeading', () => ({ PageHeading: ({ title, children }: { title: string; children?: unknown }) => <header><h1>{title}</h1>{children as never}</header> }))

const base: IncomingReadingView = {
  id: 'r1', source: 'Pdf', status: 'Ready', result: 'Read', message: null, hasPdf: false, fileName: 'danfe.pdf', pages: 1, durationMs: 120, fields: [], warnings: [], key: null,
  supplier: null, tenantDocument: '47225005000186', duplicateInvoiceId: null, duplicateMessage: null, xmlByKeyAvailable: false, xmlByKeyMessage: 'Não conseguimos buscar o XML desta nota automaticamente.',
  xmlRequestedAt: null, invoiceId: null, createdAt: new Date().toISOString(), can: { import: true, createParty: false },
}

/** Leitura com todo campo lido, menos o total da nota (incerto) e o NCM do item (não encontrado). */
function readingWithFields(): IncomingReadingView {
  const values: Record<string, string> = {
    accessKey: '35261047225005000186550010000012341123456780', issuedAt: '2026-10-15', issuerName: 'FORNECEDOR TESTE LTDA', recipientDocument: '47225005000186',
    'totals.products': '125', 'totals.total': '125', 'items.0.code': 'CH-2MM', 'items.0.description': 'CHAPA', 'items.0.unit': 'UN', 'items.0.quantity': '10', 'items.0.unitPrice': '12.5', 'items.0.amount': '125',
  }
  const fields = requiredKeys(1).map((key) => ({
    key, value: values[key] ?? (key.startsWith('totals.') ? '0' : null),
    state: key === 'totals.total' ? 'Uncertain' as const : key === 'items.0.ncm' ? 'NotFound' as const : 'Read' as const,
  }))
  return { ...base, fields }
}

function renderPage(reading: IncomingReadingView) {
  state.reading = reading
  const client = new QueryClient()
  return renderWithRouter(() => <QueryClientProvider client={client}><IncomingReadingPage /></QueryClientProvider>)
}

describe('leitura assistida (FIS-05 CA 7)', () => {
  it('foco no primeiro campo incerto, situação em texto e Conferir nota só com tudo conferido', async () => {
    renderPage(readingWithFields())
    await waitFor(() => expect(document.activeElement?.id).toBe('campo-totals-total'))
    expect(screen.getAllByText('Leitura incerta: confira').length).toBeGreaterThan(0)
    expect(screen.getByText('Não encontrado')).toBeTruthy()
    expect(screen.getAllByText('22 campos a conferir').length).toBeGreaterThan(0)
    expect((screen.getByRole('button', { name: 'Conferir nota' }) as HTMLButtonElement).disabled).toBe(true)

    // Corrigir o valor já marca como conferido; os outros, pela caixa "Conferido".
    fireEvent.change(screen.getByLabelText('NCM'), { target: { value: '72085100' } })
    expect(screen.getAllByText('21 campos a conferir').length).toBeGreaterThan(0)
    for (const box of screen.getAllByRole('checkbox', { name: 'Conferido' })) {
      if (!(box as HTMLInputElement).checked) fireEvent.click(box)
    }
    expect(screen.getByText('Todos os campos conferidos')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Conferir nota' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('a soma dos itens diferente do total dos produtos bloqueia', async () => {
    const reading = readingWithFields()
    renderPage({ ...reading, fields: reading.fields.map((f) => (f.key === 'totals.products' ? { ...f, value: '100' } : f)) })
    expect(await screen.findByText(/A soma dos itens \(R\$\s125,00\) não bate com o total da nota \(R\$\s100,00\)\./)).toBeTruthy()
  })

  it('lendo, sem texto e digitar a nota', async () => {
    const { unmount } = renderPage({ ...base, status: 'Reading', result: null })
    expect(await screen.findByText('Lendo o PDF…')).toBeTruthy()
    unmount()
    renderPage({ ...base, status: 'Failed', result: 'NoText', message: 'Este PDF não tem texto para ler. Peça o XML ao fornecedor ou digite a nota.' })
    expect(await screen.findByText('PDF sem texto para ler')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Pedir o XML ao fornecedor' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Digitar a nota' })).toBeTruthy()
  })
})
