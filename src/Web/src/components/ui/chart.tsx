import type { ReactNode } from 'react'
import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { ChartFrame, type ChartFrameState, type ChartLegendItem } from './chart-frame'

/**
 * Série do gráfico (FND-09e CA 11, FIN-07 CA 12): nunca só pela cor. Realizado em traço contínuo; projetado tracejado;
 * base tracejado fino; ajustado tracejado grosso com marcador. Cada série termina com o rótulo direto.
 */
export interface ChartSeries {
  key: string
  label: string
  kind?: 'actual' | 'projected' | 'base' | 'adjusted'
  /** Token da cor (1 a 6, --chart-n); sem ele, a ordem da série. */
  color?: number
}

interface ChartProps<T extends Record<string, unknown>> {
  /** Nome do gráfico, também a legenda da tabela equivalente. */
  title: string
  data: T[]
  xKey: keyof T & string
  series: ChartSeries[]
  /** Formata o valor (ex.: dinheiro) no eixo, na dica e na tabela. */
  format?: (value: number) => string
  height?: number
  /** Cabeçalho da primeira coluna da tabela (ex.: "Dia"). */
  xLabel?: string
  /** Marcas verticais no eixo x (ex.: "Hoje" e o dia de menor saldo), com o rótulo no topo. */
  references?: { x: string; label: string }[]
  /** Marcas horizontais no eixo y (ex.: "Saldo zero", meta, capacidade). */
  yReferences?: { y: number; label: string }[]
  /** Resumo textual acima do gráfico (FIN-07 CA 11). */
  summary?: ReactNode
  state?: ChartFrameState
  onRetry?: () => void
  emptyTitle?: string
}

/** Traço de cada tipo de série. */
const SERIES_STROKE: Record<NonNullable<ChartSeries['kind']>, { dash?: string; width: number; marker: boolean }> = {
  actual: { width: 2, marker: false },
  projected: { dash: '6 4', width: 2, marker: false },
  base: { dash: '4 4', width: 1.5, marker: false },
  adjusted: { dash: '10 4', width: 3.5, marker: true },
}

const color = (s: ChartSeries, i: number) => `var(--chart-${s.color ?? (i % 6) + 1})`

/**
 * Gráfico de linhas com Recharts (FND-09e CA 11) desenhado dentro do `ChartFrame` (FIN-07 CA 11): cores só dos tokens
 * --chart-1 a --chart-6, tipo de série pelo traço, rótulo direto no último ponto, referências em x e em y e a tabela
 * equivalente, acessível por teclado, ligada ao gráfico por aria-describedby.
 */
export function Chart<T extends Record<string, unknown>>({ title, data, xKey, series, format = (v) => v.toLocaleString('pt-BR'), height = 260, xLabel = 'Período', references = [], yReferences = [], summary, state = 'ready', onRetry, emptyTitle }: ChartProps<T>) {
  const value = (v: unknown) => (typeof v === 'number' ? format(v) : '—')
  const last = Object.fromEntries(series.map((s) => [s.key, data.reduce((at, row, i) => (typeof row[s.key] === 'number' ? i : at), -1)]))
  const legend: ChartLegendItem[] = series.map((s, i) => {
    const stroke = SERIES_STROKE[s.kind ?? 'actual']
    return { key: s.key, label: s.label + (s.kind === 'projected' && !/projet/i.test(s.label) ? ' (projetado)' : ''), color: color(s, i), dash: stroke.dash, width: stroke.width, marker: stroke.marker }
  })
  const table = { xLabel, columns: series.map((s) => ({ key: s.key, label: s.label })), rows: data.map((row) => ({ x: String(row[xKey]), values: Object.fromEntries(series.map((s) => [s.key, value(row[s.key])])) })) }
  return (
    <ChartFrame title={title} summary={summary} legend={legend} table={table} state={state === 'ready' && data.length === 0 ? 'empty' : state} onRetry={onRetry} emptyTitle={emptyTitle} height={height}>
      {() => (
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 8, right: 76, bottom: 0, left: 8 }}>
            <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
            <XAxis dataKey={xKey as never} stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} />
            <YAxis stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} tickFormatter={(v: number) => format(v)} width={88} />
            <Tooltip formatter={(v) => value(v)} contentStyle={{ background: 'var(--popover)', border: '1px solid var(--border)', color: 'var(--popover-foreground)' }} />
            {references.map((r) => <ReferenceLine key={r.label} x={r.x} stroke="var(--muted-foreground)" strokeDasharray="2 2" label={{ value: r.label, position: 'insideTopLeft', fill: 'var(--muted-foreground)', fontSize: 11 }} />)}
            {yReferences.map((r) => <ReferenceLine key={r.label} y={r.y} stroke="var(--muted-foreground)" label={{ value: r.label, position: 'insideBottomLeft', fill: 'var(--muted-foreground)', fontSize: 11 }} />)}
            {series.map((s, i) => {
              const stroke = SERIES_STROKE[s.kind ?? 'actual']
              return (
                <Line key={s.key} type="monotone" dataKey={s.key} name={s.label} stroke={color(s, i)} strokeWidth={stroke.width} strokeDasharray={stroke.dash} isAnimationActive={false}
                  dot={stroke.marker ? { r: 2.5, fill: color(s, i), strokeWidth: 0 } : false}
                  label={(p: { x?: number | string; y?: number | string; index?: number }) =>
                    p.index === last[s.key] ? <text key={`${s.key}-rotulo`} x={Number(p.x) + 6} y={Number(p.y)} dy={4} fontSize={11} fontWeight={600} fill={color(s, i)} data-series-label={s.key}>{s.label}</text> : <g key={`${s.key}-${p.index}`} />} />
              )
            })}
          </LineChart>
        </ResponsiveContainer>
      )}
    </ChartFrame>
  )
}
