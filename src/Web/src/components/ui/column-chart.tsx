import { useId } from 'react'
import { Bar, BarChart, CartesianGrid, LabelList, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'

/** Série de colunas: `outline` (vazada, só contorno, ex.: orçado) ou `solid` (cheia, ex.: projetado). Nunca só pela cor (padrão 1). */
export interface ColumnSeries {
  key: string
  label: string
  fill?: 'outline' | 'solid'
}

interface ColumnChartProps<T extends Record<string, unknown>> {
  /** Nome do gráfico, também a legenda da tabela equivalente. */
  title: string
  data: T[]
  xKey: keyof T & string
  series: ColumnSeries[]
  /** Resumo textual acima do gráfico ("A natureza que mais desviou foi …"). */
  summary?: string
  format?: (value: number) => string
  /** Formato curto do rótulo direto em cada coluna (padrão: o mesmo de `format`). */
  labelFormat?: (value: number) => string
  height?: number
  /** Cabeçalho da primeira coluna da tabela (ex.: "Natureza"). */
  xLabel?: string
  /** Referências em y além do zero (ex.: "capacidade" na PLN-04, "meta" na DSH-01b). */
  references?: { y: number; label: string }[]
}

const COLORS = [1, 2, 3, 4, 5, 6].map((n) => `var(--chart-${n})`)

/**
 * Gráfico de colunas (DSH-02 CA 17, D18): séries por preenchimento (vazada com contorno × cheia) e rótulo direto do valor em cada coluna,
 * referência em y "zero" (e outras, como capacidade ou meta), resumo textual acima e tabela equivalente ligada por aria-describedby. Cores
 * só dos tokens --chart-*. Depois do merge da FIN-07, passa a ficar dentro do ChartFrame.
 */
export function ColumnChart<T extends Record<string, unknown>>({ title, data, xKey, series, summary, format = (v) => v.toLocaleString('pt-BR'), labelFormat, height = 280, xLabel = 'Categoria', references = [] }: ColumnChartProps<T>) {
  const tableId = useId()
  const value = (v: unknown) => (typeof v === 'number' ? format(v) : '—')
  const short = labelFormat ?? format
  return (
    <figure className="space-y-2">
      {summary ? <p className="text-sm">{summary}</p> : null}
      <div role="img" aria-label={title} aria-describedby={tableId} style={{ height }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data} margin={{ top: 20, right: 8, bottom: 0, left: 8 }}>
            <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" vertical={false} />
            <XAxis dataKey={xKey as never} stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} interval={0} />
            <YAxis stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} tickFormatter={(v: number) => short(v)} width={72} />
            <Tooltip formatter={(v) => value(v)} cursor={{ fill: 'var(--muted)' }} contentStyle={{ background: 'var(--popover)', border: '1px solid var(--border)', color: 'var(--popover-foreground)' }} />
            <ReferenceLine y={0} stroke="var(--muted-foreground)" label={{ value: 'zero', position: 'insideBottomLeft', fill: 'var(--muted-foreground)', fontSize: 11 }} />
            {references.map((r) => <ReferenceLine key={r.label} y={r.y} stroke="var(--muted-foreground)" strokeDasharray="4 3" label={{ value: r.label, position: 'insideTopRight', fill: 'var(--muted-foreground)', fontSize: 11 }} />)}
            {series.map((s, i) => {
              const color = COLORS[i % COLORS.length]
              const outline = s.fill === 'outline'
              return (
                <Bar key={s.key} dataKey={s.key} name={s.label} fill={outline ? 'var(--card)' : color} stroke={color} strokeWidth={outline ? 2 : 0} isAnimationActive={false}>
                  <LabelList dataKey={s.key} position="top" formatter={(v: unknown) => (typeof v === 'number' ? short(v) : '')} fill="var(--foreground)" fontSize={11} />
                </Bar>
              )
            })}
          </BarChart>
        </ResponsiveContainer>
      </div>
      <figcaption className="flex flex-wrap gap-3 text-xs text-muted-foreground">
        {series.map((s, i) => (
          <span key={s.key} className="inline-flex items-center gap-1.5" data-series={s.key}>
            <svg aria-hidden="true" width="14" height="10">
              <rect x="1" y="1" width="12" height="8" fill={s.fill === 'outline' ? 'none' : COLORS[i % COLORS.length]} stroke={COLORS[i % COLORS.length]} strokeWidth="2" />
            </svg>
            {s.label}{s.fill === 'outline' ? ' (coluna vazada)' : ' (coluna cheia)'}
          </span>
        ))}
      </figcaption>
      <details className="text-sm">
        <summary className="cursor-pointer font-semibold text-primary">Ver os valores em tabela</summary>
        <div className="mt-2 overflow-x-auto">
          <table id={tableId} className="w-full border-collapse text-sm">
            <caption className="sr-only">{title}</caption>
            <thead className="border-b border-border text-xs text-muted-foreground">
              <tr>
                <th scope="col" className="px-3 py-2 text-left">{xLabel}</th>
                {series.map((s) => <th key={s.key} scope="col" className="px-3 py-2 text-right">{s.label}</th>)}
              </tr>
            </thead>
            <tbody>
              {data.map((row, i) => (
                <tr key={i} className="border-b border-border last:border-0">
                  <th scope="row" className="px-3 py-1.5 text-left font-normal">{String(row[xKey])}</th>
                  {series.map((s) => <td key={s.key} className="px-3 py-1.5 text-right tabular-nums">{value(row[s.key])}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  )
}
