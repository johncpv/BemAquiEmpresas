import { Link } from '@tanstack/react-router'
import { useGetCashFlowDay } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDate } from '@/lib/format'
import { KIND_LABELS } from './cash-flow'

/** O que compõe um dia (FIN-05 CA 10): transações, títulos e recorrentes, cada um com o link do documento. */
export function CashFlowDayPanel({ date, bankAccountId, onClose }: { date: string; bankAccountId?: string; onClose: () => void }) {
  const query = useGetCashFlowDay({ date, bankAccountId })
  let body
  if (query.isPending) body = <Skeleton className="h-40" />
  else if (query.isError) body = <ErrorState title="Não foi possível carregar o dia." onRetry={() => query.refetch()} />
  else if (query.data.items.length === 0) body = <EmptyState title="Nada entra nem sai neste dia." />
  else {
    body = (
      <ul className="divide-y divide-border text-sm" aria-label={`Movimentos de ${formatDate(date)}`}>
        {query.data.items.map((i) => (
          <li key={`${i.kind}-${i.id}-${i.dueDate ?? ''}`} className="flex flex-wrap items-start justify-between gap-2 py-2">
            <div className="min-w-0 space-y-0.5">
              <p className="flex flex-wrap items-center gap-1.5">
                <span className="text-xs text-muted-foreground">{KIND_LABELS[i.kind] ?? i.kind}</span>
                {i.overdue ? <StatusChip tone="crit">Vencido</StatusChip> : null}
                {i.estimate ? <Tag>Estimado</Tag> : null}
              </p>
              {i.link ? <Link to={i.link as never} className="font-semibold text-primary hover:underline">{i.number ? `${i.number} — ` : ''}{i.description}</Link> : <span className="font-semibold">{i.description}</span>}
              {i.partyName ? <p className="text-xs text-muted-foreground">{i.partyName}{i.overdue && i.dueDate ? ` · venceu em ${formatDate(i.dueDate)}` : ''}</p> : null}
            </div>
            <Money value={i.amount} className={i.amount < 0 ? 'text-crit-ink' : 'text-ok-ink'} />
          </li>
        ))}
      </ul>
    )
  }

  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={`Dia ${formatDate(date)}`} description={query.data?.projected ? 'Previsto: títulos e recorrentes do dia.' : 'Realizado: transações do dia.'}>
      {body}
    </SidePanel>
  )
}
