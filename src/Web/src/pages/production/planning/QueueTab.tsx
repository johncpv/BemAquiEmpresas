import { type Announcements, closestCenter, DndContext, type DragEndEvent, KeyboardSensor, PointerSensor, useSensor, useSensors } from '@dnd-kit/core'
import { SortableContext, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ArrowDown, ArrowUp, GripVertical } from 'lucide-react'
import { type FormEvent, useEffect, useMemo, useRef, useState } from 'react'
import {
  getGetPlanningQueueQueryKey, getGetPlanningSummaryQueryKey, getListPlanningQueueChangesQueryKey, useGetPlanningQueue, useListPlanningQueueChanges, usePreviewPlanningQueue,
  useSavePlanningQueue,
} from '@/api/generated/bemaqui'
import type { PlanningQueuePreviewRow, PlanningQueueRow, PlanningQueueView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import { forecastFix, forecastReasonText } from '@/pages/projects/projects'
import { hoursText } from './planning'
import {
  changeKindLabel, changeMoves, EDIT_BANNER, moveItem, movedAnnouncement, previewDates, QUEUE_DICAS, queueChip, READ_ONLY_NOTE, SAVED_MESSAGE, sameOrder, slackShort,
  worsenedLine,
} from './queue'

const primary = 'inline-flex min-h-[44px] items-center justify-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-[44px] items-center justify-center rounded-md border border-border bg-card px-4 text-sm font-semibold disabled:opacity-60'
const iconButton = 'inline-flex size-11 items-center justify-center rounded-md border border-border bg-card text-foreground disabled:opacity-40'
const action = 'inline-flex min-h-[44px] items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'

type PreviewState = 'idle' | 'loading' | 'error' | 'limited' | 'stale'

/**
 * Planejamento → Fila (PLN-02): os projetos com OS de fabricação aberta na ordem da fila; com production.edit, arrastar (dnd-kit, com
 * teclado) ou "Mover para cima/baixo" e "Mover para a posição…" põe a fila em edição, a prévia "antes → depois" chega pela simulação e
 * "Salvar ordem" grava (avisando antes quem passa a atrasar). A 400 px não há arraste: "Subir" e "Descer" em cada linha.
 */
export function QueueTab({ canEdit }: { canEdit: boolean }) {
  const query = useGetPlanningQueue()
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) {
    return <ErrorState title="Não foi possível carregar a fila." message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => query.refetch()} />
  }

  if (query.data.rows.length === 0) {
    return (
      <EmptyState title="Nenhum projeto com OS de fabricação aberta." description="O projeto entra na fila sozinho, pela data prometida, quando ganha a primeira OS de fabricação."
        action={<Link to={'/app/producao/os' as never} className={action}>Ver OS de fabricação</Link>} />
    )
  }

  return <QueueEditor queue={query.data} canEdit={canEdit} reload={() => query.refetch()} />
}

