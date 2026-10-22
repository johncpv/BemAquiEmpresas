import { useGetEmployeeTimeline, useListEmployeeLeavesSensitive } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate, formatDateTime, formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { LEAVE_TYPE_LABELS, todayIso } from './employee'
import { EmployeeProtected } from './EmployeeProtected'

const money = (v: string | null) => (v === null ? '—' : formatMoney(Number(v)))

/**
 * Linha do tempo (CA 16): trocas de cargo com hr.view; reajustes só com hr.view_sensitive (o servidor filtra). Afastamentos
 * (CAD-04c1) só pela rota /leaves/sensitive: sem a permissão, nada é pedido e aparece "Dados protegidos".
 */
export function EmployeeTimeline({ id }: { id: string }) {
  const canSensitive = useCan('hr.view_sensitive')
  return (
    <div className="max-w-3xl space-y-6">
      <section aria-labelledby="afastamentos" className="space-y-3">
        <h2 id="afastamentos" className="text-base font-semibold">Afastamentos</h2>
        {canSensitive ? <EmployeeLeaves id={id} /> : <EmployeeProtected />}
      </section>
      <section aria-labelledby="carreira" className="space-y-3">
        <h2 id="carreira" className="text-base font-semibold">Cargo e salário</h2>
        <CareerTimeline id={id} canSensitive={canSensitive} />
      </section>
    </div>
  )
}

function EmployeeLeaves({ id }: { id: string }) {
  const query = useListEmployeeLeavesSensitive(id)
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os afastamentos." onRetry={() => query.refetch()} />
  if (query.data.length === 0) {
    return <EmptyState title="Nenhum afastamento registrado." description='Use "Registrar afastamento" no topo: a situação fica Afastado entre o início e o fim previsto e volta a Ativo sozinha.' />
  }

  const today = todayIso()
  return (
    <ol className="space-y-3">
      {query.data.map((l) => {
        const current = l.startDate <= today && today <= l.expectedEndDate
        const future = l.startDate > today
        return (
          <li key={l.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <b>Afastamento: {LEAVE_TYPE_LABELS[l.type]}</b>
              {current ? <StatusChip tone="warn">Vigente</StatusChip> : future ? <StatusChip tone="info">Previsto</StatusChip> : <StatusChip tone="neutral">Encerrado</StatusChip>}
            </div>
            <p className="mt-1 tabular-nums">
              {formatDate(l.startDate)} a {formatDate(l.expectedEndDate)} · volta prevista em {formatDate(l.expectedReturnDate)}
            </p>
            <p className="mt-1 text-muted-foreground">{l.reason ? l.reason + ' · ' : ''}registrado em {formatDateTime(l.createdAt)}</p>
          </li>
        )
      })}
    </ol>
  )
}

function CareerTimeline({ id, canSensitive }: { id: string; canSensitive: boolean }) {
  const query = useGetEmployeeTimeline(id)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  if (query.data.length === 0) {
    return <EmptyState title="Nenhuma troca de cargo ou reajuste ainda." description={canSensitive ? 'Use "Trocar cargo" ou "Reajustar salário": cada mudança entra aqui, com a vigência e o motivo.' : 'Use "Trocar cargo": cada mudança entra aqui, com a vigência e o motivo. Os reajustes só aparecem com a permissão Ver dados sensíveis.'} />
  }

  return (
    <ol className="space-y-3">
      {query.data.map((e) => (
        <li key={e.id} className="rounded-md border border-border bg-card p-3 text-sm">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <b>{e.kind === 'Salary' ? 'Reajuste de salário' : 'Troca de cargo'}</b>
            <span className="tabular-nums text-muted-foreground">Vigência {formatDate(e.effectiveDate)}</span>
          </div>
          <p className="mt-1 tabular-nums">
            {e.kind === 'Salary' ? `${money(e.oldValue)} → ${money(e.newValue)}` : `${e.oldValue ?? '—'} → ${e.newValue}`}
          </p>
          <p className="mt-1 text-muted-foreground">{e.reason} · registrado em {formatDateTime(e.createdAt)}</p>
        </li>
      ))}
    </ol>
  )
}