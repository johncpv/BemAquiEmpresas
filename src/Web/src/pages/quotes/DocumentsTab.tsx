import { Link } from '@tanstack/react-router'
import { useRef, useState } from 'react'
import { generateQuotePdf, getDownloadAttachmentUrl, uploadAttachment, useGetQuoteDocuments } from '@/api/generated/bemaqui'
import type { AttachmentDto, QuoteDetail } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { SendQuoteDialog } from './SendQuoteDialog'

const EMAIL: Record<string, { label: string; tone: 'ok' | 'warn' | 'crit' | 'info' | 'neutral' }> = {
  Queued: { label: 'Na fila', tone: 'info' }, Sent: { label: 'Enviado', tone: 'ok' }, Failed: { label: 'Falhou', tone: 'crit' }, Skipped: { label: 'Não enviado', tone: 'neutral' },
}

function FileRow({ file }: { file: AttachmentDto }) {
  return (
    <li className="flex flex-wrap items-center justify-between gap-2 p-3">
      <span className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(file.id), file.fileName)} className="font-semibold text-primary hover:underline">{file.fileName}</button>
        {file.origin === 'System' ? <Tag>Gerado pelo sistema</Tag> : null}
        {file.scanStatus === 1 ? null : <StatusChip tone="info">Aguardando a verificação do arquivo</StatusChip>}
      </span>
      <span className="text-xs text-muted-foreground">{formatDateTime(file.createdAt)}</span>
    </li>
  )
}

/** Aba Documentos (COM-03 CA 3, 13 e 15): PDFs da revisão, envios com a situação e anexos do usuário. */
export function DocumentsTab({ quote, refresh }: { quote: QuoteDetail; refresh: () => Promise<void> }) {
  const query = useGetQuoteDocuments(quote.id)
  const canEdit = useCan('quotes.edit')
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [busy, setBusy] = useState(false)
  const run = async (action: () => Promise<unknown>, done: string) => {
    setBusy(true)
    setError(undefined)
    try { await action(); notifySuccess(done); await query.refetch() } catch (e) { setError(e instanceof ApiError ? e.message : 'Não foi possível concluir. Tente de novo.') } finally { setBusy(false) }
  }

  const upload = (file: File) => void run(() => uploadAttachment('Quote', quote.id, { file }), 'Arquivo anexado')
  if (query.isPending) return <Skeleton className="h-40" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  const d = query.data
  const sendable = canEdit && quote.isCurrent && (quote.status === 'Draft' || quote.status === 'Sent')
  return (
    <div className="space-y-4" onDragOver={(e) => e.preventDefault()} onDrop={(e) => { e.preventDefault(); const f = e.dataTransfer.files[0]; if (f && canEdit) upload(f) }}>
      <FormError message={error} />
      {!d.companyComplete ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Complete os dados da empresa para o PDF. <Link to={'/app/configuracoes/empresa' as never} className="font-semibold underline">Abrir dados da empresa</Link></p> : null}
      <div className="flex flex-wrap gap-2">
        <button type="button" disabled={busy} onClick={() => void run(() => generateQuotePdf(quote.id), 'PDF gerado')} className="rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-50">Gerar PDF</button>
        {sendable ? <SendQuoteDialog quote={quote} onSent={async () => { await query.refetch(); await refresh() }} /> : null}
      </div>
      <section aria-labelledby="pdfs" className="space-y-2">
        <h3 id="pdfs" className="text-sm font-semibold">PDFs desta revisão</h3>
        {d.generated.length === 0 ? <EmptyState title="Gere o PDF para enviar ao cliente." /> : <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">{d.generated.map((f) => <FileRow key={f.id} file={f} />)}</ul>}
      </section>
      <section aria-labelledby="envios" className="space-y-2">
        <h3 id="envios" className="text-sm font-semibold">Envios</h3>
        {d.dispatches.length === 0 ? <p className="text-sm text-muted-foreground">Ainda não foi enviado por e-mail.</p> : (
          <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
            {d.dispatches.map((s) => (
              <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span>Para <b>{s.to}</b>{s.cc ? ` (cópia: ${s.cc})` : ''} · {s.sentByName ?? '—'}</span>
                <span className="flex items-center gap-2"><StatusChip tone={EMAIL[s.status]?.tone ?? 'neutral'}>{EMAIL[s.status]?.label ?? s.status}</StatusChip>{s.error ? <span className="text-xs text-crit-ink">{s.error}</span> : null}<span className="text-xs text-muted-foreground">{formatDateTime(s.createdAt)}</span></span>
              </li>
            ))}
          </ul>
        )}
      </section>
      <section aria-labelledby="anexos" className="space-y-2">
        <h3 id="anexos" className="text-sm font-semibold">Anexos</h3>
        {d.uploaded.length > 0 ? <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">{d.uploaded.map((f) => <FileRow key={f.id} file={f} />)}</ul> : null}
        {canEdit ? (
          <>
            <input ref={input} id="anexo-orcamento" type="file" accept=".pdf,.png,.jpg,.jpeg,.docx,.xlsx,.dwg,.dxf" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) upload(f); if (input.current) input.current.value = '' }} />
            <label htmlFor="anexo-orcamento" className="inline-block cursor-pointer rounded-md border border-dashed border-border px-3 py-2 text-sm font-semibold">Escolher arquivo (ou arraste aqui)</label>
          </>
        ) : null}
      </section>
    </div>
  )
}
