import { Fragment, useState } from 'react'
import type { CashFlowAdjusted, CashFlowResult } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { CASH_FLOW_DICAS, periodLabel, type Scenario } from './cash-flow'

/**
 * Tabela do fluxo (FIN-05 CA 2, 3 e 10; FIN-07 CA 7): realizado até ontem, a linha de hoje com os vencidos em aberto e o
 * projetado, com a coluna "Saldo ajustado". A 400 px, mostra período e saldos; entradas e saídas na linha expansível.
 */
export function CashFlowTable({ flow, adjusted, scenario, onOpenDay }: { flow: CashFlowResult; adjusted: CashFlowAdjusted | null; scenario: Scenario; onOpenDay: (date: string) => void }) {
  const daily = flow.view === 'daily'
  const onlyAdjusted = scenario === 'adjusted' && adjusted !== null
  const both = scenario === 'both' && adjusted !== null
  const [open, setOpen] = useState<Record<string, boolean>>({})
  const saldoColumns = (onlyAdjusted ? 0 : 1) + (adjusted && scenario !== 'base' ? 1 : 0)
  const columns = 4 + saldoColumns
  return (
    <div className="overflow-x-auto rounded-md border border-border bg-card">
      <table className="w-full text-sm" aria-label="Fluxo de caixa por período">
        <thead className="border-b border-border text-xs text-muted-foreground">
          <tr>
            <th className="px-3 py-2 text-left">Período</th>
            <th className="hidden px-3 py-2 text-left md:table-cell"><span className="inline-flex items-center">Situação<Dica nome="Realizado e projetado" texto={`${CASH_FLOW_DICAS.realizado} ${CASH_FLOW_DICAS.projetado}`} /></span></th>
            <th className="hidden px-3 py-2 text-right sm:table-cell">{onlyAdjusted ? 'Entradas ajustadas' : 'Entradas'}</th>
            <th className="hidden px-3 py-2 text-right sm:table-cell">Saídas</th>
            {onlyAdjusted ? null : <th className="px-3 py-2 text-right">{both ? 'Saldo base' : 'Saldo'}</th>}
            {adjusted && scenario !== 'base' ? <th className="px-3 py-2 text-right"><span className="inline-flex items-center justify-end">Saldo ajustado<Dica nome="Ajustado" texto={CASH_FLOW_DICAS.ajustado} /></span></th> : null}
          </tr>
        </thead>
        <tbody>
          {flow.periods.map((p, i) => {
            const a = adjusted?.periods[i]
            const shown = onlyAdjusted && a ? a : p
            const label = periodLabel(p, flow.view)
            const overdue = shown.overdueInflow > 0 || shown.overdueOutflow > 0
            const expanded = open[p.start] ?? false
            return (
              <Fragment key={p.start}>
                <tr className={p.containsToday ? 'border-y-2 border-primary bg-primary-soft' : 'border-b border-border last:border-0'}>
                  <th scope="row" className="px-3 py-1.5 text-left font-normal tabular-nums">
                    <span className="flex flex-wrap items-center gap-x-2">
                      {daily ? <button type="button" onClick={() => onOpenDay(p.start)} className="min-h-11 font-semibold text-primary hover:underline sm:min-h-0" aria-label={`Ver o dia ${label}`}>{label}</button> : label}
                      {p.containsToday ? <span className="text-xs font-semibold">Hoje</span> : null}
                      <button type="button" aria-expanded={expanded} onClick={() => setOpen((o) => ({ ...o, [p.start]: !expanded }))}
                        className="min-h-11 text-xs text-muted-foreground underline sm:hidden" aria-label={`Entradas e saídas de ${label}`}>
                        {expanded ? 'Ocultar' : 'Entradas e saídas'}
                      </button>
                    </span>
                  </th>
                  <td className="hidden px-3 py-1.5 text-xs text-muted-foreground md:table-cell">{p.containsToday ? 'Realizado e projetado' : p.projected ? 'Projetado' : 'Realizado'}</td>
                  <td className="hidden px-3 py-1.5 text-right sm:table-cell"><Money value={shown.inflow} /></td>
                  <td className="hidden px-3 py-1.5 text-right sm:table-cell"><Money value={shown.outflow} /></td>
                  {onlyAdjusted ? null : <td className="px-3 py-1.5 text-right font-semibold"><Money value={p.balance} highlightNegative /></td>}
                  {a && scenario !== 'base' ? <td className="px-3 py-1.5 text-right font-semibold"><Money value={a.balance} highlightNegative /></td> : null}
                </tr>
                {expanded ? (
                  <tr className="border-b border-border bg-muted text-xs sm:hidden">
                    <td colSpan={columns} className="px-3 py-1.5">
                      <dl className="grid grid-cols-2 gap-x-3 gap-y-0.5">
                        <dt className="text-muted-foreground">Entradas{both ? ' (base)' : ''}</dt><dd className="text-right"><Money value={shown.inflow} /></dd>
                        {both && a ? <><dt className="text-muted-foreground">Entradas (ajustado)</dt><dd className="text-right"><Money value={a.inflow} /></dd></> : null}
                        <dt className="text-muted-foreground">Saídas</dt><dd className="text-right"><Money value={shown.outflow} /></dd>
                      </dl>
                    </td>
                  </tr>
                ) : null}
                {overdue ? (
                  <tr className="border-b border-border bg-warn-soft text-warn-ink">
                    <th scope="row" className="px-3 py-1 text-left text-xs font-semibold"><span className="inline-flex items-center">Vencidos em aberto<Dica nome="Vencidos em aberto" texto={CASH_FLOW_DICAS.vencidos} /></span></th>
                    <td className="hidden md:table-cell" />
                    <td className="hidden px-3 py-1 text-right text-xs sm:table-cell"><Money value={shown.overdueInflow} /></td>
                    <td className="hidden px-3 py-1 text-right text-xs sm:table-cell"><Money value={shown.overdueOutflow} /></td>
                    <td colSpan={saldoColumns} className="px-3 py-1 text-right text-xs sm:hidden"><Money value={shown.overdueInflow - shown.overdueOutflow} /></td>
                    <td colSpan={saldoColumns} className="hidden sm:table-cell" />
                  </tr>
                ) : null}
              </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
