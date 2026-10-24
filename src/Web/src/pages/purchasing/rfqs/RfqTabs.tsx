import { useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { getDownloadAttachmentUrl, getListAttachmentsQueryKey, uploadAttachment, useGetRfqHistory, useListAttachments } from '@/api/generated/bemaqui'
import type { RfqDetail, RfqSupplierView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { describeChanges } from '@/components/history/describe-changes'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'

export const OWNER = 'Rfq'

/** "Anexar proposta" (CA 4): dono Rfq com o id do fornecedor na cotação; pdf, png, jpg ou xlsx até 10 MB. */
export function AttachProposal({ supplier, onAttached, compact = false }: { supplier: Pick<RfqSupplierView, 'id' | 'partyName'>; onAttached?: () => void; compact?: boolean }) {
  const client = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment(OWNER, supplier.id, { file })
      notifySuccess('Proposta anexada')
      await client.invalidateQueries({ queryKey: getListAttachmentsQueryKey(OWNER, supplier.id) })
      onAttached?.()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar a proposta.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  const id = `proposta-${supplier.id}`
  return (
    <span className="inline-flex flex-col gap-1">
      <input ref={input} id={id} type="file" accept=".pdf,.png,.jpg,.jpeg,.xlsx" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor={id} className={compact ? 'inline-flex min-h-11 cursor-pointer items-center rounded-md border border-border px-3 text-sm font-semibold' : 'inline-flex min-h-11 cursor-pointer items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground'}>
        {sending ? 'Enviando…' : `Anexar proposta de ${supplier.partyName}`}
      </label>
      <FormError message={error} />
    </span>
  )
}

function SupplierDocuments({ supplier, canEdit }: { supplier: RfqSupplierView; canEdit: boolean }) {
  const list = useListAttachments(OWNER, supplier.id)
  let body
  if (list.isPending) body = <Skeleton className="h-16" />
  else if (list.isError) body = <ErrorState title="Não foi possível carregar os documentos." onRetry={() => list.refetch()} />
  else if (list.data.length === 0) body = <p className="text-sm text-muted-foreground">Nenhuma proposta anexada.</p>
  else {
    body = (
      <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
        {list.data.map((a) => (
          <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
            <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(a.id), a.fileName)} className="font-semibold text-primary hover:underline">{a.fileName}</button>
            <span className="flex items-center gap-2 text-xs text-muted-foreground">
              {a.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}
              {a.uploadedByName ? `${a.uploadedByName} · ` : ''}{formatDateTime(a.createdAt)}
            </span>
          </li>
        ))}
      </ul>
    )
  }

  return (
    <section aria-label={`Documentos de ${supplier.partyName}`} className="space-y-2 rounded-md border border-border p-3">
      <h3 className="flex flex-wrap items-center gap-2 text-sm font-semibold">
        {supplier.partyName}
        {supplier.attachmentRequired ? <StatusChip tone={supplier.attachmentsCount > 0 ? 'ok' : 'warn'}>{supplier.attachmentsCount > 0 ? 'Proposta anexada' : 'Proposta obrigatória'}</StatusChip> : null}
      </h3>
      {body}
      {canEdit ? <AttachProposal supplier={supplier} compact /> : null}
    </section>
  )
}

/** Aba Documentos (CA 4): as propostas de cada fornecedor da cotação. */
export function RfqDocuments({ rfq }: { rfq: RfqDetail }) {
  if (rfq.suppliers.length === 0) return <EmptyState title="Nenhum fornecedor na cotação." description="Inclua um fornecedor na aba Respostas para anexar a proposta dele." />
  return <div className="mt-3 grid gap-3 md:grid-cols-2">{rfq.suppliers.map((s) => <SupplierDocuments key={s.id} supplier={s} canEdit={rfq.canEdit} />)}</div>
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Removeu' }
const ENTITY: Record<string, string> = { Rfq: 'a cotação', RfqSupplier: 'um fornecedor', RfqLine: 'uma linha', RfqQuote: 'um preço', RfqLineRequest: 'o vínculo com a solicitação' }

/** Aba Histórico de alterações (CA 15): antes e depois de preços, respostas, vencedor e motivo, a geração e o envio dos pedidos. */
export function RfqHistory({ rfqId }: { rfqId: string }) {
  const [page, setPage] = useState(1)
  const query = useGetRfqHistory(rfqId, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="Cada mudança na cotação, nas respostas e nos vencedores aparece aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div className="mt-3">
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>{ENTITY[entry.entity] ?? entry.entity}</span>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'Sistema'} · </span>
              <time className="tabular-nums text-muted-foreground" dateTime={entry.at}>{formatDateTime(entry.at)}</time>
            </p>
            <ul className="mt-1 text-muted-foreground">{describeChanges(entry.changes).map((line) => <li key={line}>{line}</li>)}</ul>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="mt-3 flex items-center justify-between text-xs text-muted-foreground">
          <span className="tabular-nums">Página {data.page} de {pages}</span>
          <span className="flex gap-1">
            <button type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="rounded-md border border-border px-2 py-1 disabled:opacity-50">Anterior</button>
            <button type="button" disabled={page >= pages} onClick={() => setPage((p) => p + 1)} className="rounded-md border border-border px-2 py-1 disabled:opacity-50">Próxima</button>
          </span>
        </div>
      ) : null}
    </div>
  )
}
