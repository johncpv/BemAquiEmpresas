import { type ReactNode, useId, useRef, useState } from 'react'
import { EmptyState, ErrorState, Skeleton } from './states'

/** Item da legenda: a amostra repete o traço da série (cor, tracejado, espessura e marcador), nunca só a cor (FIN-07 CA 12). */
export interface ChartLegendItem {
  key: string
  label: string
  color: string
  dash?: string
  width?: number
  marker?: boolean
}

/** Tabela equivalente do gráfico: a primeira coluna é o eixo x; os valores já vêm formatados. */
export interface ChartTable {
  xLabel: string
  columns: { key: string; label: string }[]
  rows: { x: string; values: Record<string, string> }[]
}

export type ChartFrameState = 'ready' | 'loading' | 'empty' | 'error'

interface ChartFrameProps {
  /** Nome do gráfico: título visível, nome da figura e legenda da tabela. */
  title: string
  /** Resumo textual acima do gráfico, montado pela tela (ex.: os dois menores saldos). */
  summary?: ReactNode
  legend: ChartLegendItem[]
  table: ChartTable
  state?: ChartFrameState
  emptyTitle?: string
  emptyDescription?: string
  emptyAction?: ReactNode
  errorTitle?: string
  onRetry?: () => void
  height?: number
  /** O desenho (Recharts ou SVG) recebe o id da tabela para o aria-describedby. */
  children: (tableId: string) => ReactNode
}

/** Amostra do traço na legenda. */
export function LegendSwatch({ color, dash, width = 2, marker }: Omit<ChartLegendItem, 'key' | 'label'>) {
  return (
    <svg aria-hidden="true" width="24" height="10" className="shrink-0">
      <line x1="0" y1="5" x2="24" y2="5" stroke={color} strokeWidth={width} strokeDasharray={dash} />
      {marker ? <circle cx="12" cy="5" r="3" fill={color} /> : null}
    </svg>
  )
}

/**
 * Moldura comum da família de gráficos (FIN-07 CA 11, D18; revisão §4.4 padrão 1): título, resumo textual acima, legenda,
 * o desenho, o botão "Ver como tabela" com a tabela equivalente navegável pelo teclado e os estados carregando, vazio e
 * erro. Sem regra de negócio e sem cor literal: as cores chegam como tokens (var(--chart-n)). O `Chart` de linhas, o
 * `ColumnChart` (DSH-02) e o Gantt (PLN-03) desenham dentro dela.
 */
export function ChartFrame({ title, summary, legend, table, state = 'ready', emptyTitle = 'Sem dados para o gráfico.', emptyDescription, emptyAction, errorTitle = 'Não foi possível montar o gráfico.', onRetry, height = 260, children }: ChartFrameProps) {
  const tableId = useId()
  const titleId = useId()
  const regionId = useId()
  const [open, setOpen] = useState(false)
  const tableRef = useRef<HTMLDivElement>(null)
  const toggle = () => {
    setOpen((o) => !o)
    // Ao abrir, o foco vai para a tabela: Tab e as setas de rolagem percorrem as linhas.
    if (!open) requestAnimationFrame(() => tableRef.current?.focus())
  }

  let body: ReactNode
  if (state === 'error') body = <ErrorState title={errorTitle} onRetry={onRetry} />
  else if (state === 'empty') body = <EmptyState title={emptyTitle} description={emptyDescription} action={emptyAction} />

  return (
    <figure aria-labelledby={titleId} className="space-y-2 rounded-md border border-border bg-card p-3">
      <figcaption className="space-y-1">
        <span id={titleId} className="block text-sm font-semibold">{title}</span>
        {summary && state === 'ready' ? <span className="block text-sm text-muted-foreground" data-testid="chart-summary">{summary}</span> : null}
      </figcaption>
      {state === 'loading' ? <div style={{ height }} className="flex"><Skeleton className="h-full w-full" /></div> : body ?? (
        <>
          <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground" aria-label="Legenda">
            {legend.map((l) => (
              <li key={l.key} className="inline-flex items-center gap-1.5">
                <LegendSwatch color={l.color} dash={l.dash} width={l.width} marker={l.marker} />
                {l.label}
              </li>
            ))}
          </ul>
          <div role="img" aria-label={title} aria-describedby={tableId} style={{ height }}>
            {children(tableId)}
          </div>
          <button type="button" aria-expanded={open} aria-controls={regionId} onClick={toggle}
            className="min-h-11 rounded-md px-1 text-sm font-semibold text-primary hover:underline sm:min-h-0">
            {open ? 'Ocultar a tabela' : 'Ver como tabela'}
          </button>
          <div ref={tableRef} id={regionId} hidden={!open} tabIndex={-1} role="region" aria-label={`${title}: tabela`} className="max-h-96 overflow-auto rounded-md border border-border focus-visible:outline-2 focus-visible:outline-ring">
            <table id={tableId} className="w-full border-collapse text-sm">
              <caption className="sr-only">{title}</caption>
              <thead className="sticky top-0 border-b border-border bg-card text-xs text-muted-foreground">
                <tr>
                  <th scope="col" className="px-3 py-2 text-left">{table.xLabel}</th>
                  {table.columns.map((c) => <th key={c.key} scope="col" className="px-3 py-2 text-right">{c.label}</th>)}
                </tr>
              </thead>
              <tbody>
                {table.rows.map((row, i) => (
                  <tr key={`${row.x}-${i}`} className="border-b border-border last:border-0">
                    <th scope="row" className="px-3 py-1.5 text-left font-normal">{row.x}</th>
                    {table.columns.map((c) => <td key={c.key} className="px-3 py-1.5 text-right tabular-nums">{row.values[c.key] ?? '—'}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </figure>
  )
}
