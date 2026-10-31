import { useQueryClient } from '@tanstack/react-query'
import { FileText, Lock } from 'lucide-react'
import { useState } from 'react'
import { approveTimeAdjustment, getDownloadTimeAdjustmentDocumentUrl, rejectTimeAdjustment, useGetTimeAdjustment } from '@/api/generated/bemaqui'
import type { TimeAdjustmentDayEntryView, TimeAdjustmentView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { ADJUSTMENT_DICAS, SELF_APPROVED, shortDay, STATUS_CHIP, workedText } from './time-adjustments'
import { entryLabel, formatTime } from './time-clock'

/** Chip da situação, com a marca "autoaprovado" (CA 13). */
export function AdjustmentStatus({ adjustment }: { adjustment: TimeAdjustmentView }) {
  const chip = STATUS_CHIP[adjustment.status]
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      <StatusChip tone={chip.tone}>{chip.label}</StatusChip>
      {adjustment.selfApproved ? <span className="inline-flex items-center"><StatusChip tone="warn">{SELF_APPROVED}</StatusChip><Dica nome="Autoaprovado" texto={ADJUSTMENT_DICAS.autoaprovado} /></span> : null}
    </span>
  )
}

/** Bloco "Dados protegidos" (padrão 8, CA 9): sem hr.view_health, o abono mostra só "Ausência justificada" e "Documento anexado". */
export function ProtectedAbsence({ adjustment }: { adjustment: TimeAdjustmentView }) {
  return (
    <div role="group" aria-label="Dados protegidos" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">
      <p className="flex items-center gap-1 font-semibold"><Lock aria-hidden="true" className="size-3.5" />Dados protegidos<Dica nome="Motivo de saúde" texto={ADJUSTMENT_DICAS.saude} /></p>
      <p>Ausência justificada{adjustment.hasDocument ? ' · Documento anexado' : ''}</p>
    </div>
  )
}

