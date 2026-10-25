import { Link } from '@tanstack/react-router'
import { useRef, useState } from 'react'
import { getDownloadAttachmentUrl, uploadAttachment, useGetPurchaseOrderDocuments } from '@/api/generated/bemaqui'
import type { AttachmentDto, PurchaseOrderDetail } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { dispatchStatus, dispatchVerb, isResend } from './purchase-orders'

const OWNER = 'PurchaseOrder'

function FileRow({ file }: { file: AttachmentDto }) {
  return (
    <li className="flex flex-wrap items-center justify-between gap-2 p-3">
      <span className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(file.id), file.fileName)} className="font-semibold text-primary hover:underline">{file.fileName}</button>
        {file.origin === 'System' ? <Tag>Gerado pelo sistema</Tag> : null}
        {file.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}
      </span>
      <span className="text-xs text-muted-foreground tabular-nums">{formatDateTime(file.createdAt)}</span>
    </li>
  )
}

/**
 * Aba Documentos (CMP-03a CA 21 e CMP-03b CA 3): os envios ao fornecedor em cartões (destinatário, quem, quando, a situação do e-mail e o
 * PDF enviado), os PDFs gerados nos envios e os anexos do usuário (pdf, png e jpg até 10 MB, com purchasing.edit). Falhou: "Reenviar".
 */
export function OrderDocuments({ order, onDispatch }: { order: PurchaseOrderDetail; onDispatch: () => void }) {
  const query = useGetPurchaseOrderDocuments(order.id)
  const canEdit = useCan('purchasing.edit')
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [sending, setSending] = useState(false)
  const words = dispatchVerb(isResend(order))
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment(OWNER, order.id, { file })
      notifySuccess('Documento anexado')
      await query.refetch()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o documento.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  if (query.isPending) return <div className="mt-3 space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-16" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar os documentos." message="Verifique a conexão e tente de novo." onRetry={() => query.refetch()} />
  const d = query.data
  const dispatchButton = order.canDispatch
    ? <button type="button" onClick={onDispatch} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{words.verb}</button>
    : null

  return (
    <div className="mt-3 space-y-5">
      <FormError message={error} />
      {!d.companyComplete ? (
        <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          Complete os dados da empresa para o PDF do pedido. <Link to={'/app/configuracoes/empresa' as never} className="font-semibold underline">Abrir dados da empresa</Link>
        </p>
      ) : null}
      <section aria-labelledby="envios-pedido" className="space-y-2">
        <h3 id="envios-pedido" className="text-sm font-semibold">Envios ao fornecedor</h3>
        {d.dispatches.length === 0 ? (
          <EmptyState title="Nenhum envio." description="Envie o pedido ao fornecedor depois de aprovado." action={dispatchButton ?? undefined} />
        ) : (
          <ul className="space-y-2" aria-label="Envios ao fornecedor">
            {d.dispatches.map((s) => {
              const state = dispatchStatus(s)
              return (
                <li key={s.id} className="space-y-1 rounded-md border border-border bg-card p-3 text-sm">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="break-all">Para <b>{s.to}</b>{s.cc ? ` · cópia: ${s.cc}` : ''}</span>
                    <StatusChip tone={state.tone}>{state.label}</StatusChip>
                  </div>
                  <p className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                    <span className="tabular-nums">{formatDateTime(s.createdAt)}</span>
                    <span>por {s.sentByName ?? '—'}</span>
                    {s.isResend ? <Tag>Reenvio</Tag> : null}
                    {s.recipientConfirmed ? <Tag>Destinatário confirmado</Tag> : null}
                    {s.attachmentName ? (
                      <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(s.attachmentId), s.attachmentName ?? 'pedido.pdf')} className="font-semibold text-primary hover:underline">{s.attachmentName}</button>
                    ) : null}
                  </p>
                  {s.status === 'Failed' && order.canDispatch ? <button type="button" onClick={onDispatch} className="text-sm font-semibold text-primary underline">Reenviar</button> : null}
                </li>
              )
            })}
          </ul>
        )}
      </section>
      {d.generated.length > 0 ? (
        <section aria-labelledby="pdfs-pedido" className="space-y-2">
          <h3 id="pdfs-pedido" className="text-sm font-semibold">PDFs enviados</h3>
          <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">{d.generated.map((f) => <FileRow key={f.id} file={f} />)}</ul>
        </section>
      ) : null}
      <section aria-labelledby="anexos-pedido" className="space-y-2">
        <h3 id="anexos-pedido" className="text-sm font-semibold">Anexos</h3>
        {d.uploaded.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum documento anexado. Anexe a proposta do fornecedor ou outro documento do pedido (PDF, PNG ou JPG até 10 MB).</p>
          : <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">{d.uploaded.map((f) => <FileRow key={f.id} file={f} />)}</ul>}
        {canEdit ? (
          <>
            <input ref={input} id="documento-pedido" type="file" accept=".pdf,.png,.jpg,.jpeg" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
            <label htmlFor="documento-pedido" className="inline-block cursor-pointer rounded-md border border-border px-3 py-2 text-sm font-semibold">{sending ? 'Enviando…' : 'Anexar documento'}</label>
          </>
        ) : null}
      </section>
    </div>
  )
}
