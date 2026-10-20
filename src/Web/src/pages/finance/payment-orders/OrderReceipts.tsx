import { useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { getDownloadAttachmentUrl, getListAttachmentsQueryKey, uploadAttachment, useListAttachments } from '@/api/generated/bemaqui'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'

const OWNER = 'PaymentOrder'

/** Comprovantes (CA 22): PDF, PNG ou JPG até 10 MB, só em OP paga, com finance.pay; leitura com finance.view. */
export function OrderReceipts({ orderId, paid }: { orderId: string; paid: boolean }) {
  const list = useListAttachments(OWNER, orderId)
  const client = useQueryClient()
  const canPay = useCan('finance.pay')
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment(OWNER, orderId, { file })
      notifySuccess('Comprovante anexado')
      await client.invalidateQueries({ queryKey: getListAttachmentsQueryKey(OWNER, orderId) })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o comprovante.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  const action = paid && canPay ? (
    <>
      <input ref={input} id="comprovante" type="file" accept=".pdf,.png,.jpg,.jpeg" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor="comprovante" className="inline-block cursor-pointer rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{sending ? 'Enviando…' : 'Anexar comprovante'}</label>
    </>
  ) : null

  let body
  if (list.isPending) body = <Skeleton className="h-24" />
  else if (list.isError) body = <ErrorState onRetry={() => list.refetch()} />
  else if (list.data.length === 0) body = <EmptyState title={paid ? 'Nenhum comprovante ainda.' : 'Anexe o comprovante depois do pagamento.'} action={action} />
  else {
    body = (
      <>
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
          {list.data.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(a.id), a.fileName)} className="font-semibold text-primary hover:underline">{a.fileName}</button>
              <span className="flex items-center gap-2 text-xs text-muted-foreground">{/* ScanStatus: 1 = Clean */}{a.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}{formatDateTime(a.createdAt)}</span>
            </li>
          ))}
        </ul>
        <div className="mt-3">{action}</div>
      </>
    )
  }

  return <div className="space-y-2"><FormError message={error} />{body}</div>
}
