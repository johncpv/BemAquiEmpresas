import { useQueryClient } from '@tanstack/react-query'
import { FileText } from 'lucide-react'
import { useState } from 'react'
import {
  attachMyTimeAdjustmentDocument, cancelMyTimeAdjustment, createMyTimeAdjustment, getDownloadMyTimeAdjustmentDocumentUrl, getListMyTimeAdjustmentsQueryKey,
  useListMyAbsenceReasons, useListMyTimeAdjustments,
} from '@/api/generated/bemaqui'
import type { TimeAdjustmentView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { AdjustmentDialog, type AdjustmentMode, type VoidTarget } from './AdjustmentDialogs'
import { AdjustmentStatus } from './AdjustmentPanel'
import { NOT_LINKED } from './time-clock'
import { shortDay } from './time-adjustments'

const button = 'min-h-11 rounded-md border border-border px-3 text-sm font-semibold'

/** Diálogo do pedido do funcionário (CA 5 e 7): incluir, desconsiderar (numa marcação dele) ou abonar; o funcionário vem da sessão. */
export function MyAdjustmentDialog({ mode, target, onClose }: { mode: AdjustmentMode | null; target?: VoidTarget; onClose: () => void }) {
  const client = useQueryClient()
  const idempotency = useIdempotencyKey()
  const reasons = useListMyAbsenceReasons({ query: { enabled: mode === 'absence' } })
  const titles: Record<AdjustmentMode, string> = { add: 'Incluir marcação esquecida', void: 'Desconsiderar esta marcação', absence: 'Abonar ausência' }
  return (
    <AdjustmentDialog mode={mode ?? 'add'} open={mode !== null} onOpenChange={(o) => { if (!o) onClose() }} verb="Pedir ajuste" done="Pedido enviado para aprovação"
      title={titles[mode ?? 'add']} description="O RH aprova antes de mudar o seu ponto. Você acompanha em Meus pedidos." reasons={reasons.data} canHealth target={target}
      submit={async (body, file) => {
        const saved = await createMyTimeAdjustment(body, idempotency.options)
        idempotency.renew()
        if (file) await attachMyTimeAdjustmentDocument(saved.id, { file })
        await client.invalidateQueries({ queryKey: getListMyTimeAdjustmentsQueryKey() })
      }} />
  )
}

function MyRequest({ adjustment: a }: { adjustment: TimeAdjustmentView }) {
  const client = useQueryClient()
  const [cancelling, setCancelling] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const refresh = () => client.invalidateQueries({ queryKey: getListMyTimeAdjustmentsQueryKey() })
  const upload = async (file: File | undefined) => {
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      await attachMyTimeAdjustmentDocument(a.id, { file })
      notifySuccess('Documento anexado')
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o documento.')
    } finally {
      setUploading(false)
    }
  }

  return (
    <li className="space-y-2 rounded-md border border-border bg-card p-3 text-sm">
      <p className="flex flex-wrap items-center justify-between gap-2"><span className="font-semibold"><span className="tabular-nums">{shortDay(a.day)}</span> · {a.summary}</span><AdjustmentStatus adjustment={a} /></p>
      <p className="text-muted-foreground">{a.justification}</p>
      {a.decisionComment && a.status === 'Rejected' ? <p className="text-crit-ink">Motivo da reprovação: {a.decisionComment}</p> : null}
      {a.documents.length > 0 ? (
        <ul aria-label="Documentos do pedido">
          {a.documents.map((d) => <li key={d.id}><a href={getDownloadMyTimeAdjustmentDocumentUrl(a.id, d.id)} className="inline-flex min-h-11 items-center gap-1 font-semibold text-primary underline"><FileText aria-hidden="true" className="size-4" />{d.fileName}</a></li>)}
        </ul>
      ) : null}
      {a.status === 'Pending' ? (
        <div className="flex flex-wrap items-center gap-2">
          {a.kind === 'Absence' ? (
            <label className={`${button} inline-flex cursor-pointer items-center`}>
              {uploading ? 'Anexando…' : 'Anexar documento'}
              <input type="file" accept="application/pdf,image/png,image/jpeg" className="sr-only" disabled={uploading} onChange={(e) => void upload(e.target.files?.[0])} />
            </label>
          ) : null}
          {a.canCancel ? <button type="button" className={button} onClick={() => setCancelling(true)}>Cancelar pedido</button> : null}
        </div>
      ) : null}
      {error ? <p role="alert" className="text-crit-ink">{error}</p> : null}
      <ConfirmDialog verb="Cancelar pedido" done="Pedido cancelado" destructive title="Cancelar este pedido?" open={cancelling} onOpenChange={setCancelling}
        description={`${a.summary} em ${shortDay(a.day)}. Nada muda no seu ponto.`}
        onConfirm={async () => { await cancelMyTimeAdjustment(a.id, { version: a.version }); await refresh() }} onReload={refresh} />
    </li>
  )
}

/**
 * "Pedir ajuste" e "Meus pedidos" (RH-01b CA 5, 7 e 8, layout de autoatendimento): incluir a marcação esquecida e abonar uma ausência
 * (com o documento opcional, que só o próprio funcionário e quem tem Ver dados de saúde veem); os pedidos com a situação, "Cancelar pedido"
 * enquanto pendente e "Anexar documento" no abono.
 */
export function MyTimeAdjustments({ initiallyOpen }: { initiallyOpen: boolean }) {
  const [mode, setMode] = useState<AdjustmentMode | null>(null)
  const [show, setShow] = useState(initiallyOpen)
  const query = useListMyTimeAdjustments({ query: { enabled: show, retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2 } })

  let list = null
  if (!show) list = null
  else if (query.isPending) list = <Skeleton className="h-16" />
  else if (query.isError) {
    const notLinked = query.error instanceof ApiError && query.error.code === 'employee_not_active'
    list = notLinked ? <EmptyState title="Pedidos indisponíveis" description={NOT_LINKED} /> : <ErrorState title="Não foi possível carregar os seus pedidos." onRetry={() => query.refetch()} />
  } else if (query.data.length === 0) list = <EmptyState title="Nenhum pedido de ajuste." description='Esqueceu uma marcação? Toque em "Incluir marcação esquecida".' />
  else list = <ul aria-label="Meus pedidos" className="space-y-2">{query.data.map((a) => <MyRequest key={a.id} adjustment={a} />)}</ul>

  return (
    <section id="meus-pedidos" aria-labelledby="pedir-ajuste" className="space-y-2">
      <h2 id="pedir-ajuste" className="text-lg font-bold">Pedir ajuste</h2>
      <div className="flex flex-wrap gap-2">
        <button type="button" className={button} onClick={() => { setShow(true); setMode('add') }}>Incluir marcação esquecida</button>
        <button type="button" className={button} onClick={() => { setShow(true); setMode('absence') }}>Abonar ausência</button>
        <button type="button" className={button} aria-expanded={show} onClick={() => setShow((v) => !v)}>{show ? 'Ocultar meus pedidos' : 'Meus pedidos'}</button>
      </div>
      {list}
      <MyAdjustmentDialog mode={mode} onClose={() => setMode(null)} />
    </section>
  )
}
