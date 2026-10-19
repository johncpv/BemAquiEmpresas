import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getListSchedulesQueryKey, useListSchedules } from '@/api/generated/bemaqui'
import type { ScheduleDetail } from '@/api/generated/model'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { formatHours, WEEKDAY_LABELS } from './hr'
import { HrTabs } from './HrTabs'
import { SchedulePanel } from './SchedulePanel'

/** RH → Jornadas (CAD-04a CA 15 e 16): horas por semana e por mês; edição no painel lateral. */
export function SchedulesPage() {
  const query = useListSchedules()
  const client = useQueryClient()
  const [editing, setEditing] = useState<{ schedule: ScheduleDetail | null } | null>(null)
  const newButton = <Can perm="hr.create"><button type="button" onClick={() => setEditing({ schedule: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova jornada</button></Can>
  return (
    <>
      <PageHeading title="RH: Jornadas">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Os horários de trabalho, que dão as horas mensais de cada cargo.</p>
          {newButton}
        </div>
      </PageHeading>
      <HrTabs />
      {query.isPending ? <Skeleton className="h-48" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : query.data.length === 0 ? (
        <EmptyState title="Cadastre as jornadas de trabalho da empresa." action={newButton} />
      ) : (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full text-sm">
            <caption className="sr-only">Jornadas de trabalho</caption>
            <thead className="bg-muted text-left text-xs text-muted-foreground">
              <tr><th scope="col" className="px-3 py-2">Nome</th><th scope="col" className="hidden px-3 py-2 sm:table-cell">Dias</th><th scope="col" className="px-3 py-2 text-right">Horas/semana</th><th scope="col" className="px-3 py-2">Situação</th></tr>
            </thead>
            <tbody>
              {query.data.map((s) => (
                <tr key={s.id} className="border-t border-border">
                  <td className="px-3 py-2"><button type="button" onClick={() => setEditing({ schedule: s })} className="font-semibold text-primary hover:underline">{s.name}</button></td>
                  <td className="hidden px-3 py-2 sm:table-cell">{s.days.map((d) => WEEKDAY_LABELS[d.weekday].slice(0, 3)).join(', ')}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{formatHours(s.weeklyHours)} h</td>
                  <td className="px-3 py-2"><StatusChip tone={s.isActive ? 'ok' : 'neutral'}>{s.isActive ? 'Ativa' : 'Inativa'}</StatusChip></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editing ? <SchedulePanel schedule={editing.schedule} onClose={() => setEditing(null)} onSaved={() => { void client.invalidateQueries({ queryKey: getListSchedulesQueryKey() }); setEditing(null) }} /> : null}
    </>
  )
}
