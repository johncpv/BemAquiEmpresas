import { type KeyboardEvent, useRef, useState } from 'react'
import type { FactoryLoadRow, FactoryLoadWeek } from '@/api/generated/model'
import { StatusChip } from '@/components/ui/status-chip'
import { cn } from '@/lib/utils'
import { bandChip, cellLabel, hoursOf, percentText, weekLabel } from './load'

/**
 * Tabela analítica da ocupação (PLN-04 CA 7, padrão 3 das Fases 4 a 8): a coluna do centro fica fixa na rolagem horizontal e a navegação é
 * por célula (setas, Home/End na linha, Ctrl+Home/End no canto, Enter ou Espaço abre o drill-down). role="grid" com foco móvel (um só
 * Tab entra e sai da tabela). Fica local até o merge da FIN-08, que traz a `TreeTable` com coluna fixa e navegação por célula.
 */
export function OccupancyGrid({ label, weeks, rows, onOpen }: {
  label: string
  weeks: FactoryLoadWeek[]
  /** Os centros e, por último, a linha "Fábrica". */
  rows: FactoryLoadRow[]
  onOpen: (row: FactoryLoadRow, weekIndex: number) => void
}) {
  const [focus, setFocus] = useState<[number, number]>([0, 0])
  const refs = useRef(new Map<string, HTMLTableCellElement>())
  const key = (r: number, c: number) => `${r}:${c}`
  const lastRow = rows.length - 1
  const lastCol = weeks.length - 1
  const [fr, fc] = [Math.min(focus[0], lastRow), Math.min(focus[1], lastCol)]

  const go = (r: number, c: number) => {
    const next: [number, number] = [Math.max(0, Math.min(lastRow, r)), Math.max(0, Math.min(lastCol, c))]
    setFocus(next)
    refs.current.get(key(...next))?.focus()
  }

  const keys = (event: KeyboardEvent, r: number, c: number) => {
    const ctrl = event.ctrlKey || event.metaKey
    const moves: Record<string, () => void> = {
      ArrowRight: () => go(r, c + 1),
      ArrowLeft: () => go(r, c - 1),
      ArrowDown: () => go(r + 1, c),
      ArrowUp: () => go(r - 1, c),
      Home: () => (ctrl ? go(0, 0) : go(r, 0)),
      End: () => (ctrl ? go(lastRow, lastCol) : go(r, lastCol)),
      Enter: () => onOpen(rows[r], c),
      ' ': () => onOpen(rows[r], c),
    }
    const move = moves[event.key]
    if (!move) return
    event.preventDefault()
    move()
  }

  return (
    <div className="overflow-x-auto rounded-md border border-border bg-card">
      <table role="grid" aria-label={label} className="w-full border-collapse text-sm">
        <thead className="border-b border-border text-xs text-muted-foreground">
          <tr>
            <th scope="col" className="sticky left-0 z-10 min-w-40 bg-card px-3 py-2 text-left font-semibold">Centro de trabalho</th>
            {weeks.map((w) => <th key={w.start} scope="col" className="min-w-32 px-3 py-2 text-right font-semibold tabular-nums">{weekLabel(w)}</th>)}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, r) => {
            const factory = row.workCenterId === null
            return (
              <tr key={row.workCenterId ?? 'fabrica'} className={cn('border-b border-border last:border-0', factory && 'border-t-2 font-semibold')}>
                <th scope="row" className="sticky left-0 z-10 bg-card px-3 py-2 text-left align-top">
                  <span className="block">{row.name}</span>
                  {row.line ? <span className="block text-xs font-normal text-muted-foreground">{row.line}</span> : null}
                </th>
                {row.cells.map((cell, c) => {
                  const chip = bandChip(cell.band)
                  return (
                    <td
                      key={cell.weekStart}
                      ref={(el) => { if (el) refs.current.set(key(r, c), el); else refs.current.delete(key(r, c)) }}
                      role="gridcell"
                      tabIndex={r === fr && c === fc ? 0 : -1}
                      aria-label={cellLabel(row, weeks[c], cell)}
                      data-band={cell.band}
                      onFocus={() => setFocus([r, c])}
                      onClick={() => { setFocus([r, c]); onOpen(row, c) }}
                      onKeyDown={(e) => keys(e, r, c)}
                      className={cn('cursor-pointer px-3 py-2 text-right align-top tabular-nums hover:bg-muted focus-visible:bg-muted focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring',
                        cell.band === 'Over' && 'bg-crit-soft text-crit-ink')}
                    >
                      <span className="block text-base font-semibold">{percentText(cell.percent)}</span>
                      <span className="block text-xs text-muted-foreground">{hoursOf(cell)}</span>
                      {chip ? <StatusChip tone={chip.tone} className="mt-1">{chip.label}</StatusChip> : null}
                    </td>
                  )
                })}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
