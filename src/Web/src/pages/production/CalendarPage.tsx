import { useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { getGetDefaultCalendarQueryKey, getListCalendarExceptionsQueryKey, updateCalendar, useGetDefaultCalendar, useListCalendarExceptions } from '@/api/generated/bemaqui'
import type { CalendarDetail, CalendarExceptionDetail, Weekday } from '@/api/generated/model'
import { ChipGroup } from '@/components/form/choice'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { toastDone } from '@/components/ui/toast'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { WEEKDAYS } from './calendar'
import { ExceptionPanel } from './ExceptionPanel'
import { ExceptionsTable } from './ExceptionsTable'
import { HolidaysPanel } from './HolidaysPanel'
import { ProductionTabs } from './ProductionTabs'

const thisYear = () => Number(new Intl.DateTimeFormat('en', { year: 'numeric', timeZone: 'America/Sao_Paulo' }).format(new Date()))

/** Produção → Calendário (CAD-03 CA 8 a 11): dias úteis e as exceções do ano, com a sugestão de feriados nacionais. */
export function CalendarPage() {
  const query = useGetDefaultCalendar()
  return (
    <>
      <PageHeading title="Produção: Calendário">
        <p className="mt-2 text-muted-foreground">Dias úteis, feriados e paradas que o planejamento usa para calcular os prazos.</p>
      </PageHeading>
      <ProductionTabs />
      {query.isPending ? <Skeleton className="h-96" /> : query.isError || !query.data ? <ErrorState onRetry={() => query.refetch()} /> : <CalendarBody calendar={query.data} />}
    </>
  )
}

function CalendarBody({ calendar }: { calendar: CalendarDetail }) {
  const canEdit = useCan('production.edit')
  const client = useQueryClient()
  const [days, setDays] = useState<Weekday[]>(calendar.workingDays)
  const [year, setYear] = useState(thisYear)
  const [editing, setEditing] = useState<{ exception: CalendarExceptionDetail | null } | null>(null)
  const [suggesting, setSuggesting] = useState(false)
  const exceptions = useListCalendarExceptions(calendar.id, { year })
  const daysDirty = [...days].sort().join() !== [...calendar.workingDays].sort().join()
  const refresh = () => void client.invalidateQueries({ queryKey: getListCalendarExceptionsQueryKey(calendar.id) })

  const saveDays = async () => {
    const saved = await updateCalendar(calendar.id, { workingDays: days })
    client.setQueryData(getGetDefaultCalendarQueryKey(), saved)
    toastDone('Dias úteis salvos')
  }

  return (
    <div className="space-y-6">
      <section aria-labelledby="dias-uteis" className="space-y-3">
        <h2 id="dias-uteis" className="text-lg font-semibold">Dias úteis</h2>
        <ChipGroup legend="Dias em que a fábrica trabalha" options={WEEKDAYS} value={days} onChange={setDays} disabled={!canEdit} required
          error={days.length === 0 ? 'Marque ao menos um dia útil.' : undefined} />
        {canEdit ? <button type="button" disabled={!daysDirty || days.length === 0} onClick={() => void saveDays()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar dias úteis</button> : null}
      </section>

      <section aria-labelledby="excecoes" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <h2 id="excecoes" className="text-lg font-semibold">Feriados e paradas de <span className="tabular-nums">{year}</span></h2>
            <button type="button" aria-label="Ano anterior" onClick={() => setYear(year - 1)} className="grid size-8 place-items-center rounded-md border border-border"><ChevronLeft aria-hidden="true" className="size-4" /></button>
            <button type="button" aria-label="Próximo ano" onClick={() => setYear(year + 1)} className="grid size-8 place-items-center rounded-md border border-border"><ChevronRight aria-hidden="true" className="size-4" /></button>
          </div>
          {canEdit ? (
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={() => setSuggesting(true)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Sugerir feriados nacionais de {year}</button>
              <button type="button" onClick={() => setEditing({ exception: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova exceção</button>
            </div>
          ) : null}
        </div>
        {exceptions.isPending ? <Skeleton className="h-48" /> : exceptions.isError ? <ErrorState onRetry={() => exceptions.refetch()} /> : exceptions.data.length === 0 ? (
          <EmptyState title={`Nenhum feriado cadastrado em ${year}.`} description="Os feriados e paradas tiram horas da capacidade dos centros nesses dias."
            action={canEdit ? <button type="button" onClick={() => setSuggesting(true)} className="text-sm font-semibold text-primary">Sugerir feriados nacionais</button> : undefined} />
        ) : <ExceptionsTable calendarId={calendar.id} year={year} items={exceptions.data} canEdit={canEdit} onEdit={(exception) => setEditing({ exception })} onChanged={refresh} />}
      </section>
      {editing ? <ExceptionPanel calendarId={calendar.id} exception={editing.exception} canEdit={canEdit} onClose={() => setEditing(null)} onSaved={() => { refresh(); setEditing(null) }} /> : null}
      {suggesting ? <HolidaysPanel calendarId={calendar.id} year={year} onClose={() => setSuggesting(false)} onSaved={() => { refresh(); setSuggesting(false) }} /> : null}
    </div>
  )
}
