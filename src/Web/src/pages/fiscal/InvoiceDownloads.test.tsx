import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { OutgoingInvoiceView } from '@/api/generated/model'
import { InvoiceDownloadButtons } from './InvoiceDownloads'

vi.mock('@/api/generated/bemaqui', () => ({
  getDownloadOutgoingInvoiceXmlUrl: (id: string) => `/api/v1/fiscal/outgoing-invoices/${id}/xml`,
  getDownloadOutgoingInvoiceDanfeUrl: (id: string) => `/api/v1/fiscal/outgoing-invoices/${id}/danfe`,
  getDownloadCancelledOutgoingInvoiceDanfeUrl: (id: string) => `/api/v1/fiscal/outgoing-invoices/${id}/cancelled-danfe`,
}))

const authorized = {
  id: 'n1', accessKey: '35261147225005000186550010000000011000000011', xmlAttachmentId: 'x1', danfeAttachmentId: 'd1',
  actions: { canDownload: true }, cancellation: { hasCancelledDanfe: false },
} as unknown as OutgoingInvoiceView

describe('baixar DANFE e XML no topo da nota (feedback do PO, 15/11)', () => {
  it('a autorizada mostra os dois botões; o rascunho, nenhum', () => {
    const { unmount } = render(<InvoiceDownloadButtons invoice={authorized} className="" onError={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'Baixar DANFE' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Baixar XML' })).toBeTruthy()
    unmount()

    const draft = { ...authorized, accessKey: null, xmlAttachmentId: null, danfeAttachmentId: null, actions: { canDownload: false } } as unknown as OutgoingInvoiceView
    render(<InvoiceDownloadButtons invoice={draft} className="" onError={vi.fn()} />)
    expect(screen.queryByRole('button', { name: /Baixar/ })).toBeNull()
  })
})
