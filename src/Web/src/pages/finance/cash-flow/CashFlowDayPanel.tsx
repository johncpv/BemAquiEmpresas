import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { useGetCashFlowDay } from '@/api/generated/bemaqui'
import type { CashFlowItem } from '@/api/generated/model'
import { ToggleGroup } from '@/components/form/choice'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDate } from '@/lib/format'
import { KIND_LABELS } from './cash-flow'

type DayScenario = 'base' | 'adjusted'

function ItemRow({ item, adjusted }: { item: CashFlowItem; adjusted: boolean }) {
  const moved = adjusted && item.kind === 'Receivable' && item.adjustedDate && item.dueDate && item.adjustedDate !== item.dueDate
  return (
    <li className="flex flex-wrap items-start justify-between gap-2 py-2">
      <div className="min-w-0 space-y-0.5">
        <p className="flex flex-wrap items-center gap-1.5">
          <span className="text-xs text-muted-foreground">{KIND_LABELS[item.kind] ?? item.kind}</span>
          {item.overdue ? <StatusChip tone="crit">Vencido</StatusChip> : null}
          {item.estimate ? <Tag>Estimado</Tag> : null}
          {item.originalAmount != null && !item.shiftedOut ? <StatusChip tone="warn">Ponderado</StatusChip> : null}
        </p>
        {item.link ? <Link to={item.link as never} className="font-semibold text-primary hover:underline">{item.number ? `${item.number} — ` : ''}{item.description}</Link> : <span className="font-semibold">{item.description}</span>}
        {item.partyName ? <p className="text-xs text-muted-foreground">{item.partyName}{item.overdue && item.dueDate && !moved ? ` · venceu em ${formatDate(item.dueDate)}` : ''}</p> : null}
        {moved ? <p className="text-xs text-muted-foreground">Vencimento {formatDate(item.dueDate!)} · data ajustada {formatDate(item.adjustedDate!)}</p> : null}
        {adjusted && item.reason ? <p className="text-xs">{item.reason}</p> : null}
        {item.originalAmount != null && !item.shiftedOut ? <p className="text-xs text-muted-foreground">Valor do título: <Money value={item.originalAmount} /></p> : null}
      </div>
      <Money value={item.amount} className={item.shiftedOut ? 'text-muted-foreground line-through' : item.amount < 0 ? 'text-crit-ink' : 'text-ok-ink'} />
    </li>
  )
}

/**
 * O que compõe um dia (FIN-05 CA 10; FIN-07 CA 9): transações, títulos e recorrentes, cada um com o link do documento. No
 * ajustado, cada recebível traz o vencimento original, a data ajustada e o motivo; os que saíram do dia por deslocamento
 * aparecem à parte, fora do total. O total é o da célula.
 */
export function CashFlowDayPanel({ date, bankAccountId, scenario, canAdjust, onClose }: { date: string; bankAccountId?: string; scenario: DayScenario; canAdjust: boolean; onClose: () => void }) {
  const [current, setCurrent] = useState<DayScenario>(canAdjust ? scenario : 'base')
  const query = useGetCashFlowDay({ date, bankAccountId, scenario: current })
  const adjusted = current === 'adjusted'
  let body
  if (query.isPending) body = <Skeleton className="h-40" />
  else if (query.isError) body = <ErrorState title="Não foi possível carregar o dia." onRetry={() => query.refetch()} />
  else if (query.data.items.length === 0) body = <EmptyState title="Nada entra nem sai neste dia." />
  else {
    const kept = query.data.items.filter((i) => !i.shiftedOut)
    const shifted = query.data.items.filter((i) => i.shiftedOut)
    body = (
      <div className="space-y-4">
        {kept.length > 0 ? (
          <ul className="divide-y divide-border text-sm" aria-label={`Movimentos de ${formatDate(date)}`}>
            {kept.map((i) => <ItemRow key={`${i.kind}-${i.id}-${i.dueDate ?? ''}`} item={i} adjusted={adjusted} />)}
          </ul>
        ) : <p className="text-sm text-muted-foreground">Nada entra nem sai neste dia no ajustado.</p>}
        {query.data.total != null ? (
          <p className="flex justify-between border-t border-border pt-2 text-sm font-semibold">Total do dia<Money value={query.data.total} highlightNegative /></p>
        ) : null}
        {shifted.length > 0 ? (
          <section aria-label="Saíram deste dia" className="space-y-1">
            <h3 className="text-sm font-semibold">Saíram deste dia pelo atraso de sempre</h3>
            <p className="text-xs text-muted-foreground">Venciam aqui e, no ajustado, entram em outro dia. Não somam no total.</p>
            <ul className="divide-y divide-border text-sm">
              {shifted.map((i) => <ItemRow key={`saiu-${i.id}`} item={i} adjusted />)}
            </ul>
          </section>
        ) : null}
      </div>
    )
  }

  const description = !query.data?.projected ? 'Realizado: transações do dia.' : adjusted ? 'Ajustado: recebimentos com o atraso de sempre de cada cliente.' : 'Previsto: títulos e recorrentes do dia.'
  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={`Dia ${formatDate(date)}`} description={description}>
      {canAdjust ? (
        <div className="mb-3">
          <ToggleGroup label="Cenário do dia" value={current} onChange={setCurrent} options={[{ value: 'base', label: 'Base' }, { value: 'adjusted', label: 'Ajustado' }]} />
        </div>
      ) : null}
      {body}
    </SidePanel>
  )
}