function QueueEditor({ queue, canEdit, reload }: { queue: PlanningQueueView; canEdit: boolean; reload: () => Promise<unknown> }) {
  const client = useQueryClient()
  const saved = useMemo(() => queue.rows.map((r) => r.projectId), [queue.rows])
  const byId = useMemo(() => new Map(queue.rows.map((r) => [r.projectId, r])), [queue.rows])
  const [draft, setDraft] = useState<string[] | null>(null)
  const order = draft && sameOrder(draft, saved) ? saved : draft ?? saved
  const dirty = !!draft && !sameOrder(draft, saved)
  const [announcement, setAnnouncement] = useState('')
  const [preview, setPreview] = useState<Map<string, PlanningQueuePreviewRow> | null>(null)
  const [previewState, setPreviewState] = useState<PreviewState>('idle')
  const [confirming, setConfirming] = useState(false)
  const previewMutation = usePreviewPlanningQueue()
  const saveMutation = useSavePlanningQueue()
  const request = useRef(0)
  const orderKey = dirty ? order.join(',') : ''

  // Quem saiu ou entrou na fila desde a abertura (outra pessoa salvou, OS concluída): a edição em andamento não vale mais.
  useEffect(() => {
    setDraft((current) => (current && (current.length !== saved.length || current.some((id) => !byId.has(id))) ? null : current))
  }, [saved, byId])

  // CA 4: a prévia chega em até 2 s, pela simulação no servidor, sem gravar nada (uma por pausa na edição).
  useEffect(() => {
    if (!orderKey) {
      setPreview(null)
      setPreviewState('idle')
      return
    }

    setPreviewState('loading')
    const id = ++request.current
    const timer = setTimeout(() => {
      previewMutation.mutateAsync({ data: { order: orderKey.split(',') } }).then((result) => {
        if (id !== request.current) return
        setPreview(new Map(result.rows.map((r) => [r.projectId, r])))
        setPreviewState('idle')
      }, (error: unknown) => {
        if (id !== request.current) return
        setPreview(null)
        setPreviewState(error instanceof ApiError && error.status === 429 ? 'limited' : error instanceof ApiError && error.status === 422 ? 'stale' : 'error')
      })
    }, 400)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderKey])

  const numberOf = (id: string) => byId.get(id)?.projectNumber ?? 'Projeto'
  const move = (from: number, to: number) => {
    if (!canEdit || to < 0 || to >= order.length || from === to) return
    const next = moveItem(order, from, to)
    setDraft(next)
    setAnnouncement(movedAnnouncement(numberOf(order[from]), to + 1))
  }

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }), useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }))
  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return
    move(order.indexOf(String(active.id)), order.indexOf(String(over.id)))
  }

  const announcements: Announcements = {
    onDragStart: ({ active }) => `Projeto ${numberOf(String(active.id))} pego. Use as setas para mudar a posição e Espaço para soltar.`,
    onDragOver: ({ active, over }) => (over ? `Projeto ${numberOf(String(active.id))} sobre a posição ${order.indexOf(String(over.id)) + 1}` : ''),
    onDragEnd: ({ active, over }) => (over ? movedAnnouncement(numberOf(String(active.id)), order.indexOf(String(over.id)) + 1) : ''),
    onDragCancel: ({ active }) => `Movimento do projeto ${numberOf(String(active.id))} cancelado.`,
  }

  const refreshAll = async () => {
    await client.invalidateQueries({ queryKey: getGetPlanningQueueQueryKey() })
    await client.invalidateQueries({ queryKey: getGetPlanningSummaryQueryKey() })
    await client.invalidateQueries({ queryKey: getListPlanningQueueChangesQueryKey() })
  }

  const persist = async () => {
    await saveMutation.mutateAsync({ data: { order, version: queue.version } })
    setDraft(null)
    await refreshAll()
  }

  const discard = () => {
    setDraft(null)
    setAnnouncement('Ordem descartada. A fila voltou à ordem gravada.')
  }

  const previewRows = order.map((id) => preview?.get(id)).filter((r): r is PlanningQueuePreviewRow => !!r)
  const worse = worsenedLine(previewRows, numberOf)
  const save = async () => {
    if (worse) {
      setConfirming(true)
      return
    }

    try {
      await persist()
      notifySuccess(SAVED_MESSAGE)
    } catch (error) {
      if (error instanceof ApiError && (error.status === 409 || error.status === 422)) {
        notifyError(error.message, () => { setDraft(null); void reload() })
      } else {
        notifyError('Não foi possível salvar a ordem. Tente de novo.', () => void save())
      }
    }
  }

  const previewStatus = previewState === 'loading' ? 'Calculando o impacto…'
    : previewState === 'limited' ? 'Aguarde alguns segundos para simular de novo.'
      : previewState === 'stale' ? 'A fila mudou. Recarregue e reordene de novo.'
        : previewState === 'error' ? 'Não foi possível calcular o impacto agora. Você ainda pode salvar a ordem.'
          : worse ?? 'Nenhum projeto passa a atrasar com a ordem nova.'

  return (
    <div className="space-y-4">
      <p className="sr-only" aria-live="polite" role="status">{announcement}</p>
      {canEdit ? (
        <p className="text-sm text-muted-foreground">
          Arraste pela alça ou use os botões de mover para mudar a ordem. A prévia mostra a entrega prevista antes → depois de cada projeto, antes de salvar.
          <Dica nome="Prévia" texto={QUEUE_DICAS.previa} />
        </p>
      ) : (
        <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">{READ_ONLY_NOTE}</p>
      )}

      {dirty ? (
        <section aria-label="Edição da fila" className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-l-4 border-border border-l-warn bg-warn-soft p-3 text-sm text-warn-ink">
          <div className="min-w-0 space-y-1">
            <p className="font-semibold">{EDIT_BANNER}</p>
            <p aria-live="polite" className={cn(worse && previewState === 'idle' ? 'font-semibold' : undefined)}>{previewStatus}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button type="button" className={secondary} onClick={discard}>Descartar</button>
            <button type="button" className={primary} disabled={saveMutation.isPending || previewState === 'loading'} onClick={() => void save()}>
              {saveMutation.isPending ? 'Salvando…' : 'Salvar ordem'}
            </button>
          </div>
        </section>
      ) : queue.reorderedAt ? (
        <p className="text-sm text-muted-foreground">Última mudança manual em {formatDateTime(queue.reorderedAt)}{queue.reorderedBy ? `, por ${queue.reorderedBy}` : ''}.</p>
      ) : null}

      <div className="rounded-md border border-border bg-card">
        <div aria-hidden="true" className="hidden grid-cols-[2.75rem_2.5rem_minmax(0,2fr)_minmax(0,1.4fr)_3rem_4.5rem_6rem_6rem_5rem_7.5rem_auto] items-center gap-3 border-b border-border px-3 py-2 text-xs font-semibold text-muted-foreground lg:grid">
          <span />
          <span>Pos.</span>
          <span>Projeto</span>
          <span>Cliente</span>
          <span className="text-right">OS</span>
          <span className="text-right">Horas</span>
          <span>Prometido</span>
          <span>Previsto</span>
          <span>Folga</span>
          <span>Prazo</span>
          <span />
        </div>
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}
          accessibility={{ announcements, screenReaderInstructions: { draggable: 'Para mover, aperte Espaço, use as setas para cima e para baixo e aperte Espaço de novo para soltar. Esc cancela.' } }}>
          <SortableContext items={order} strategy={verticalListSortingStrategy} disabled={!canEdit}>
            <ol aria-label="Fila de projetos" className="divide-y divide-border">
              {order.map((id, index) => (
                <QueueRow key={id} row={byId.get(id)!} index={index} total={order.length} canEdit={canEdit} preview={dirty ? preview?.get(id) : undefined}
                  previewLoading={dirty && previewState === 'loading'} onMove={move} />
              ))}
            </ol>
          </SortableContext>
        </DndContext>
      </div>

      <QueueChanges />

      <ConfirmDialog open={confirming} onOpenChange={setConfirming} verb="Salvar mesmo assim" done={SAVED_MESSAGE} title="Salvar a ordem nova?"
        description={worse ?? undefined} onConfirm={persist} onReload={async () => { setDraft(null); await reload() }} />
    </div>
  )
}

