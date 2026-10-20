import { useId } from 'react'
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'

/** Série do gráfico: realizado em traço contínuo, projetado em tracejado (FND-09e CA 11, padrão 10 da Fase 2). */
export interface ChartSeries {
  key: string
  label: string
  kind?: 'actual' | 'projected'
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
}

const COLORS = [1, 2, 3, 4, 5, 6].map((n) => `var(--chart-${n})`)

/**
 * Gráfico de linhas com Recharts (FND-09e CA 11): cores só dos tokens --chart-1 a --chart-6, realizado × projetado pelo
 * traço, e a tabela equivalente, acessível por teclado, ligada ao gráfico por aria-describedby.
 */
export function Chart<T extends Record<string, unknown>>({ title, data, xKey, series, format = (v) => v.toLocaleString('pt-BR'), height = 260, xLabel = 'Período' }: ChartProps<T>) {
  const tableId = useId()
  const value = (v: unknown) => (typeof v === 'number' ? format(v) : '—')
  return (
    <figure className="space-y-2">
      <div role="img" aria-label={title} aria-describedby={tableId} style={{ height }}>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: 8 }}>
            <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
            <XAxis dataKey={xKey as never} stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} />
            <YAxis stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} tickFormatter={(v: number) => format(v)} width={88} />
            <Tooltip formatter={(v) => value(v)} contentStyle={{ background: 'var(--popover)', border: '1px solid var(--border)', color: 'var(--popover-foreground)' }} />
            {series.map((s, i) => (
              <Line key={s.key} type="monotone" dataKey={s.key} name={s.label} stroke={COLORS[i % COLORS.length]} strokeWidth={2} dot={false} strokeDasharray={s.kind === 'projected' ? '6 4' : undefined} isAnimationActive={false} />
            ))}
          </LineChart>
        </ResponsiveContainer>
      </div>
      <figcaption className="flex flex-wrap gap-3 text-xs text-muted-foreground">
        {series.map((s, i) => (
          <span key={s.key} className="inline-flex items-center gap-1.5">
            <svg aria-hidden="true" width="20" height="6"><line x1="0" y1="3" x2="20" y2="3" stroke={COLORS[i % COLORS.length]} strokeWidth="2" strokeDasharray={s.kind === 'projected' ? '4 3' : undefined} /></svg>
            {s.label}{s.kind === 'projected' && !/projet/i.test(s.label) ? ' (projetado)' : ''}
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