function DayList({ label, entries, worked, struck }: { label: string; entries: readonly TimeAdjustmentDayEntryView[]; worked: number; struck: boolean }) {
  return (
    <section aria-label={label} className="rounded-md border border-border bg-card">
      <p className="flex justify-between border-b border-border px-3 py-2 text-sm font-semibold"><span>{label}</span><span className="tabular-nums">{workedText(worked)}</span></p>
      {entries.length === 0 ? <p className="px-3 py-2 text-sm text-muted-foreground">Sem marcações.</p> : (
        <ul className="divide-y divide-border text-sm">
          {entries.map((e, i) => (
            <li key={`${e.id ?? 'novo'}-${i}`} className={e.changed ? (struck ? 'px-3 py-1.5 text-muted-foreground line-through' : 'px-3 py-1.5 font-semibold text-ok-ink') : 'px-3 py-1.5'}>
              {entryLabel(e.type)} <span className="tabular-nums">{formatTime(e.at)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/**
 * Detalhe do pedido em painel lateral (CA 10 a 14, tela cheia a 400 px): o que muda, a justificativa (conforme o CA 9), quem pediu e quando,
 * o dia antes e depois com as horas trabalhadas, e "Aprovar ajuste" ou "Reprovar" para quem tem hr.approve e não é o solicitante nem o dono
 * das marcações (salvo o Proprietário único). 409 de versão oferece "Recarregar".
 */
export function AdjustmentPanel({ id, onClose, onChanged }: { id: string | null; onClose: () => void; onChanged?: () => void }) {
  const query = useGetTimeAdjustment(id ?? '', { query: { enabled: !!id } })
  const client = useQueryClient()
  const canApprove = useCan('hr.approve')
  const [approving, setApproving] = useState(false)
  const [rejecting, setRejecting] = useState(false)
  const [comment, setComment] = useState('')
  const refresh = async () => {
    await client.invalidateQueries()
    onChanged?.()
  }

  let body
  if (!id) body = null
  else if (query.isPending) body = <div className="space-y-2"><Skeleton className="h-20" /><Skeleton className="h-32" /></div>
  else if (query.isError) {
    const missing = query.error instanceof ApiError && query.error.status === 404
    body = <ErrorState title={missing ? 'Pedido não encontrado.' : 'Não foi possível carregar o pedido.'} message={missing ? 'Ele pode ter sido de outro funcionário. Volte para a lista.' : undefined} onRetry={missing ? undefined : () => query.refetch()} />
  } else {
    const { adjustment: a, impact } = query.data
    body = (
      <div className="space-y-4">
        <div className="space-y-1">
          <p className="text-lg font-bold">{a.summary}</p>
          <p className="text-sm text-muted-foreground">{a.employeeName} · {shortDay(a.day)}</p>
          <AdjustmentStatus adjustment={a} />
        </div>
        {a.protected ? <ProtectedAbsence adjustment={a} /> : (
          <div className="space-y-1 text-sm">
            <p><span className="font-semibold">Justificativa:</span> {a.justification}</p>
            {a.documents.length > 0 ? (
              <ul aria-label="Documentos" className="space-y-1">
                {a.documents.map((d) => (
                  <li key={d.id}><a href={getDownloadTimeAdjustmentDocumentUrl(a.employeeId, a.id, d.id)} className="inline-flex min-h-11 items-center gap-1 font-semibold text-primary underline"><FileText aria-hidden="true" className="size-4" />{d.fileName}</a></li>
                ))}
              </ul>
            ) : null}
          </div>
        )}
        <p className="text-sm text-muted-foreground">Pedido por {a.requestedByName ?? 'usuário'} em {formatDateTime(a.requestedAt)}{a.source === 'HR' ? ' (lançamento do RH)' : ''}.</p>
        {a.decidedAt ? <p className="text-sm text-muted-foreground">Decidido por {a.decidedByName ?? 'usuário'} em {formatDateTime(a.decidedAt)}{a.decisionComment ? `: ${a.decisionComment}` : ''}.</p> : null}
        {a.kind === 'Absence' ? (
          <p className="text-sm">{impact.fullDayAbsence ? 'Abono do dia inteiro.' : `Abono de ${workedText(impact.absenceMinutesAfter ?? 0)}.`}</p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2">
            <DayList label="Antes" entries={impact.before} worked={impact.workedMinutesBefore} struck />
            <DayList label="Depois" entries={impact.after} worked={impact.workedMinutesAfter} struck={false} />
          </div>
        )}
        {a.status === 'Pending' && canApprove && !a.canDecide ? <p role="status" className="text-sm text-warn-ink">Você não pode aprovar um ajuste seu. Outra pessoa que aprova decide.</p> : null}
      </div>
    )
  }

  const adjustment = query.data?.adjustment
  const footer = adjustment?.status === 'Pending' && adjustment.canDecide ? (
    <>
      <button type="button" onClick={() => setRejecting(true)} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Reprovar</button>
      <button type="button" onClick={() => setApproving(true)} className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">Aprovar ajuste</button>
    </>
  ) : undefined

  return (
    <>
      <SidePanel open={!!id} onOpenChange={(o) => { if (!o) onClose() }} title="Pedido de ajuste" footer={footer}>{body}</SidePanel>
      {adjustment ? (
        <>
          <ConfirmDialog verb="Aprovar" done="Ajuste aprovado" title={`Aprovar o ajuste de ${adjustment.employeeName}?`} open={approving} onOpenChange={setApproving}
            description={`${adjustment.summary} em ${shortDay(adjustment.day)}. A marcação original nunca é apagada: o ajuste fica no histórico.`}
            onReload={() => query.refetch()}
            onConfirm={async () => { await approveTimeAdjustment(adjustment.id, { version: adjustment.version }); await refresh() }} />
          <ConfirmDialog verb="Reprovar" done="Ajuste reprovado" title={`Reprovar o ajuste de ${adjustment.employeeName}?`} open={rejecting} onOpenChange={setRejecting}
            description="Nada muda nas marcações. Quem pediu recebe o seu comentário." onOpen={() => setComment('')} onReload={() => query.refetch()}
            onConfirm={async () => {
              if (!comment.trim()) throw new DialogMessage('Escreva o motivo da reprovação.')
              await rejectTimeAdjustment(adjustment.id, { version: adjustment.version, comment: comment.trim() })
              await refresh()
            }}>
            <Field label="Comentário" required hint="Quem pediu recebe este texto.">
              {(p) => <textarea {...p} rows={3} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} className={inputClass} />}
            </Field>
          </ConfirmDialog>
        </>
      ) : null}
    </>
  )
}