interface RowProps {
  row: PlanningQueueRow
  index: number
  total: number
  canEdit: boolean
  preview?: PlanningQueuePreviewRow
  previewLoading: boolean
  onMove: (from: number, to: number) => void
}

function QueueRow({ row, index, total, canEdit, preview, previewLoading, onMove }: RowProps) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({ id: row.projectId, disabled: !canEdit })
  const [askPosition, setAskPosition] = useState(false)
  const chip = queueChip(row)
  const fix = row.hasForecast ? null : forecastFix(row.noForecastReason, row.reasonOrderId, row.projectId)
  const worse = preview?.worsened
  const submitPosition = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const value = Number(new FormData(event.currentTarget).get('posicao'))
    if (Number.isInteger(value) && value >= 1 && value <= total) {
      onMove(index, value - 1)
      setAskPosition(false)
    }
  }

  return (
    <li ref={setNodeRef} style={{ transform: CSS.Transform.toString(transform), transition }} data-testid="fila-linha" data-project={row.projectNumber}
      className={cn('relative bg-card px-3 py-2', isDragging && 'z-10 shadow-lg', worse && 'border-l-4 border-l-warn bg-warn-soft')}>
      <div className="grid grid-cols-[2.5rem_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[2.75rem_2.5rem_minmax(0,2fr)_minmax(0,1.4fr)_3rem_4.5rem_6rem_6rem_5rem_7.5rem_auto]">
        {canEdit ? (
          <button type="button" ref={setActivatorNodeRef} {...attributes} {...listeners} aria-label={`Arrastar ${row.projectNumber}`}
            className="hidden size-11 cursor-grab touch-none items-center justify-center rounded-md text-muted-foreground lg:inline-flex">
            <GripVertical aria-hidden="true" className="size-5" />
          </button>
        ) : <span className="hidden lg:block" />}
        <span className="font-bold tabular-nums" aria-label={`Posição ${index + 1}`}>{index + 1}</span>
        <span className="min-w-0">
          <Link to={'/app/projetos/$id' as never} params={{ id: row.projectId } as never} className="font-semibold text-primary tabular-nums hover:underline">{row.projectNumber}</Link>
          <span className="block truncate text-sm text-muted-foreground">{row.projectName}</span>
          <span className="mt-1 flex flex-wrap items-center gap-2 text-sm lg:hidden">
            <span className="tabular-nums">Previsto {row.forecastDate ? formatDate(row.forecastDate) : '—'}</span>
            <StatusChip tone={chip.tone}>{chip.label}</StatusChip>
          </span>
        </span>
        <span className="hidden truncate text-sm lg:block">{row.partyName ?? '—'}</span>
        <span className="hidden text-right text-sm tabular-nums lg:block">{row.openOrders}</span>
        <span className="hidden text-right text-sm tabular-nums lg:block">{hoursText(row.remainingHours)}</span>
        <span className="hidden text-sm tabular-nums lg:block">{row.promisedDate ? formatDate(row.promisedDate) : '—'}</span>
        <span className="hidden text-sm tabular-nums lg:block">{row.forecastDate ? formatDate(row.forecastDate) : '—'}</span>
        <span className="hidden text-sm tabular-nums lg:block">{slackShort(row.slackDays)}</span>
        <span className="hidden lg:block"><StatusChip tone={chip.tone}>{chip.label}</StatusChip></span>
        {canEdit ? (
          <span className="flex items-center gap-1 justify-self-end">
            <button type="button" className={cn(iconButton, 'hidden lg:inline-flex')} aria-label={`Mover ${row.projectNumber} para cima`} title="Mover para cima" disabled={index === 0} onClick={() => onMove(index, index - 1)}>
              <ArrowUp aria-hidden="true" className="size-4" />
            </button>
            <button type="button" className={cn(iconButton, 'hidden lg:inline-flex')} aria-label={`Mover ${row.projectNumber} para baixo`} title="Mover para baixo" disabled={index === total - 1} onClick={() => onMove(index, index + 1)}>
              <ArrowDown aria-hidden="true" className="size-4" />
            </button>
            <button type="button" className={cn(secondary, 'px-3 lg:hidden')} disabled={index === 0} onClick={() => onMove(index, index - 1)} aria-label={`Subir ${row.projectNumber}`}>Subir</button>
            <button type="button" className={cn(secondary, 'px-3 lg:hidden')} disabled={index === total - 1} onClick={() => onMove(index, index + 1)} aria-label={`Descer ${row.projectNumber}`}>Descer</button>
            <button type="button" className={cn(secondary, 'hidden px-3 lg:inline-flex')} aria-expanded={askPosition} onClick={() => setAskPosition((v) => !v)}
              aria-label={`Mover ${row.projectNumber} para a posição…`}>Posição…</button>
          </span>
        ) : <span />}
      </div>

      {askPosition ? (
        <form onSubmit={submitPosition} className="mt-2 flex flex-wrap items-center gap-2 text-sm lg:ml-[5.25rem]">
          <label htmlFor={`posicao-${row.projectId}`}>Mover {row.projectNumber} para a posição</label>
          <input id={`posicao-${row.projectId}`} name="posicao" type="number" min={1} max={total} defaultValue={index + 1} autoFocus
            className="min-h-[44px] w-20 rounded-md border border-input bg-background px-2 tabular-nums" />
          <button type="submit" className={primary}>Mover</button>
          <button type="button" className={secondary} onClick={() => setAskPosition(false)}>Cancelar</button>
        </form>
      ) : null}

      {!row.hasForecast ? (
        <p className="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted-foreground lg:ml-[5.25rem]">
          <span>Sem previsão: {forecastReasonText(row.noForecastReason, row.reasonOrderNumber)}</span>
          {fix ? <Link to={fix.to as never} params={fix.params as never} search={fix.search as never} className="font-semibold text-primary hover:underline">{fix.label}</Link> : null}
        </p>
      ) : null}

      {preview ? (
        <p className={cn('mt-1 flex flex-wrap gap-x-3 text-sm tabular-nums lg:ml-[5.25rem]', worse ? 'font-semibold text-warn-ink' : 'text-muted-foreground')} data-testid="fila-previa">
          <span>Previsto {previewDates(preview)}</span>
          <span>Folga {slackShort(preview.slackAfter)}</span>
          {worse ? <span>Passa a atrasar {preview.daysLate === 1 ? '1 dia' : `${preview.daysLate} dias`}</span> : null}
        </p>
      ) : previewLoading ? <p className="mt-1 text-sm text-muted-foreground lg:ml-[5.25rem]">Calculando o impacto…</p> : null}
    </li>
  )
}

