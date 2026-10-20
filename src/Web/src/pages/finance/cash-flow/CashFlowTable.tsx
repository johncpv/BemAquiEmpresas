import { Fragment } from 'react'
import type { CashFlowResult } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { CASH_FLOW_DICAS, periodLabel } from './cash-flow'

/** Tabela do fluxo (CA 2, 3 e 10): realizado até ontem, a linha de hoje com os vencidos em aberto e o projetado. */
export function CashFlowTable({ flow, onOpenDay }: { flow: CashFlowResult; onOpenDay: (date: string) => void }) {
  const daily = flow.view === 'daily'
  return (
    <div className="overflow-x-auto rounded-md border border-border bg-card">
      <table className="w-full text-sm" aria-label="Fluxo de caixa por período">
        <thead className="border-b border-border text-xs text-muted-foreground">
          <tr>
            <th className="px-3 py-2 text-left">Período</th>
            <th className="hidden px-3 py-2 text-left sm:table-cell"><span className="inline-flex items-center">Situação<Dica nome="Realizado e projetado" texto={`${CASH_FLOW_DICAS.realizado} ${CASH_FLOW_DICAS.projetado}`} /></span></th>
            <th className="px-3 py-2 text-right">Entradas</th>
            <th className="px-3 py-2 text-right">Saídas</th>
            <th className="px-3 py-2 text-right">Saldo</th>
          </tr>
        </thead>
        <tbody>
          {flow.periods.map((p) => {
            const label = periodLabel(p, flow.view)
            const overdue = p.overdueInflow > 0 || p.overdueOutflow > 0
            return (
              <Fragment key={p.start}>
                <tr className={p.containsToday ? 'border-y-2 border-primary bg-primary-soft' : 'border-b border-border last:border-0'}>
                  <th scope="row" className="px-3 py-1.5 text-left font-normal tabular-nums">
                    {daily ? <button type="button" onClick={() => onOpenDay(p.start)} className="font-semibold text-primary hover:underline" aria-label={`Ver o dia ${label}`}>{label}</button> : label}
                    {p.containsToday ? <span className="ml-2 text-xs font-semibold">Hoje</span> : null}
                  </th>
                  <td className="hidden px-3 py-1.5 text-xs text-muted-foreground sm:table-cell">{p.containsToday ? 'Realizado e projetado' : p.projected ? 'Projetado' : 'Realizado'}</td>
                  <td className="px-3 py-1.5 text-right"><Money value={p.inflow} /></td>
                  <td className="px-3 py-1.5 text-right"><Money value={p.outflow} /></td>
                  <td className="px-3 py-1.5 text-right font-semibold"><Money value={p.balance} highlightNegative /></td>
                </tr>
                {overdue ? (
                  <tr className="border-b border-border bg-warn-soft text-warn-ink">
                    <th scope="row" className="px-3 py-1 text-left text-xs font-semibold"><span className="inline-flex items-center">Vencidos em aberto<Dica nome="Vencidos em aberto" texto={CASH_FLOW_DICAS.vencidos} /></span></th>
                    <td className="hidden sm:table-cell" />
                    <td className="px-3 py-1 text-right text-xs"><Money value={p.overdueInflow} /></td>
                    <td className="px-3 py-1 text-right text-xs"><Money value={p.overdueOutflow} /></td>
                    <td />
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
