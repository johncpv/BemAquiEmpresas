import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useListEmployees, useListPositionWorkCenters, useListTimeEntries } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { InternalControlBanner } from './TimeClockPanel'
import { defaultRange, todayInSaoPaulo } from './time-clock'
import { TimeClockTabs } from './TimeClockTabs'
import { TimeEntriesByDay } from './TimeEntriesView'

const text = (v: unknown) => (typeof v === 'string' && v ? v : undefined)
const field = 'min-h-11 rounded-md border border-input bg-background px-2 text-sm'

/**
 * RH → Ponto → Marcações (/app/rh/ponto/marcacoes, RH-01a CA 1 e 12): marcações por dia com filtro de período, funcionário e centro na
 * URL (?de, ?ate, ?funcionario, ?centro), com hr.view; a localização só com hr.view_sensitive, sob demanda.
 */
export function TimeEntriesPage() {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const range = defaultRange(todayInSaoPaulo())
  const from = text(search.de) ?? range.from
  const to = text(search.ate) ?? range.to
  const employeeId = text(search.funcionario)
  const workCenterId = text(search.centro)
  const canSensitive = useCan('hr.view_sensitive')
  const employees = useListEmployees({ pageSize: 100, sort: 'name' })
  const centers = useListPositionWorkCenters()
  const query = useListTimeEntries({ from, to, employeeId, workCenterId }, { query: { retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2 } })

  const set = (patch: Record<string, string | undefined>) =>
    void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => {
      const next = { ...prev, ...patch } as Record<string, unknown>
      for (const k of Object.keys(next)) if (!next[k]) delete next[k]
      return next
    } } as never)

  let content
  if (query.isPending) {
    content = <div className="space-y-2"><Skeleton className="h-24" /><Skeleton className="h-24" /></div>
  } else if (query.isError) {
    const invalid = query.error instanceof ApiError && query.error.status === 422
    content = <ErrorState title={invalid && query.error instanceof ApiError ? query.error.message : 'Não foi possível carregar as marcações.'} message={invalid ? 'Ajuste o período e tente de novo.' : undefined} onRetry={invalid ? undefined : () => query.refetch()} />
  } else if (query.data.items.length === 0) {
    content = (
      <EmptyState title="Nenhuma marcação no período."
        description="As marcações aparecem aqui quando os funcionários registram o ponto no app (Meu ponto) ou no tablet da fábrica."
        action={<Link to="/app/rh/funcionarios" className="text-sm font-semibold text-primary">Ver os funcionários e o vínculo com o usuário</Link>} />
    )
  } else {
    content = (
      <>
        {query.data.truncated ? <p role="status" className="mb-2 text-sm text-warn-ink">Muitas marcações no período: mostrando as primeiras. Filtre por funcionário ou centro.</p> : null}
        <TimeEntriesByDay items={query.data.items} canSensitive={canSensitive} showEmployee />
      </>
    )
  }

  return (
    <>
      <PageHeading title="RH: Ponto">
        <p className="mt-1 text-muted-foreground">Marcações de cada funcionário por dia, para o espelho, o banco de horas e a folha.</p>
      </PageHeading>
      <HrTabs />
      <div className="max-w-4xl space-y-4">
        <TimeClockTabs />
        <InternalControlBanner />
        <form aria-label="Filtros das marcações" className="flex flex-wrap items-end gap-3" onSubmit={(e) => e.preventDefault()}>
          <label className="flex flex-col text-xs font-semibold">De
            <input type="date" className={field} value={from} max={to} onChange={(e) => set({ de: e.target.value || undefined })} />
          </label>
          <label className="flex flex-col text-xs font-semibold">Até
            <input type="date" className={field} value={to} min={from} onChange={(e) => set({ ate: e.target.value || undefined })} />
          </label>
          <label className="flex min-w-48 flex-col text-xs font-semibold">Funcionário
            <select className={field} value={employeeId ?? ''} onChange={(e) => set({ funcionario: e.target.value || undefined })}>
              <option value="">Todos</option>
              {(employees.data?.items ?? []).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </label>
          <label className="flex min-w-40 flex-col text-xs font-semibold">Centro
            <select className={field} value={workCenterId ?? ''} onChange={(e) => set({ centro: e.target.value || undefined })}>
              <option value="">Todos</option>
              {(centers.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </label>
        </form>
        {content}
      </div>
    </>
  )
}
