import { getDownloadAssetLabelBatchUrl, getPrintAssetLabelUrl } from '@/api/generated/bemaqui'
import { apiDownload, ApiError } from '@/api/http'
import { notifyError, notifySuccess } from '@/components/ui/messages'

/** "Imprimir etiqueta" fora dos botões (próximo passo da criação): baixa o PDF ou mostra o motivo. */
export async function printLabel(id: string, tag: string) {
  try {
    await apiDownload(getPrintAssetLabelUrl(id), `etiqueta-${tag}.pdf`)
    notifySuccess('Etiqueta pronta para imprimir')
  } catch (e) {
    notifyError(e instanceof ApiError ? e.message : 'Não foi possível gerar a etiqueta. Tente de novo.')
  }
}

/** Baixa o PDF do lote (link do sino ou "Imprimir etiquetas" até 50). */
export async function downloadLabelBatch(attachmentId: string) {
  await apiDownload(getDownloadAssetLabelBatchUrl(attachmentId), 'etiquetas.pdf')
}
