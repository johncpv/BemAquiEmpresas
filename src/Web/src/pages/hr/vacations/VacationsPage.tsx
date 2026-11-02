import { Link } from '@tanstack/react-router'
import { useGetVacationOverview } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { periodTone, SCHEDULE_TONES, VACATION_DICAS } from './vacations'

const monthLabel = (iso: string) => new Date(`${iso}T12:00:00`).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })

/**
 * RH → Férias (/app/rh/ferias, FLH-03 CA 3): os vencimentos dos próximos 90 dias (e os vencidos, em destaque) e o calendário dos próximos 6
 * meses. Sem valor: sai com hr.view. Cada linha leva à aba "Férias" do funcionário, onde se programa.
 */
export function VacationsPage() {
  const query = useGetVacationOverview({ days: 90 })
  let content
  if (query.isPending) content = <div className="space-y-2"><Skeleton className="h-24" /><Skeleton className="h-48" /></div>
  else if (query.isError || !query.data) content = <ErrorState title="Não foi possível carregar as férias." message="Confira a conexão e tente de novo." onRetry={() => query.refetch()} />
  else {
    const { due, calendar } = query.data
    const empty = due.length === 0 && calendar.every((m) => m.entries.length === 0)
    content = empty ? (
      <EmptyState title="Nenhum período aquisitivo completo ainda." description="Quando um funcionário completar 12 meses de casa, o vencimento das férias aparece aqui."
        action={<Link to="/app/rh/funcionarios" className="font-semibold text-primary">Ver funcionários</Link>} />
    ) : (
      <div className="space-y-6">
        <section aria-labelledby="ferias-vencimentos" className="space-y-2">
          <h2 id="ferias-vencimentos" className="inline-flex items-center text-lg font-bold">Vencimentos nos próximos {query.data.days} dias<Dica nome="Período concessivo" texto={VACATION_DICAS.concessivo} /></h2>
          {due.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma férias vence nos próximos {query.data.days} dias.</p> : (
            <ul className="space-y-2">
              {due.map((d) => (
                <li key={d.periodId} className={'flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-3' + (d.status === 'Expired' ? ' border-l-4 border-l-crit' : d.tone === 'warn' ? ' border-l-4 border-l-warn' : '')}>
                  <div>
                    <Link to="/app/rh/funcionarios/$id" params={{ id: d.employeeId }} search={{ aba: 'ferias' } as never} className="font-semibold text-primary">{d.name}</Link>
                    {d.registration ? <span className="ml-2 text-sm tabular-nums text-muted-foreground">{d.registration}</span> : null}
                    <p className="text-sm text-muted-foreground tabular-nums">Período {formatDate(d.periodStart)} a {formatDate(d.periodEnd)} · saldo {d.balance} dias · concessivo até {formatDate(d.concessionEndsOn)}</p>
                  </div>
                  <StatusChip tone={periodTone(d.tone)}>{d.statusLabel}</StatusChip>
                </li>
              ))}
            </ul>
          )}
        </section>
        <section aria-labelledby="ferias-calendario" className="space-y-3">
          <h2 id="ferias-calendario" className="text-lg font-bold">Calendário</h2>
          {calendar.map((m) => (
            <div key={m.month} className="rounded-md border border-border bg-card">
              <p className="border-b border-border px-3 py-2 text-sm font-semibold first-letter:uppercase">{monthLabel(m.month)}</p>
              {m.entries.length === 0 ? <p className="px-3 py-2 text-sm text-muted-foreground">Ninguém de férias.</p> : (
                <ul className="divide-y divide-border">
                  {m.entries.map((e) => (
                    <li key={e.scheduleId} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
                      <Link to="/app/rh/funcionarios/$id" params={{ id: e.employeeId }} search={{ aba: 'ferias' } as never} className="font-semibold text-primary">{e.name}</Link>
                      <span className="flex items-center gap-2 tabular-nums">{formatDate(e.startsOn)} a {formatDate(e.endsOn)} · {e.days} dias<StatusChip tone={SCHEDULE_TONES[e.status] ?? 'neutral'}>{e.statusLabel}</StatusChip></span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ))}
        </section>
      </div>
    )
  }

  return (
    <>
      <PageHeading title="RH: Férias">
        <p className="mt-1 text-sm text-muted-foreground">Quem tem férias a vencer e quem está de férias. Programe pela aba "Férias" do funcionário.</p>
      </PageHeading>
      <HrTabs />
      {content}
    </>
  )
}
