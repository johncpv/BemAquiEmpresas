import { ArrowDown, ArrowUp, Copy, GripVertical, Plus, Trash2 } from 'lucide-react'
import { type CSSProperties, type ReactNode, useRef, useState } from 'react'
import { InlineError } from '@/components/ui/states'

/** Coluna do editor (FND-09e CA 9): rótulo, largura na grade (ex.: "8rem", "2fr") e o campo da célula. */
export interface RowsColumn<T> {
  label: string
  width?: string
  render: (row: T, index: number, update: (patch: Partial<T>) => void) => ReactNode
}

interface RowsEditorProps<T> {
  /** Nome da linha no singular, usado nos rótulos ("Adicionar parcela", "Remover parcela 2"). */
  noun: string
  rows: T[]
  onChange: (rows: T[]) => void
  newRow: () => T
  /** Modelo de colunas; no lugar dele, ainda vale o par headers + renderRow (colunas de largura igual). */
  columns?: RowsColumn<T>[]
  /** Cabeçalhos das colunas; a 400 px cada linha vira um cartão com os rótulos. */
  headers?: string[]
  renderRow?: (row: T, index: number, update: (patch: Partial<T>) => void) => ReactNode[]
  /** Erro da linha inteira (ex.: o 422 de "lines[2]"), mostrado abaixo dela. */
  rowError?: (index: number) => string | undefined
  /** Total ao vivo (ex.: "Soma: 100,00%"), anunciado em aria-live. */
  summary?: ReactNode
  min?: number
  max?: number
  reorder?: boolean
  /** Com reorder: também arrastar a linha pela alça (o teclado continua com Mover para cima/baixo). */
  draggable?: boolean
  /** "Duplicar" em cada linha: a cópia entra logo abaixo, com o foco no primeiro campo (lançamento em lote, FIN-00). */
  duplicate?: boolean
  disabled?: boolean
}

let seed = 0
const newKey = () => `r${++seed}`

/**
 * Linhas repetidas (FND-09d CA 12, FND-09e CA 9): parcelas, turnos, marcos, composição de custo. Cada linha tem chave
 * estável (nunca o índice); cada botão diz o que faz e em qual linha; depois de remover, o foco vai para a linha
 * seguinte (ou para "Adicionar"); a 400 px as colunas viram um cartão empilhado.
 */
