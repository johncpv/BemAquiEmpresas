import { useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { getDownloadAttachmentUrl, getListAttachmentsQueryKey, uploadAttachment, useListAttachments } from '@/api/generated/bemaqui'
import type { AttachmentDto } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'

function Files({ items }: { items: AttachmentDto[] }) {
  return (
    <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
      {items.map((a) => (
        <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
          <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(a.id), a.fileName)} className="font-semibold text-primary hover:underline">{a.fileName}</button>
          <span className="flex items-center gap-2 text-xs text-muted-foreground">{/* ScanStatus: 1 = Clean */}{a.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}{formatDateTime(a.createdAt)}{a.uploadedByName ? ` · ${a.uploadedByName}` : ''}</span>
        </li>
      ))}
    </ul>
  )
}

/** Documentos (COM-05 CA 11): anexos do projeto (upload com projects.edit, por botão ou arrastando) e o contrato assinado. */
export function ProjectDocumentsTab({ projectId, contractId }: { projectId: string; contractId: string }) {
  const own = useListAttachments('Project', projectId)
  const signed = useListAttachments('Contract', contractId)
  const client = useQueryClient()
  const canEdit = useCan('projects.edit')
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment('Project', projectId, { file })
      notifySuccess('Documento anexado')
      await client.invalidateQueries({ queryKey: getListAttachmentsQueryKey('Project', projectId) })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o documento.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  const action = canEdit ? (
    <div onDragOver={(e) => e.preventDefault()} onDrop={(e) => { e.preventDefault(); const f = e.dataTransfer.files[0]; if (f) void upload(f) }} className="rounded-md border border-dashed border-border p-3 text-sm">
      <input ref={input} id="documento-projeto" type="file" accept=".pdf,.png,.jpg,.jpeg" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor="documento-projeto" className="inline-block cursor-pointer rounded-md bg-primary px-3 py-2 font-semibold text-primary-foreground">{sending ? 'Enviando…' : 'Escolher arquivo'}</label>
      <span className="ml-2 text-muted-foreground">ou arraste aqui (PDF, PNG ou JPG até 25 MB)</span>
    </div>
  ) : null

  return (
    <div className="space-y-4">
      <FormError message={error} />
      <section className="space-y-2">
        <h3 className="text-sm font-semibold">Documentos do projeto</h3>
        {own.isPending ? <Skeleton className="h-20" /> : own.isError ? <ErrorState onRetry={() => own.refetch()} /> : own.data.length === 0 ? <EmptyState title="Nenhum documento do projeto ainda." action={action} /> : <><Files items={own.data} />{action}</>}
      </section>
      <section className="space-y-2">
        <h3 className="text-sm font-semibold">Contrato assinado</h3>
        {signed.isPending ? <Skeleton className="h-12" /> : signed.isError ? <ErrorState onRetry={() => signed.refetch()} /> : signed.data.length === 0 ? <p className="text-sm text-muted-foreground">Ainda não anexado. Anexe na tela do contrato.</p> : <Files items={signed.data} />}
      </section>
    </div>
  )
}
