import { useId } from 'react'
import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { CashWeekItem } from '@/api/generated/model'
import { formatMoney } from '@/lib/format'
import { cashPoints, type Scenario } from './dashboard'

const SERIES = [
  { key: 'base', label: 'Base: vencimentos combinados', color: 'var(--chart-2)', dash: '6 4' },
  { key: 'ajustado', label: 'Ajustado: atraso histórico de cada cliente', color: 'var(--chart-1)', dash: undefined },
] as const

/**
 * Caixa projetado das 12 semanas (padrão 1 das Fases 4 a 8): série nunca só por cor (traço e rótulo), referência "Saldo zero"
 * em y, resumo textual acima (no card) e a tabela equivalente. Versão local do DSH-01a sobre o Recharts: trocar pelo
 * ChartFrame/Chart da FIN-07 depois do merge.
 */
export function CashChart({ weeks, scenario }: { weeks: readonly CashWeekItem[]; scenario: Scenario }) {
  const tableId = useId()
  const data = cashPoints(weeks, scenario)
  const series = SERIES.filter((s) => (s.key === 'base' ? scenario !== 'adjusted' : scenario !== 'base'))
  const money = (v: number) => formatMoney(v, false)
  return (
    <figure className="space-y-2">
      <div role="img" aria-label="Saldo projetado no fim de cada semana" aria-describedby={tableId} className="h-56 sm:h-64">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 16, right: 8, bottom: 0, left: 8 }}>
            <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
            <XAxis dataKey="semana" stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} />
            <YAxis stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} tickFormatter={money} width={84} />
            <Tooltip formatter={(v) => (typeof v === 'number' ? money(v) : '—')} contentStyle={{ background: 'var(--popover)', border: '1px solid var(--border)', color: 'var(--popover-foreground)' }} />
            <ReferenceLine y={0} stroke="var(--crit)" strokeDasharray="2 2" label={{ value: 'Saldo zero', position: 'insideTopLeft', fill: 'var(--muted-foreground)', fontSize: 11 }} />
            {series.map((s) => <Line key={s.key} type="monotone" dataKey={s.key} name={s.label} stroke={s.color} strokeWidth={2} strokeDasharray={s.dash} dot={false} isAnimationActive={false} />)}
          </LineChart>
        </ResponsiveContainer>
      </div>
      <figcaption className="flex flex-wrap gap-3 text-xs text-muted-foreground">
        {series.map((s) => (
          <span key={s.key} className="inline-flex items-center gap-1.5">
            <svg aria-hidden="true" width="20" height="6"><line x1="0" y1="3" x2="20" y2="3" stroke={s.color} strokeWidth="2" strokeDasharray={s.dash} /></svg>
            {s.label}
          </span>
        ))}
      </figcaption>
      <details className="text-sm">
        <summary className="inline-flex min-h-[44px] cursor-pointer items-center font-semibold text-primary sm:min-h-0">Ver os valores em tabela</summary>
        <div className="mt-2 overflow-x-auto">
          <table id={tableId} className="w-full border-collapse text-sm">
            <caption className="sr-only">Saldo projetado por semana</caption>
            <thead className="border-b border-border text-xs text-muted-foreground">
              <tr>
                <th scope="col" className="px-3 py-2 text-left">Semana até</th>
                <th scope="col" className="px-3 py-2 text-right">Base</th>
                <th scope="col" className="px-3 py-2 text-right">Ajustado</th>
              </tr>
            </thead>
            <tbody>
              {weeks.map((w, i) => (
                <tr key={w.weekEnd} className="border-b border-border last:border-0">
                  <th scope="row" className="px-3 py-1.5 text-left font-normal">{data[i].semana}</th>
                  <td className="px-3 py-1.5 text-right tabular-nums">{money(w.baseBalance)}</td>
                  <td className="px-3 py-1.5 text-right tabular-nums">{money(w.adjustedBalance)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  )
}
