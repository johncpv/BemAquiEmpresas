import { useState } from 'react'
import { addHolidays, useSuggestHolidays } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { toastDone } from '@/components/ui/toast'
import { formatDate } from '@/lib/format'
import { weekdayOf } from './calendar'

/**
 * "Sugerir feriados nacionais de {ano}" (CAD-03 CA 10): a Sexta-feira Santa vem marcada; os pontos facultativos (Carnaval e
 * Corpus Christi) desmarcados; nada é gravado sem "Adicionar feriados".
 */
export function HolidaysPanel({ calendarId, year, onClose, onSaved }: { calendarId: string; year: number; onClose: () => void; onSaved: () => void }) {
  const query = useSuggestHolidays(calendarId, year)
  const [chosen, setChosen] = useState<Set<string> | null>(null)
  const [error, setError] = useState<string | null>(null)
  const items = query.data ?? []
  const selected = chosen ?? new Set(items.filter((h) => !h.optional && !h.alreadyAdded).map((h) => h.date))
  const toggle = (date: string, on: boolean) => { const next = new Set(selected); if (on) next.add(date); else next.delete(date); setChosen(next) }

  const add = async () => {
    setError(null)
    try {
      const result = await addHolidays(calendarId, { holidays: items.filter((h) => selected.has(h.date)).map((h) => ({ date: h.date, description: h.name })) })
      toastDone(result.added === 1 ? '1 feriado adicionado' : `${result.added} feriados adicionados`)
      onSaved()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível adicionar os feriados.')
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={`Feriados nacionais de ${year}`} description="Confira e desmarque o que não vale para a fábrica. Estaduais e municipais são cadastrados à mão."
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          <button type="button" disabled={selected.size === 0} onClick={() => void add()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Adicionar feriados</button>
        </>
      )}
    >
      <FormError message={error ?? undefined} />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : (
        <ul className="divide-y divide-border rounded-md border border-border">
          {items.map((h) => (
            <li key={h.date}>
              <label className="flex items-center gap-3 px-3 py-2 text-sm">
                <input type="checkbox" className="size-4" disabled={h.alreadyAdded} checked={h.alreadyAdded || selected.has(h.date)} onChange={(e) => toggle(h.date, e.target.checked)} />
                <span className="w-24 tabular-nums">{formatDate(h.date)}</span>
                <span className="w-8 text-muted-foreground">{weekdayOf(h.date)}</span>
                <span className="flex-1">{h.name}{h.optional ? <span className="text-muted-foreground"> · ponto facultativo</span> : null}</span>
                {h.alreadyAdded ? <span className="text-xs text-muted-foreground">Já cadastrado</span> : null}
              </label>
            </li>
          ))}
        </ul>
      )}
    </SidePanel>
  )
}
