import { Link } from '@tanstack/react-router'
import { useGetDreChanges } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate, formatDateTime } from '@/lib/format'
import { KIND_LABELS, monthLabel } from './dre'

const TONE = { Lançado: 'info', Cancelado: 'warn', Estornado: 'warn' } as const

/** "Ver o que mudou" (CA 9): cada documento com o mês, o efeito no resultado e o link. */
export function DreChangesPanel({ since, from, to, onClose }: { since: string; from: string; to: string; onClose: () => void }) {
  const query = useGetDreChanges({ since, from, to })
  let body
  if (query.isPending) body = <Skeleton className="h-40" />
  else if (query.isError) body = <ErrorState title="Não foi possível listar o que mudou." onRetry={() => query.refetch()} />
  else if (query.data.items.length === 0) body = <EmptyState title="Nada mudou nos meses do período." />
  else {
    body = (
      <ul className="divide-y divide-border text-sm" aria-label="Documentos que mudaram">
        {query.data.items.map((c) => (
          <li key={`${c.kind}-${c.id}-${c.effect}-${c.at}`} className="flex flex-wrap items-start justify-between gap-2 py-2">
            <div className="min-w-0 space-y-0.5">
              <p className="flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                <StatusChip tone={TONE[c.effect as keyof typeof TONE] ?? 'neutral'}>{c.effect}</StatusChip>
                {KIND_LABELS[c.kind] ?? c.kind} · mês {monthLabel(c.month)} · {formatDateTime(c.at)}
              </p>
              {c.link ? <Link to={c.link as never} className="font-semibold text-primary hover:underline">{c.number ?? KIND_LABELS[c.kind]} de {formatDate(c.date)}</Link>
                : <span className="font-semibold">{c.number ?? KIND_LABELS[c.kind]} de {formatDate(c.date)}</span>}
            </div>
            {c.amount == null ? <span className="text-xs text-muted-foreground">Dados protegidos</span> : <Money value={c.amount} />}
          </li>
        ))}
      </ul>
    )
  }

  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title="O que mudou" description={`Desde a sua consulta de ${formatDateTime(since)}.`}>
      {body}
      {query.data && query.data.count > query.data.items.length ? <p className="mt-2 text-xs text-muted-foreground">Mostrando os {query.data.items.length} mais recentes de {query.data.count}.</p> : null}
    </SidePanel>
  )
}
