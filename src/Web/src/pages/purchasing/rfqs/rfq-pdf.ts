import { getDownloadRfqRequestPdfUrl } from '@/api/generated/bemaqui'
import type { RfqDetail, RfqSupplierView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { rfqRequestFileName } from './rfqs'

/** Baixa o pedido de cotação de um fornecedor (EXP-03 CA 1); devolve a mensagem de erro do servidor (409 da cotação fechada, 404) ou null. */
export async function downloadRfqRequest(rfq: Pick<RfqDetail, 'id' | 'number'>, supplier: Pick<RfqSupplierView, 'id' | 'partyName'>): Promise<string | null> {
  try {
    await apiDownload(getDownloadRfqRequestPdfUrl(rfq.id, supplier.id), rfqRequestFileName(rfq.number, supplier.partyName))
    return null
  } catch (e) {
    return e instanceof ApiError ? e.message : 'Não foi possível gerar o PDF. Verifique a conexão e tente de novo.'
  }
}

/** O mapa comparativo sai da cotação aberta com ao menos uma resposta, da concluída e da cancelada (EXP-03 CA 2). */
export const canDownloadComparison = (rfq: Pick<RfqDetail, 'status' | 'suppliers'>) => rfq.status !== 'Open' || rfq.suppliers.some((s) => s.responded)
