import { type DownloadableInvoice, downloadInvoiceFile, type InvoiceFile } from './invoice-downloads'

/**
 * "Baixar DANFE" e "Baixar XML" na barra de ações do topo da nota (feedback do PO, 15/11): com a nota autorizada, sem precisar achar a aba
 * Documentos. Na cancelada, o DANFE do topo é o com a marca CANCELADA. A aba Documentos continua com todos os arquivos e o reenvio.
 */
export function InvoiceDownloadButtons({ invoice: v, className, onError }: { invoice: DownloadableInvoice; className: string; onError: (message: string | null) => void }) {
  if (!v.actions.canDownload) return null
  const danfe: InvoiceFile | null = v.cancellation.hasCancelledDanfe ? 'cancelada' : v.danfeAttachmentId ? 'danfe' : null
  const download = async (kind: InvoiceFile) => {
    onError(null)
    onError(await downloadInvoiceFile(v, kind))
  }
  return (
    <>
      {danfe ? <button type="button" onClick={() => void download(danfe)} className={className}>{danfe === 'cancelada' ? 'Baixar DANFE cancelado' : 'Baixar DANFE'}</button> : null}
      {v.xmlAttachmentId ? <button type="button" onClick={() => void download('xml')} className={className}>Baixar XML</button> : null}
    </>
  )
}
