import { Lock, MapPin, MapPinOff } from 'lucide-react'
import { useState } from 'react'
import { listEmployeeTimeEntryLocations } from '@/api/generated/bemaqui'
import type { TimeEntryLocationRow, TimeEntryRow } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { dayLabel, entryLabel, formatTime, groupByDay, locationText, sourceLabel, TIME_CLOCK_DICAS } from './time-clock'

/** "Ver localização" (CA 11): só com hr.view_sensitive, pela rota /sensitive (a leitura fica registrada); mostra as coordenadas reduzidas. */
export function LocationButton({ employeeId, day, entryId }: { employeeId: string; day: string; entryId: string }) {
  const [row, setRow] = useState<TimeEntryLocationRow | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const load = async () => {
    setBusy(true)
    setError(null)
    try {
      const rows = await listEmployeeTimeEntryLocations(employeeId, { from: day, to: day })
      setRow(rows.find((r) => r.id === entryId) ?? null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível carregar a localização.')
    } finally {
      setBusy(false)
    }
  }

  if (row !== undefined) return <span className="inline-flex items-center gap-1 text-xs tabular-nums"><MapPin aria-hidden="true" className="size-3.5" />{locationText(row?.latitude, row?.longitude, row?.accuracyM)}</span>
  return (
    <>
      <button type="button" onClick={() => void load()} disabled={busy} aria-label="Ver localização (dado protegido)"
        className="inline-flex min-h-11 items-center gap-1 rounded-md border border-border px-2 text-xs font-semibold">
        <MapPin aria-hidden="true" className="size-3.5" />{busy ? 'Carregando…' : 'Ver localização'}
      </button>
      {error ? <span role="alert" className="text-xs text-crit-ink">{error}</span> : null}
    </>
  )
}

/** Bloco "Dados protegidos" padrão (padrão 8) para quem não tem hr.view_sensitive. */
export function ProtectedLocation() {
  return (
    <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
      <Lock aria-hidden="true" className="size-3" />Dados protegidos
    </span>
  )
}

/**
 * Marcações por dia (CA 12): hora local, tipo, origem (App, PIN no {aparelho}, Manual), "Sem localização" quando cabe e o botão de
 * localização só para quem tem hr.view_sensitive. A lista nunca traz a localização: ela vem da rota /sensitive, sob demanda.
 */
export function TimeEntriesByDay({ items, canSensitive, showEmployee }: { items: readonly TimeEntryRow[]; canSensitive: boolean; showEmployee: boolean }) {
  return (
    <ul aria-label="Marcações por dia" className="space-y-3">
      {groupByDay(items).map((g) => (
        <li key={g.day} className="rounded-md border border-border bg-card">
          <p className="border-b border-border px-3 py-2 text-sm font-semibold">{dayLabel(g.day)}</p>
          <ul className="divide-y divide-border">
            {g.items.map((e) => (
              <li key={e.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm">
                {showEmployee ? <span className="min-w-0 flex-1 basis-40 font-semibold">{e.employeeName}</span> : null}
                <span className="w-44 font-medium">{entryLabel(e.type)}</span>
                <span className="w-12 tabular-nums">{formatTime(e.at)}</span>
                <span className="text-muted-foreground">{sourceLabel(e.source, e.deviceName)}</span>
                {e.status === 'Voided' ? <StatusChip tone="neutral">Desconsiderada</StatusChip> : null}
                {e.location === 'Missing' ? (
                  <span className="inline-flex items-center">
                    <StatusChip tone="neutral"><MapPinOff aria-hidden="true" className="size-3" />Sem localização</StatusChip>
                    <Dica nome="Sem localização" texto={TIME_CLOCK_DICAS.semLocalizacao} />
                  </span>
                ) : null}
                {e.location === 'Captured' ? (canSensitive ? <LocationButton employeeId={e.employeeId} day={e.day} entryId={e.id} /> : <ProtectedLocation />) : null}
              </li>
            ))}
          </ul>
        </li>
      ))}
    </ul>
  )
}
