import { Link } from '@tanstack/react-router'
import { useGetProjectFinancial } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { payableSituation } from '../finance/payables/payables'
import { situation } from '../finance/receivables/receivables'
import { COST_SECTIONS } from '../quotes/quotes'

/** Financeiro do projeto (COM-05 CA 6): a receber e a pagar, com totais; cada linha leva ao título. Só com finance.view. */
export function ProjectFinancialTab({ projectId }: { projectId: string }) {
  const query = useGetProjectFinancial(projectId)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o financeiro do projeto." onRetry={() => query.refetch()} />
  const f = query.data
  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Recebido</dt><dd><Money value={f.received} className="font-semibold" /></dd></div>
        <div><dt className="text-xs text-muted-foreground">A receber</dt><dd><Money value={f.toReceive} /></dd></div>
        <div><dt className="text-xs text-muted-foreground">Pago</dt><dd><Money value={f.paid} /></dd></div>
        <div><dt className="text-xs text-muted-foreground">A pagar</dt><dd><Money value={f.toPay} /></dd></div>
      </dl>
      <h3 className="text-sm font-semibold">A receber</h3>
      {f.receivables.length === 0 ? <EmptyState title="Nenhum título a receber neste projeto." /> : (
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Títulos a receber do projeto">
          {f.receivables.map((r) => {
            const s = situation({ status: r.status, isOverdue: false, isDueToday: false })
            return (
              <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span><Link to={'/app/financeiro/receber/$id' as never} params={{ id: r.id } as never} className="font-mono font-semibold text-primary hover:underline">{r.number}</Link> <span className="text-muted-foreground">{r.description}</span></span>
                <span className="flex items-center gap-2"><span className="tabular-nums">{r.dueDate ? formatDate(r.dueDate) : 'aguarda o marco'}</span><Money value={r.balance} /><StatusChip tone={s.tone}>{s.label}</StatusChip></span>
              </li>
            )
          })}
        </ul>
      )}
      <h3 className="text-sm font-semibold">A pagar</h3>
      {f.payables.length === 0 ? <EmptyState title="Nenhum título a pagar com este projeto." description="Ao lançar uma despesa, escolha o projeto e a natureza do custo." /> : (
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Títulos a pagar do projeto">
          {f.payables.map((p) => {
            const s = payableSituation({ status: p.status, isOverdue: false, isDueToday: false })
            return (
              <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span><Link to={'/app/financeiro/pagar/$id' as never} params={{ id: p.id } as never} className="font-mono font-semibold text-primary hover:underline">{p.number}</Link> <span className="text-muted-foreground">{p.partyName} · {COST_SECTIONS.find((c) => c.type === p.costType)?.label ?? 'Sem natureza'}</span></span>
                <span className="flex items-center gap-2"><span className="tabular-nums">{p.dueDate ? formatDate(p.dueDate) : '—'}</span><Money value={p.amount} /><StatusChip tone={s.tone}>{s.label}</StatusChip></span>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