export function RowsEditor<T>({ noun, rows, onChange, newRow, columns, headers = [], renderRow, rowError, summary, min = 0, max = 50, reorder, draggable, duplicate, disabled }: RowsEditorProps<T>) {
  const [dragging, setDragging] = useState<number | null>(null)
  const [stored, setKeys] = useState<string[]>(() => rows.map(newKey))
  let keys = stored
  // Linhas trocadas de fora (reset do formulário): as chaves acompanham o tamanho, ajustadas durante o render.
  if (keys.length !== rows.length) {
    keys = keys.length > rows.length ? keys.slice(0, rows.length) : [...keys, ...rows.slice(keys.length).map(newKey)]
    setKeys(keys)
  }

  const container = useRef<HTMLDivElement>(null)
  const addRef = useRef<HTMLButtonElement>(null)

  const labels = columns ? columns.map((c) => c.label) : headers
  const template = (columns ? columns.map((c) => c.width ?? 'minmax(0, 1fr)').join(' ') : `repeat(${headers.length}, minmax(0, 1fr))`) + ' auto'
  const cellsOf = (row: T, i: number, update: (patch: Partial<T>) => void) => (columns ? columns.map((c) => c.render(row, i, update)) : (renderRow?.(row, i, update) ?? []))
  const update = (index: number) => (patch: Partial<T>) => onChange(rows.map((r, i) => (i === index ? { ...r, ...patch } : r)))

  const swap = (a: number, b: number) => {
    const next = [...rows]
    ;[next[a], next[b]] = [next[b], next[a]]
    const k = [...keys]
    ;[k[a], k[b]] = [k[b], k[a]]
    setKeys(k)
    onChange(next)
  }

  /** Leva a linha de from para to, com a chave junto (o foco e o estado da linha acompanham). */
  const move = (from: number, to: number) => {
    if (from === to) return
    const next = [...rows]
    const [row] = next.splice(from, 1)
    next.splice(to, 0, row)
    const k = [...keys]
    const [key] = k.splice(from, 1)
    k.splice(to, 0, key)
    setKeys(k)
    onChange(next)
  }

  const remove = (i: number) => {
    const target = keys[i + 1]
    setKeys(keys.filter((_, k) => k !== i))
    onChange(rows.filter((_, k) => k !== i))
    requestAnimationFrame(() => {
      const row = target ? container.current?.querySelector<HTMLElement>(`[data-row="${target}"]`) : null
      const focusable = row?.querySelector<HTMLElement>('input, select, textarea, button')
      ;(focusable ?? addRef.current)?.focus()
    })
  }

  const copy = (i: number) => {
    const key = newKey()
    setKeys([...keys.slice(0, i + 1), key, ...keys.slice(i + 1)])
    onChange([...rows.slice(0, i + 1), { ...rows[i] }, ...rows.slice(i + 1)])
    requestAnimationFrame(() => container.current?.querySelector<HTMLElement>(`[data-row="${key}"] input, [data-row="${key}"] button`)?.focus())
  }

  const add = () => {
    setKeys([...keys, newKey()])
    onChange([...rows, newRow()])
  }

  const plural = noun[0].toUpperCase() + noun.slice(1) + 's'
  const iconButton = 'grid size-8 place-items-center rounded-md border border-border disabled:opacity-40'
  return (
    <div className="space-y-2">
      <div ref={container} role="table" aria-label={plural} className="space-y-2">
        <div role="row" className="hidden gap-2 px-1 text-xs font-semibold text-muted-foreground sm:grid" style={{ gridTemplateColumns: template }}>
          {labels.map((h) => <span key={h} role="columnheader">{h}</span>)}
          <span role="columnheader" className="sr-only">Ações</span>
        </div>
        {rows.map((row, i) => {
          const cells = cellsOf(row, i, update(i))
          const error = rowError?.(i)
          return (
            <div key={keys[i]} data-row={keys[i]}
              onDragOver={draggable && dragging !== null ? (e) => e.preventDefault() : undefined}
              onDrop={draggable && dragging !== null ? (e) => { e.preventDefault(); move(dragging, i); setDragging(null) } : undefined}>
              <div role="row" aria-label={`${noun} ${i + 1}`} aria-invalid={error ? true : undefined} className="grid gap-2 rounded-md border border-border p-2 sm:grid-cols-(--cols) sm:items-center sm:border-0 sm:p-0" style={{ '--cols': template } as CSSProperties}>
                {cells.map((cell, c) => (
                  <div key={c} role="cell" className="col-span-full min-w-0 sm:col-span-1">
                    <span className="mb-0.5 block text-xs font-medium text-muted-foreground sm:sr-only">{labels[c]}</span>
                    {cell}
                  </div>
                ))}
                <div role="cell" className="col-span-full flex justify-end gap-1 sm:col-span-1">
                  {reorder && draggable && !disabled ? (
                    <span draggable onDragStart={(e) => { e.dataTransfer.effectAllowed = 'move'; setDragging(i) }} onDragEnd={() => setDragging(null)} title={`Arrastar ${noun} ${i + 1}`}
                      aria-hidden="true" className="grid size-8 cursor-grab place-items-center rounded-md text-muted-foreground"><GripVertical className="size-4" /></span>
                  ) : null}
                  {reorder ? (
                    <>
                      <button type="button" disabled={disabled || i === 0} onClick={() => swap(i, i - 1)} aria-label={`Mover ${noun} ${i + 1} para cima`} className={iconButton}><ArrowUp aria-hidden="true" className="size-4" /></button>
                      <button type="button" disabled={disabled || i === rows.length - 1} onClick={() => swap(i, i + 1)} aria-label={`Mover ${noun} ${i + 1} para baixo`} className={iconButton}><ArrowDown aria-hidden="true" className="size-4" /></button>
                    </>
                  ) : null}
                  {duplicate ? <button type="button" disabled={disabled || rows.length >= max} onClick={() => copy(i)} aria-label={`Duplicar ${noun} ${i + 1}`} className={iconButton}><Copy aria-hidden="true" className="size-4" /></button> : null}
                  <button type="button" disabled={disabled || rows.length <= min} onClick={() => remove(i)} aria-label={`Remover ${noun} ${i + 1}`} className={iconButton}><Trash2 aria-hidden="true" className="size-4" /></button>
                </div>
              </div>
              <InlineError id={`${keys[i]}-error`} message={error} />
            </div>
          )
        })}
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <button ref={addRef} type="button" disabled={disabled || rows.length >= max} onClick={add} className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-1.5 text-sm font-semibold disabled:opacity-50">
          <Plus aria-hidden="true" className="size-4" /> Adicionar {noun}
        </button>
        {summary !== undefined ? <p aria-live="polite" className="text-sm tabular-nums">{summary}</p> : null}
      </div>
    </div>
  )
}
