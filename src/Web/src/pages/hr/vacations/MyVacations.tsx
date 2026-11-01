import { useState } from 'react'
import { useListMyVacations } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { NOT_LINKED } from '../timeclock/time-clock'
import { SCHEDULE_TONES, VACATION_DICAS } from './vacations'

function Content() {
  const query = useListMyVacations({ query: { retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2 } })
  if (query.isPending) return <Skeleton className="h-20" />
  if (query.isError) {
    const notLinked = query.error instanceof ApiError && query.error.code === 'employee_not_active'
    return notLinked ? <EmptyState title="Férias indisponíveis" description={NOT_LINKED} /> : <ErrorState title="Não foi possível carregar as suas férias." onRetry={() => query.refetch()} />
  }

  const { periods, schedules } = query.data
  if (periods.length === 0) return <EmptyState title="Nenhum período aquisitivo ainda." description="O primeiro período fica completo 12 meses depois da sua admissão." />
  return (
    <div className="space-y-3">
      <ul aria-label="Meus períodos de férias" className="space-y-2">
        {periods.map((p) => (
          <li key={p.startsOn} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm">
            <span className="tabular-nums">{formatDate(p.startsOn)} a {formatDate(p.endsOn)} · saldo <b>{p.balance}</b> de {p.entitledDays} dias</span>
            <StatusChip tone={p.status === 'Expired' ? 'crit' : p.status === 'Available' ? 'ok' : 'neutral'}>{p.statusLabel}</StatusChip>
          </li>
        ))}
      </ul>
      {schedules.length > 0 ? (
        <ul aria-label="Minhas férias programadas" className="space-y-2">
          {schedules.map((s) => (
            <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm">
              <span className="font-semibold tabular-nums">{formatDate(s.startsOn)} a {formatDate(s.endsOn)} · {s.days} dias{s.soldDays > 0 ? ` + ${s.soldDays} vendidos` : ''}</span>
              <StatusChip tone={SCHEDULE_TONES[s.status] ?? 'neutral'}>{s.statusLabel}</StatusChip>
            </li>
          ))}
        </ul>
      ) : <p className="text-sm text-muted-foreground">Nenhuma férias programada. O RH avisa 30 dias antes do início.</p>}
    </div>
  )
}

/** "Minhas férias" no "Meu ponto" (FLH-03 CA 5 e 12, ADR 0035): as datas e o saldo, sem valor; só carrega quando a pessoa abre. */
export function MyVacations() {
  const [open, setOpen] = useState(false)
  return (
    <section aria-labelledby="minhas-ferias" className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <h2 id="minhas-ferias" className="inline-flex items-center text-lg font-bold">Minhas férias<Dica nome="Período concessivo" texto={VACATION_DICAS.concessivo} /></h2>
        <button type="button" onClick={() => setOpen((v) => !v)} aria-expanded={open} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">{open ? 'Ocultar' : 'Ver férias e saldo'}</button>
      </div>
      {open ? <Content /> : null}
    </section>
  )
}
