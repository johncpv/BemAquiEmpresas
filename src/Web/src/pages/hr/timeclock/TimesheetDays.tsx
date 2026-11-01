import { Link } from '@tanstack/react-router'
import { ShieldCheck } from 'lucide-react'
import type { TimesheetDayView, TimesheetDetail, TimesheetTotalsView } from '@/api/generated/model'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { dayTone, minutes, shortDay, TIMESHEET_DICAS } from './timesheet'

/** Totais no topo (CA 4 e 9): o mesmo total do drill-down dos dias; o banco só com acordo. */
export function TimesheetTotals({ totals }: { totals: TimesheetTotalsView }) {
  const items: [string, string][] = [
    ['Esperado', minutes(totals.expectedMinutes, '0 min')],
    ['Trabalhado', minutes(totals.workedMinutes, '0 min')],
    ['Extras', minutes(totals.overtimeMinutes, '0 min')],
    ['Faltas', `${minutes(totals.absenceMinutes, '0 min')}${totals.absenceDays ? ` (${totals.absenceDays} ${totals.absenceDays === 1 ? 'dia' : 'dias'})` : ''}`],
    ['Atrasos', minutes(totals.lateMinutes, '0 min')],
    ['Abonado', minutes(totals.justifiedMinutes, '0 min')],
  ]
  return (
    <section aria-label="Totais do período" className="space-y-2">
      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-6">
        {items.map(([label, value]) => (
          <div key={label} className="rounded-md border border-border bg-card px-3 py-2">
            <dt className="text-xs font-semibold text-muted-foreground">{label}</dt>
            <dd className="font-display text-lg font-bold tabular-nums">{value}</dd>
          </div>
        ))}
      </dl>
      {totals.bankText ? (
        <p className="inline-flex items-center rounded-md border border-info-soft bg-info-soft px-3 py-1.5 text-sm font-semibold text-info-ink tabular-nums">
          {totals.bankText}
          <Dica nome="Banco de horas" texto={TIMESHEET_DICAS.banco} />
        </p>
      ) : null}
    </section>
  )
}

interface DayActions {
  employeeId?: string
  canReview?: boolean
  onReview?: (day: TimesheetDayView) => void
}

/** O que faltou no dia inconsistente (CA 6) e os atalhos para resolver: lançar a marcação, ver os pedidos ou marcar como revisado. */
function Issues({ day, actions }: { day: TimesheetDayView; actions: DayActions }) {
  if (day.issues.length === 0) return null
  return (
    <div className="mt-1 space-y-1 text-xs">
      <p className="text-warn-ink">{day.issues.join(' · ')}</p>
      {day.reviewed ? (
        <p className="inline-flex items-center gap-1 text-muted-foreground"><ShieldCheck aria-hidden="true" className="size-3.5" />Dia revisado{day.reviewComment ? `: ${day.reviewComment}` : ''}</p>
      ) : actions.employeeId ? (
        <p className="flex flex-wrap gap-x-3 gap-y-1">
          <Link to="/app/rh/funcionarios/$id" params={{ id: actions.employeeId }} search={{ aba: 'ponto' } as never} className="font-semibold text-primary">Lançar marcação</Link>
          <Link to="/app/rh/ponto/ajustes" search={{ funcionario: actions.employeeId } as never} className="font-semibold text-primary">Ver pedidos de ajuste</Link>
          {actions.canReview && actions.onReview ? (
            <button type="button" onClick={() => actions.onReview?.(day)} className="font-semibold text-primary">Marcar como revisado</button>
          ) : null}
        </p>
      ) : null}
    </div>
  )
}

function Note({ day }: { day: TimesheetDayView }) {
  const parts = [day.holiday, day.leaveLabel, ...day.absences.map((a) => `${a.label}${a.minutes ? ` ${minutes(a.minutes)}` : ''}`)].filter(Boolean)
  return parts.length ? <p className="text-xs text-muted-foreground">{parts.join(' · ')}</p> : null
}

const entriesText = (day: TimesheetDayView) => day.entries.map((e) => e.time).join(' · ') || '—'

