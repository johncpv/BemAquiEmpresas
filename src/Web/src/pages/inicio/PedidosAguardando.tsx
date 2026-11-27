import { Link } from '@tanstack/react-router'
import { ChevronRight } from 'lucide-react'
import { useGetSalesOrderSummary } from '@/api/generated/bemaqui'
import { useNavigation } from '@/navigation/navigation-context'
import { canIn } from '../use-can'

/**
 * Card "Pedidos aguardando ação" do Início (VND-02a): para confirmar, para cobrar, aguardando pagamento e com problema, cada um levando ao grupo da
 * lista. Some quando não há pedido parado nem acesso a Vendas (a fábrica sem a exceção não faz a chamada).
 */
export function PedidosAguardando() {
  const { all } = useNavigation()
  const enabled = canIn(all, 'sales.view')
  const query = useGetSalesOrderSummary({ query: { enabled, retry: false } })
  if (!enabled || !query.data) return null
  const s = query.data
  const items = [
    { label: 'Para confirmar', count: s.toConfirm, passo: 'Confirm' },
    { label: 'Para cobrar', count: s.toCharge, passo: 'Charge' },
    { label: 'Aguardando pagamento', count: s.awaitingPayment, passo: 'AwaitPayment' },
  ]
  if (items.every((i) => i.count === 0) && s.withProblem === 0) return null
  return (
    <section aria-labelledby="pedidos-aguardando" className="mb-6 rounded-md border border-border bg-card p-4">
      <h2 id="pedidos-aguardando" className="mb-2 font-semibold">
        <Link to={'/app/loja/pedidos' as never} className="inline-flex items-center gap-1 hover:text-primary">Pedidos aguardando ação<ChevronRight aria-hidden="true" className="size-4 text-muted-foreground" /></Link>
      </h2>
      <ul className="flex flex-wrap gap-2 text-sm">
        {items.filter((i) => i.count > 0).map((i) => (
          <li key={i.passo}>
            <Link to={'/app/loja/pedidos' as never} search={{ passo: i.passo } as never} className="inline-flex min-h-11 items-center gap-2 rounded-md border border-border px-3 hover:border-primary">
              {i.label}<b className="tabular-nums">{i.count}</b>
            </Link>
          </li>
        ))}
        {s.withProblem > 0 ? <li className="inline-flex min-h-11 items-center rounded-md border border-crit-soft bg-crit-soft px-3 text-crit-ink">Com problema <b className="ml-2 tabular-nums">{s.withProblem}</b></li> : null}
      </ul>
    </section>
  )
}
