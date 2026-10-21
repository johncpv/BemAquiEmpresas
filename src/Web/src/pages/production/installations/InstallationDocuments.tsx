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

const OWNER = 'ProductionOrder'

/** Fotos e termo assinado (COM-06 CA 8): png, jpg ou pdf até 10 MB; no celular, a câmera pelo "Escolher arquivo". */
export function InstallationDocuments({ orderId }: { orderId: string }) {
  const list = useListAttachments(OWNER, orderId)
  const client = useQueryClient()
  const canEdit = useCan('production.edit')
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment(OWNER, orderId, { file })
      notifySuccess('Arquivo anexado')
      await client.invalidateQueries({ queryKey: getListAttachmentsQueryKey(OWNER, orderId) })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o arquivo.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  const action = canEdit ? (
    <>
      <input ref={input} id="foto-os" type="file" accept=".pdf,.png,.jpg,.jpeg,image/*" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor="foto-os" className="inline-flex min-h-[56px] cursor-pointer items-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground">{sending ? 'Enviando…' : 'Escolher arquivo'}</label>
    </>
  ) : null
  let body
  if (list.isPending) body = <Skeleton className="h-24" />
  else if (list.isError) body = <ErrorState onRetry={() => list.refetch()} />
  else if (list.data.length === 0) body = <EmptyState title="Nenhuma foto ou termo anexado." description="Anexe as fotos da instalação e o termo de entrega assinado." action={action} />
  else {
    body = (
      <>
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
          {list.data.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(a.id), a.fileName)} className="font-semibold text-primary hover:underline">{a.fileName}</button>
              <span className="flex items-center gap-2 text-xs text-muted-foreground">{/* ScanStatus: 1 = Clean */}{a.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}{formatDateTime(a.createdAt)}{a.uploadedByName ? ` · ${a.uploadedByName}` : ''}</span>
            </li>
          ))}
        </ul>
        <div className="mt-3">{action}</div>
      </>
    )
  }

  return <div className="space-y-2"><FormError message={error} />{body}</div>
}
