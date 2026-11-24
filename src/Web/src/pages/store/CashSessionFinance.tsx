import { Link } from '@tanstack/react-router'
import { useGetCashSessionFinance } from '@/api/generated/bemaqui'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { FINANCE_DICA, receivableStatusLabel } from './cash-sessions'

/** CX-03: seção "No financeiro" da sessão de caixa: os títulos (cartão, Pix, dinheiro) e as transferências que o fechamento lançou. */
export function CashSessionFinance({ sessionId }: { sessionId: string }) {
  const query = useGetCashSessionFinance(sessionId)
  const f = query.data
  return (
    <section aria-labelledby="financeiro" className="space-y-2">
      <h3 id="financeiro" className="font-semibold">No financeiro<Dica nome="No financeiro" texto={FINANCE_DICA} /></h3>
      {query.isLoading ? <Skeleton className="h-16" /> : query.isError || !f ? (
        <ErrorState title="Não foi possível carregar os lançamentos do caixa." message="Tente de novo em instantes." onRetry={() => void query.refetch()} />
      ) : f.receivables.length === 0 && f.transfers.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nada lançado ainda: o financeiro recebe o caixa quando ele fecha.</p>
      ) : (
        <ul className="divide-y divide-border rounded-md border border-border text-sm">
          {f.receivables.map((r) => (
            <li key={r.id} className="flex flex-wrap items-baseline justify-between gap-2 p-2">
              <span>
                <Link to={'/app/financeiro/receber/$id' as never} params={{ id: r.id } as never} className="font-semibold text-primary hover:underline">{r.number}</Link>
                {' '}{r.description}{r.dueDate ? <span className="text-muted-foreground"> · crédito em {formatDate(r.dueDate)}</span> : null}
                {' '}<StatusChip tone={r.status === 'Cancelled' ? 'info' : r.status === 'Paid' ? 'ok' : 'warn'}>{receivableStatusLabel(r.status)}</StatusChip>
              </span>
              <span className="tabular-nums">{r.expectedFee ? `${formatMoney(r.grossAmount ?? r.amount)} − ${formatMoney(r.expectedFee)} = ` : ''}{formatMoney(r.amount)}</span>
            </li>
          ))}
          {f.transfers.map((t) => (
            <li key={t.id} className="flex flex-wrap items-baseline justify-between gap-2 p-2">
              <span>{t.description} <span className="text-muted-foreground">({t.fromAccountName} → {t.toAccountName}, {formatDate(t.date)})</span>
                {t.reversed ? <> <StatusChip tone="info">Estornada</StatusChip></> : null}</span>
              <span className="tabular-nums">{formatMoney(t.amount)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
