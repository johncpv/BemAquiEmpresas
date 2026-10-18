import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react'
import type { ReactNode } from 'react'

interface RowsEditorProps<T> {
  /** Nome da linha no singular, usado nos rótulos ("Adicionar parcela", "Remover parcela 2"). */
  noun: string
  rows: T[]
  onChange: (rows: T[]) => void
  newRow: () => T
  /** Cabeçalhos das colunas; a 400 px cada linha vira um cartão com os rótulos. */
  headers: string[]
  renderRow: (row: T, index: number, update: (patch: Partial<T>) => void) => ReactNode[]
  /** Total ao vivo (ex.: "Soma: 100,00%"), anunciado em aria-live. */
  summary?: ReactNode
  min?: number
  max?: number
  reorder?: boolean
  disabled?: boolean
}

/**
 * Linhas repetidas (FND-09d CA 12): parcelas, turnos, dias da jornada. Cada botão diz o que faz e em qual linha; o total
 * é anunciado; a 400 px as colunas viram linhas empilhadas.
 */
export function RowsEditor<T>({ noun, rows, onChange, newRow, headers, renderRow, summary, min = 0, max = 50, reorder, disabled }: RowsEditorProps<T>) {
  const update = (index: number) => (patch: Partial<T>) => onChange(rows.map((r, i) => (i === index ? { ...r, ...patch } : r)))
  const swap = (a: number, b: number) => {
    const next = [...rows]
    ;[next[a], next[b]] = [next[b], next[a]]
    onChange(next)
  }
  return (
    <div className="space-y-2">
      <div role="table" aria-label={noun[0].toUpperCase() + noun.slice(1) + 's'} className="space-y-2">
        <div role="row" className="hidden gap-2 px-1 text-xs font-semibold text-muted-foreground sm:grid" style={{ gridTemplateColumns: `repeat(${headers.length}, minmax(0, 1fr)) auto` }}>
          {headers.map((h) => <span key={h} role="columnheader">{h}</span>)}
          <span role="columnheader" className="sr-only">Ações</span>
        </div>
        {rows.map((row, i) => {
          const cells = renderRow(row, i, update(i))
          return (
            <div key={i} role="row" aria-label={`${noun} ${i + 1}`} className="grid gap-2 rounded-md border border-border p-2 sm:items-center sm:border-0 sm:p-0" style={{ gridTemplateColumns: `repeat(${headers.length}, minmax(0, 1fr)) auto` }}>
              {cells.map((cell, c) => (
                <div key={c} role="cell" className="col-span-full min-w-0 sm:col-span-1">
                  <span className="mb-0.5 block text-xs font-medium text-muted-foreground sm:sr-only">{headers[c]}</span>
                  {cell}
                </div>
              ))}
              <div role="cell" className="col-span-full flex justify-end gap-1 sm:col-span-1">
                {reorder ? (
                  <>
                    <button type="button" disabled={disabled || i === 0} onClick={() => swap(i, i - 1)} aria-label={`Subir ${noun} ${i + 1}`} className="grid size-8 place-items-center rounded-md border border-border disabled:opacity-40"><ArrowUp aria-hidden="true" className="size-4" /></button>
                    <button type="button" disabled={disabled || i === rows.length - 1} onClick={() => swap(i, i + 1)} aria-label={`Descer ${noun} ${i + 1}`} className="grid size-8 place-items-center rounded-md border border-border disabled:opacity-40"><ArrowDown aria-hidden="true" className="size-4" /></button>
                  </>
                ) : null}
                <button type="button" disabled={disabled || rows.length <= min} onClick={() => onChange(rows.filter((_, k) => k !== i))} aria-label={`Remover ${noun} ${i + 1}`} className="grid size-8 place-items-center rounded-md border border-border disabled:opacity-40"><Trash2 aria-hidden="true" className="size-4" /></button>
              </div>
            </div>
          )
        })}
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <button type="button" disabled={disabled || rows.length >= max} onClick={() => onChange([...rows, newRow()])} className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-1.5 text-sm font-semibold disabled:opacity-50">
          <Plus aria-hidden="true" className="size-4" /> Adicionar {noun}
        </button>
        {summary !== undefined ? <p aria-live="polite" className="text-sm tabular-nums">{summary}</p> : null}
      </div>
    </div>
  )
}