/**
 * Dias do espelho (CA 3, padrão 3): tabela com a coluna do dia fixa no computador e cartões a 400 px, com o trabalhado e a situação
 * sempre visíveis; faixa lateral âmbar no dia inconsistente.
 */
export function TimesheetDays({ sheet, actions = {} }: { sheet: TimesheetDetail; actions?: DayActions }) {
  const days = sheet.days.filter((d) => d.status !== 'NotEmployed' || d.entries.length > 0)
  return (
    <>
      <div className="hidden overflow-x-auto rounded-md border border-border md:block">
        <table className="w-full text-sm">
          <caption className="sr-only">Dias do espelho de {sheet.employeeName}</caption>
          <thead className="bg-muted text-left text-xs font-semibold text-muted-foreground">
            <tr>
              <th scope="col" className="sticky left-0 bg-muted px-3 py-2">Dia</th>
              <th scope="col" className="px-3 py-2">Marcações</th>
              <th scope="col" className="px-3 py-2 text-right">Esperado</th>
              <th scope="col" className="px-3 py-2 text-right">Trabalhado</th>
              <th scope="col" className="px-3 py-2 text-right">Extra</th>
              <th scope="col" className="px-3 py-2 text-right">Falta</th>
              <th scope="col" className="px-3 py-2 text-right">Atraso</th>
              <th scope="col" className="px-3 py-2 text-right">Abono</th>
              <th scope="col" className="px-3 py-2">Situação <Dica nome="Inconsistente" texto={TIMESHEET_DICAS.inconsistente} /></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {days.map((d) => (
              <tr key={d.day} className={cn(d.status === 'Inconsistent' && !d.reviewed && 'border-l-4 border-l-warn', d.status === 'Absent' && 'border-l-4 border-l-crit')}>
                <th scope="row" className="sticky left-0 bg-card px-3 py-2 text-left font-semibold tabular-nums">{shortDay(d.day)}</th>
                <td className="px-3 py-2 tabular-nums">
                  {entriesText(d)}
                  <Note day={d} />
                  <Issues day={d} actions={actions} />
                </td>
                <td className="px-3 py-2 text-right tabular-nums">{minutes(d.expectedMinutes)}</td>
                <td className="px-3 py-2 text-right font-semibold tabular-nums">{minutes(d.workedMinutes)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{minutes(d.overtimeMinutes)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{minutes(d.absenceMinutes)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{minutes(d.lateMinutes)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{minutes(d.justifiedMinutes + d.compensatedMinutes)}</td>
                <td className="px-3 py-2"><StatusChip tone={dayTone(d.status)}>{d.statusLabel}</StatusChip></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ul aria-label={`Dias do espelho de ${sheet.employeeName}`} className="space-y-2 md:hidden">
        {days.map((d) => (
          <li key={d.day} className={cn('rounded-md border border-border bg-card px-3 py-2', d.status === 'Inconsistent' && !d.reviewed && 'border-l-4 border-l-warn', d.status === 'Absent' && 'border-l-4 border-l-crit')}>
            <div className="flex items-center justify-between gap-2">
              <span className="font-semibold tabular-nums">{shortDay(d.day)}</span>
              <StatusChip tone={dayTone(d.status)}>{d.statusLabel}</StatusChip>
            </div>
            <p className="mt-1 text-sm tabular-nums">Trabalhado <b>{minutes(d.workedMinutes, '0 min')}</b> de {minutes(d.expectedMinutes, '0 min')}</p>
            <p className="text-xs text-muted-foreground tabular-nums">{entriesText(d)}</p>
            {d.overtimeMinutes || d.absenceMinutes || d.lateMinutes || d.justifiedMinutes ? (
              <p className="text-xs tabular-nums">
                {[d.overtimeMinutes ? `Extra ${minutes(d.overtimeMinutes)}` : '', d.absenceMinutes ? `Falta ${minutes(d.absenceMinutes)}` : '', d.lateMinutes ? `Atraso ${minutes(d.lateMinutes)}` : '', d.justifiedMinutes ? `Abono ${minutes(d.justifiedMinutes)}` : ''].filter(Boolean).join(' · ')}
              </p>
            ) : null}
            <Note day={d} />
            <Issues day={d} actions={actions} />
          </li>
        ))}
      </ul>
    </>
  )
}
