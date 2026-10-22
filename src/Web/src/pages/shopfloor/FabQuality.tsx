import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useEffect } from 'react'
import { useGetQualityQueue } from '@/api/generated/bemaqui'
import type { QualityQueueItem } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FloorButton, FloorHeader, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { useHelp } from '@/help/help-context'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useProductionLive } from '@/pages/production/kanban/useBoardLive'
import { QualityInspection } from '@/pages/production/quality/QualityInspection'
import { refreshQuality } from '@/pages/production/quality/quality'

const backLink = 'inline-flex min-h-[56px] min-w-[56px] items-center justify-center rounded-md border-2 border-input bg-card px-4 text-[18px] font-semibold'

/** Cartão da OS na fila de qualidade (CA 2): o cartão inteiro é o alvo (≥ 56 px). Sem custo. */
export function QualityCard({ item, to }: { item: QualityQueueItem; to: '/fab/qualidade/$stepId' | '/app/producao/qualidade/$stepId' }) {
  return (
    <Link to={to} params={{ stepId: item.stepId }} data-floor-target="" aria-label={`${item.orderNumber}, ${item.stepName}, tentativa ${item.attempt}`}
      className={cn('block min-h-[56px] rounded-md border-2 bg-card p-4 text-left focus-visible:outline-4 focus-visible:outline-ring', item.canDecide ? 'border-border' : 'border-dashed border-border opacity-90')}>
      <span className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-display text-[20px] font-bold tabular-nums">{item.orderNumber}</span>
        <StageIndicator stages={item.stepNames} letters={item.codes} current={item.stepIndex} />
      </span>
      <span className="mt-1 block text-[20px] font-semibold">{item.stepName}</span>
      <span className="mt-1 block text-[18px]">{item.productName || 'Produto'}</span>
      <span className="block truncate text-base text-muted-foreground">{item.projectNumber} · {item.projectName}{item.partyName ? ` · ${item.partyName}` : ''}</span>
      <span className="mt-2 flex flex-wrap items-center gap-2 text-base">
        <span className="tabular-nums text-muted-foreground">Tentativa {item.attempt}</span>
        {item.promisedDate ? <span className="tabular-nums text-muted-foreground">· Prometido {formatDate(item.promisedDate)}</span> : null}
        {item.openNonConformities > 0 ? <StatusChip tone="crit">NC aberta</StatusChip> : null}
        {item.selfInspection ? <StatusChip tone="warn">Autoinspeção</StatusChip> : null}
        {!item.canDecide ? <StatusChip tone="neutral">Você fez parte do trabalho</StatusChip> : null}
        {item.status === 'InProgress' ? <StatusChip tone="info">Em inspeção</StatusChip> : null}
      </span>
    </Link>
  )
}

/** Fila de qualidade no tablet (/fab/qualidade, CA 2): "Aguardando você" (padrão) ou "Todas", no endereço. */
export function FabQualityQueuePage() {
  const help = useHelp()
  const navigate = useNavigate()
  const { filtro } = useSearch({ strict: false }) as { filtro?: string }
  const all = filtro === 'todas'
  const queue = useGetQualityQueue({ filter: all ? 'todas' : undefined }, { query: { retry: false } })
  useProductionLive(refreshQuality)
  useEffect(() => { document.title = 'Qualidade · App do operador · BemAqui Empresas' }, [])
  const header = (
    <FloorHeader title="Qualidade" subtitle="OS aguardando inspeção" onHelp={() => help.setOpen(true)}>
      <Link to="/fab" className={backLink}>Fila</Link>
    </FloorHeader>
  )
  const setFilter = (todas: boolean) => void navigate({ to: '/fab/qualidade', search: todas ? { filtro: 'todas' } : {} } as never)
  let body
  if (queue.isPending) body = <FloorSkeleton />
  else if (queue.isError) {
    body = queue.error instanceof ApiError && queue.error.status === 403
      ? <FloorState title="Você não decide a qualidade." message="Peça a quem administra os acessos a ação Aprovar ou reprovar a qualidade." />
      : <FloorState tone="error" title="Não foi possível carregar a fila de qualidade." message="Verifique a conexão e tente de novo." action={<FloorButton onClick={() => void queue.refetch()}>Tentar de novo</FloorButton>} />
  } else {
    const items = queue.data.items
    body = (
      <>
        <div role="group" aria-label="Filtro da fila" className="flex flex-wrap gap-[8px]">
          <FloorButton tone={!all ? 'primary' : 'secondary'} aria-pressed={!all} onClick={() => setFilter(false)}>Aguardando você ({queue.data.mineCount})</FloorButton>
          <FloorButton tone={all ? 'primary' : 'secondary'} aria-pressed={all} onClick={() => setFilter(true)}>Todas ({queue.data.totalCount})</FloorButton>
        </div>
        {items.length === 0 ? (
          <FloorState title="Nenhuma OS aguardando qualidade." message={!all && queue.data.totalCount > 0 ? 'As que estão na fila foram feitas por você: outra pessoa decide.' : 'Quando uma etapa terminar antes da qualidade, a OS aparece aqui.'}
            action={!all && queue.data.totalCount > 0 ? <FloorButton tone="secondary" onClick={() => setFilter(true)}>Ver todas</FloorButton> : <FloorButton tone="secondary" onClick={() => void queue.refetch()}>Atualizar</FloorButton>} />
        ) : <ul className="space-y-[12px]">{items.map((i) => <li key={i.stepId}><QualityCard item={i} to="/fab/qualidade/$stepId" /></li>)}</ul>}
      </>
    )
  }
  return <>{header}<div className="mx-auto max-w-3xl space-y-4 p-4"><h1 className="sr-only">Fila de qualidade</h1>{body}</div></>
}

/** Decisão no tablet (/fab/qualidade/$stepId, CA 3 a 8). */
export function FabQualityStepPage() {
  const { stepId } = useParams({ strict: false }) as { stepId: string }
  const help = useHelp()
  useEffect(() => { document.title = 'Inspeção · App do operador · BemAqui Empresas' }, [])
  return (
    <>
      <FloorHeader title="Inspeção de qualidade" onHelp={() => help.setOpen(true)}>
        <Link to="/fab/qualidade" className={backLink}>Fila</Link>
      </FloorHeader>
      <div className="mx-auto max-w-3xl p-4"><QualityInspection stepId={stepId} queue="/fab/qualidade" /></div>
    </>
  )
}