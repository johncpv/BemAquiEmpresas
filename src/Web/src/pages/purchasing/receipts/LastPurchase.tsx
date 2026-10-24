import { Link } from '@tanstack/react-router'
import { useGetItemLastPurchase } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { RECEIPT_DICAS } from './receipts'

/** "Última compra" do item (CMP-04 CA 15): o preço e o fornecedor do último pedido aprovado, para quem vê Compras. */
export function LastPurchase({ itemId }: { itemId: string }) {
  const canSee = useCan('purchasing.view')
  const query = useGetItemLastPurchase(itemId, { query: { enabled: canSee, retry: false } })
  if (!canSee || query.isPending) return null
  if (query.isError) {
    return query.error instanceof ApiError && query.error.status === 404
      ? <p className="mb-3 inline-flex items-center text-sm text-muted-foreground">Última compra: ainda não comprado.<Dica nome="Última compra" texto={RECEIPT_DICAS.ultimaCompra} /></p>
      : null
  }

  const last = query.data
  return (
    <p className="mb-3 inline-flex flex-wrap items-center gap-1 text-sm">
      <span className="text-muted-foreground">Última compra:</span> <Money value={last.unitPrice} className="font-semibold" /> de {last.partyName} em {formatDate(last.approvedAt.slice(0, 10))}
      (<Link to={'/app/compras/pedidos/$id' as never} params={{ id: last.purchaseOrderId } as never} className="font-semibold text-primary">{last.purchaseOrderNumber}</Link>)
      <Dica nome="Última compra" texto={RECEIPT_DICAS.ultimaCompra} />
    </p>
  )
}
