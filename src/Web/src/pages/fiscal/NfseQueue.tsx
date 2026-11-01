import { Link } from '@tanstack/react-router'
import { useListInstallationsToInvoice } from '@/api/generated/bemaqui'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate, formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { NFSE_TEXTS } from './nfse'
import { useStartNfse } from './use-start-nfse'

const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground disabled:opacity-60'

/** Instalações aceitas esperando a NFS-e na fila "A emitir" (CA 1): o aceite há mais de 1 dia útil em destaque, primeiro. */
export function NfseQueue() {
  const queue = useListInstallationsToInvoice()
  const { start, busy, error } = useStartNfse()
  if (queue.isPending) return <Skeleton className="h-16" />
  if (queue.isError) return <ErrorState title="Não foi possível carregar as instalações a faturar." onRetry={() => void queue.refetch()} />
  const rows = [...queue.data].sort((a, b) => Number(b.late) - Number(a.late) || a.acceptedOn.localeCompare(b.acceptedOn))
  return (
    <section aria-labelledby="nfse-fila" className="mt-6 space-y-2">
      <h2 id="nfse-fila" className="font-semibold">Instalações aceitas esperando NFS-e</h2>
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
      {rows.length === 0 ? <p className="text-sm text-muted-foreground">{NFSE_TEXTS.emptyQueue} Quando o cliente der o aceite técnico de uma instalação, ela aparece aqui.</p> : (
        <ul className="space-y-2" aria-label="Instalações esperando NFS-e">
          {rows.map((r) => (
            <li key={r.installationOrderId} className={cn('rounded-md border border-border bg-card p-3 text-sm', r.late && 'border-l-4 border-l-crit')}>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="min-w-0">
                  <p className="font-semibold">
                    <Link to={'/app/producao/instalacoes/$id' as never} params={{ id: r.installationOrderId } as never} className="text-primary tabular-nums hover:underline">{r.orderNumber}</Link>
                    <span className="text-muted-foreground"> · </span>
                    <Link to={'/app/projetos/$id' as never} params={{ id: r.projectId } as never} className="tabular-nums hover:underline">{r.projectNumber}</Link>
                  </p>
                  <p className="text-muted-foreground">{r.partyName} · aceite em <span className="tabular-nums">{formatDate(r.acceptedOn)}</span> · <span className="tabular-nums">{r.daysSinceAcceptance}</span> dias</p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  {r.late ? <StatusChip tone="crit">Atrasada</StatusChip> : null}
                  <span className="tabular-nums font-semibold">{formatMoney(r.estimatedAmount)}</span>
                  <button type="button" disabled={busy !== null} onClick={() => void start(r.installationOrderId, r.projectId)} className={primary}>{busy === r.installationOrderId ? 'Criando…' : 'Emitir NFS-e'}</button>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
