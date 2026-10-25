import { getDownloadPurchaseOrderPdfUrl } from '@/api/generated/bemaqui'
import type { PurchaseOrderDetail } from '@/api/generated/model'
import { apiDownload } from '@/api/http'

/** "Baixar PDF" (CMP-03b CA 1): o PDF gerado na hora pelo servidor, com o nome do pedido. */
export const downloadOrderPdf = (order: Pick<PurchaseOrderDetail, 'id' | 'number'>) => apiDownload(getDownloadPurchaseOrderPdfUrl(order.id), `${order.number}.pdf`)