/** CA 8: mudanças manuais e automáticas, com quem (ou "Sistema"), quando, o motivo e as posições antes → depois. */
function QueueChanges() {
  const [open, setOpen] = useState(false)
  const query = useListPlanningQueueChanges({ pageSize: 20 }, { query: { enabled: open } })
  return (
    <section aria-labelledby="mudancas-fila" className="rounded-md border border-border bg-card">
      <h2 id="mudancas-fila" className="m-0">
        <button type="button" aria-expanded={open} onClick={() => setOpen((v) => !v)} className="flex min-h-[44px] w-full items-center justify-between px-3 text-left font-semibold">
          Mudanças na fila
          <span aria-hidden="true" className="text-muted-foreground">{open ? '−' : '+'}</span>
        </button>
      </h2>
      {open ? (
        <div className="border-t border-border p-3">
          {query.isPending ? <Skeleton className="h-16" /> : query.isError ? (
            <ErrorState title="Não foi possível carregar as mudanças." message="Tente de novo em instantes." onRetry={() => query.refetch()} />
          ) : query.data.items.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nenhuma mudança ainda. A fila é montada pela data prometida no primeiro cálculo do plano.</p>
          ) : (
            <ul className="space-y-2 text-sm">
              {query.data.items.map((c) => (
                <li key={c.id} className="rounded-md border border-border px-3 py-2">
                  <p className="flex flex-wrap gap-x-2">
                    <span className="font-semibold">{changeKindLabel(c.kind)}</span>
                    <span className="tabular-nums text-muted-foreground">{formatDateTime(c.at)}</span>
                    <span>por {c.who}</span>
                  </p>
                  <p>{c.reason}</p>
                  <p className="tabular-nums text-muted-foreground">{changeMoves(c)}</p>
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
    </section>
  )
}
