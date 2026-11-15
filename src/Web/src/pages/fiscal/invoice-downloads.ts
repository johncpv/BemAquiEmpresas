import { getDownloadCancelledOutgoingInvoiceDanfeUrl, getDownloadOutgoingInvoiceDanfeUrl, getDownloadOutgoingInvoiceXmlUrl } from '@/api/generated/bemaqui'
import type { OutgoingInvoiceView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'

export type InvoiceFile = 'xml' | 'danfe' | 'cancelada'
export type DownloadableInvoice = Pick<OutgoingInvoiceView, 'id' | 'accessKey' | 'xmlAttachmentId' | 'danfeAttachmentId' | 'actions' | 'cancellation'>

/** Baixa o XML, o DANFE ou o DANFE cancelado da NF-e (topo, próximos passos e aba Documentos). Devolve a mensagem de erro, ou null quando baixou. */
export async function downloadInvoiceFile(v: DownloadableInvoice, kind: InvoiceFile): Promise<string | null> {
  const name = v.accessKey ?? v.id
  try {
    const url = kind === 'xml' ? getDownloadOutgoingInvoiceXmlUrl(v.id) : kind === 'danfe' ? getDownloadOutgoingInvoiceDanfeUrl(v.id) : getDownloadCancelledOutgoingInvoiceDanfeUrl(v.id)
    await apiDownload(url, `${name}-${kind === 'xml' ? 'nfe.xml' : kind === 'danfe' ? 'danfe.pdf' : 'danfe-cancelada.pdf'}`)
    return null
  } catch (e) {
    return e instanceof ApiError ? e.message : 'Não foi possível baixar.'
  }
}
