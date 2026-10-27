import { useGetPaymentOrderPayeeAccounts } from '@/api/generated/bemaqui'
import type { PaymentOrderDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Skeleton } from '@/components/ui/states'

/**
 * "Para onde vai o dinheiro" (RH-02c CA 10, padrão 8): a conta de salário lida do RH só agora, ao abrir o "Pagar", nunca gravada no
 * Financeiro. Se a conta mudou depois do envio, a OP volta para nova aprovação.
 */
export function PayeeAccounts({ order, onResubmit }: { order: PaymentOrderDetail; onResubmit: () => void }) {
  const hasSalaries = order.items.some((i) => i.payeeType === 'Employee')
  const query = useGetPaymentOrderPayeeAccounts(order.summary.id, { query: { enabled: hasSalaries, staleTime: 0, gcTime: 0, retry: false } })
  if (!hasSalaries) return null
  if (query.isPending) return <Skeleton className="mt-3 h-16" />
  if (query.isError) {
    const changed = query.error instanceof ApiError && query.error.code === 'payee_account_changed'
    return (
      <div role="alert" className="mt-3 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
        <p>{query.error instanceof ApiError ? query.error.message : 'Não foi possível ler a conta do funcionário.'}</p>
        {changed ? <button type="button" onClick={onResubmit} className="mt-2 min-h-11 rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground">Reenviar para aprovação</button> : null}
      </div>
    )
  }

  return (
    <section aria-label="Para onde vai o dinheiro" className="mt-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">
      <h3 className="font-semibold">Para onde vai o dinheiro</h3>
      <p className="text-xs text-muted-foreground">Conta lida do RH agora, só para quem paga. Não fica gravada no Financeiro.</p>
      <ul className="mt-2 space-y-2">
        {(query.data ?? []).map((a) => (
          <li key={a.itemId} className="tabular-nums">
            <b>{a.payableNumber}</b> — {a.registration ? `${a.registration} — ` : ''}{a.employeeName}
            <span className="block">
              {a.bankCode ? `Banco ${a.bankCode}${a.bankName ? ` ${a.bankName}` : ''} · agência ${a.agency ?? '—'} · conta ${a.account ?? '—'}` : null}
              {a.pixKey ? ` · Pix ${a.pixKey}` : null}
            </span>
            <span className="block text-xs text-muted-foreground">Titular {a.holderName ?? '—'}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}
