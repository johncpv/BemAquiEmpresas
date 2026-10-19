import { deleteCalendarException } from '@/api/generated/bemaqui'
import type { CalendarExceptionDetail } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { HOURS_DICA, TYPE_LABEL, weekdayOf } from './calendar'
import { formatHours } from './shifts'

/** Exceções do ano (CA 8 e 9): data, tipo, horas disponíveis e descrição; a data abre o painel. */
export function ExceptionsTable({ calendarId, year, items, canEdit, onEdit, onChanged }: { calendarId: string; year: number; items: CalendarExceptionDetail[]; canEdit: boolean; onEdit: (e: CalendarExceptionDetail) => void; onChanged: () => void }) {
  return (
    <div className="overflow-x-auto rounded-md border border-border">
      <table className="w-full text-sm">
        <caption className="sr-only">Feriados e paradas de {year}</caption>
        <thead className="bg-muted text-left text-xs text-muted-foreground">
          <tr>
            <th scope="col" className="px-3 py-2">Data</th>
            <th scope="col" className="px-3 py-2">Tipo</th>
            <th scope="col" className="px-3 py-2 text-right"><span className="inline-flex items-center gap-1">Horas disponíveis <Dica nome="Horas disponíveis" texto={HOURS_DICA} /></span></th>
            <th scope="col" className="hidden px-3 py-2 sm:table-cell">Descrição</th>
            <th scope="col" className="px-3 py-2"><span className="sr-only">Ações</span></th>
          </tr>
        </thead>
        <tbody>
          {items.map((e) => (
            <tr key={e.id} className="border-t border-border">
              <td className="px-3 py-2 tabular-nums">
                <button type="button" onClick={() => onEdit(e)} className="font-semibold text-primary hover:underline">{formatDate(e.date)}</button> <span className="text-muted-foreground">{weekdayOf(e.date)}</span>
              </td>
              <td className="px-3 py-2"><StatusChip tone={e.type === 'Holiday' ? 'info' : 'warn'}>{TYPE_LABEL[e.type]}</StatusChip></td>
              <td className="px-3 py-2 text-right tabular-nums">{formatHours(e.hoursAvailable)} h</td>
              <td className="hidden px-3 py-2 sm:table-cell">{e.description}</td>
              <td className="px-3 py-2 text-right">
                {canEdit ? (
                  <ConfirmDialog verb="Excluir" done="Excluída" destructive title={`Excluir a exceção de ${formatDate(e.date)}?`} description="O dia volta a ter as horas normais dos centros."
                    onConfirm={async () => { await deleteCalendarException(calendarId, e.id); onChanged() }}
                    trigger={<button type="button" className="text-sm font-semibold text-crit-ink">Excluir<span className="sr-only"> a exceção de {formatDate(e.date)}</span></button>} />
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
