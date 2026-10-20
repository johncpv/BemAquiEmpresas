import { useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { getDownloadAttachmentUrl, getListAttachmentsQueryKey, uploadAttachment, useListAttachments } from '@/api/generated/bemaqui'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'

const OWNER = 'Contract'

/** Contrato assinado (COM-04a CA 15): só PDF até 25 MB, com projects.edit; o mais recente vale, os anteriores ficam na lista. */
export function ContractDocuments({ contractId }: { contractId: string }) {
  const list = useListAttachments(OWNER, contractId)
  const client = useQueryClient()
  const canEdit = useCan('projects.edit')
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment(OWNER, contractId, { file })
      notifySuccess('Contrato assinado anexado')
      await client.invalidateQueries({ queryKey: getListAttachmentsQueryKey(OWNER, contractId) })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o contrato.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  const items = [...(list.data ?? [])].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
  const action = canEdit ? (
    <>
      <input ref={input} id="contrato-assinado" type="file" accept=".pdf,application/pdf" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor="contrato-assinado" className="inline-block cursor-pointer rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">
        {sending ? 'Enviando…' : items.length > 0 ? 'Trocar contrato assinado' : 'Anexar contrato assinado'}
      </label>
    </>
  ) : null

  let body
  if (list.isPending) body = <Skeleton className="h-24" />
  else if (list.isError) body = <ErrorState onRetry={() => list.refetch()} />
  else if (items.length === 0) body = <EmptyState title="O contrato assinado ainda não foi anexado." description="Anexe o PDF que o cliente assinou. É opcional e pode ser trocado depois." action={action} />
  else {
    body = (
      <>
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
          {items.map((a, i) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <span className="flex flex-wrap items-center gap-2">
                <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(a.id), a.fileName)} className="font-semibold text-primary hover:underline">{a.fileName}</button>
                {i === 0 ? <Tag>Contrato assinado</Tag> : <span className="text-xs text-muted-foreground">Versão anterior</span>}
              </span>
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
