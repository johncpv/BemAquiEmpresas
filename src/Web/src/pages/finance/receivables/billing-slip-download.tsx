import { Download } from 'lucide-react'
import { useState } from 'react'
import { getDownloadAttachmentUrl, listBillingSlips } from '@/api/generated/bemaqui'
import type { ReceivableDetail } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { notifyError } from '@/components/ui/messages'
import { hasSlipShortcut } from './billing-slips'

/** O mesmo arquivo da aba Boleto (`boleto-{número}.pdf`): lê o boleto em andamento só no clique, sem chamada por linha. */
async function downloadBillingSlip(r: Pick<ReceivableDetail, 'id' | 'number' | 'billingSlipId'>) {
  const slips = await listBillingSlips(r.id)
  const slip = slips.find((s) => s.id === r.billingSlipId) ?? slips.find((s) => s.pdfAttachmentId)
  if (!slip?.pdfAttachmentId) throw new Error('O PDF do boleto ainda não está disponível. Abra a aba Boleto do título.')
  await apiDownload(getDownloadAttachmentUrl(slip.pdfAttachmentId), `boleto-${r.number}.pdf`)
}

/** "Baixar boleto" (EXP-04 CA 19): nada com o boleto cancelado, expirado ou sem boleto. */
export function BillingSlipShortcut({ receivable, className }: { receivable: ReceivableDetail; className: string }) {
  const [busy, setBusy] = useState(false)
  if (!hasSlipShortcut(receivable.billingSlipStatus)) return null
  const run = async () => {
    setBusy(true)
    try {
      await downloadBillingSlip(receivable)
    } catch (e) {
      notifyError(e instanceof ApiError || e instanceof Error ? e.message : 'Não foi possível baixar o boleto.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <button type="button" onClick={() => void run()} disabled={busy} aria-busy={busy} className={className}>
      <Download aria-hidden="true" className="size-4" />{busy ? 'Baixando…' : 'Baixar boleto'}
    </button>
  )
}
