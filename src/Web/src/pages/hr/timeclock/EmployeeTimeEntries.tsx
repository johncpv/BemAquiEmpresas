import { useState } from 'react'
import { useListEmployeeTimeEntries } from '@/api/generated/bemaqui'
import type { EmployeeDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { EmployeeTimeAdjustments } from './EmployeeTimeAdjustments'
import { InternalControlBanner } from './TimeClockPanel'
import { defaultRange, todayInSaoPaulo } from './time-clock'
import { TimeEntriesByDay } from './TimeEntriesView'

const field = 'min-h-11 rounded-md border border-input bg-background px-2 text-sm'

/** Aba "Ponto" do funcionário (/app/rh/funcionarios/$id?aba=ponto, RH-01a CA 12 e RH-01b): as marcações por dia do período, com hr.view, e os ajustes. */
export function EmployeeTimeEntries({ employee }: { employee: EmployeeDetail }) {
  const initial = defaultRange(todayInSaoPaulo())
  const [from, setFrom] = useState(initial.from)
  const [to, setTo] = useState(initial.to)
  const canSensitive = useCan('hr.view_sensitive')
  const query = useListEmployeeTimeEntries(employee.id, { from, to }, { query: { retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2 } })

  let content
  if (query.isPending) content = <div className="space-y-2"><Skeleton className="h-24" /></div>
  else if (query.isError) {
    const invalid = query.error instanceof ApiError && query.error.status === 422
    content = <ErrorState title={invalid && query.error instanceof ApiError ? query.error.message : 'Não foi possível carregar as marcações.'} onRetry={invalid ? undefined : () => query.refetch()} />
  } else if (query.data.items.length === 0) {
    content = <EmptyState title="Nenhuma marcação no período." description={`${employee.name} marca o ponto em "Meu ponto", no celular, no computador ou no tablet da fábrica, com o usuário ligado ao cadastro.`} />
  } else content = <TimeEntriesByDay items={query.data.items} canSensitive={canSensitive} showEmployee={false} />

  return (
    <div className="max-w-3xl space-y-4">
      <InternalControlBanner />
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col text-xs font-semibold">De
          <input type="date" className={field} value={from} max={to} onChange={(e) => e.target.value && setFrom(e.target.value)} />
        </label>
        <label className="flex flex-col text-xs font-semibold">Até
          <input type="date" className={field} value={to} min={from} onChange={(e) => e.target.value && setTo(e.target.value)} />
        </label>
      </div>
      {content}
      <EmployeeTimeAdjustments employee={employee} />
    </div>
  )
}
