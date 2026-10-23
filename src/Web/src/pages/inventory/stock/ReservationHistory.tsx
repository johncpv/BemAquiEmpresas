import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { useGetItemReservationHistory, useGetItemStockContext, useGetOrderReservationHistory } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { CHANGE_KIND, deltaText, RESERVATION_DICAS } from './reservations'
import { formatQuantity } from './stock'

type Source = { orderId: string; itemId?: never } | { itemId: string; orderId?: never }

/**
 * Histórico da reserva (EST-02 CA 18): quanto foi reservado, liberado e completado, por qual origem ("Evento {tipo} · mensagem {id}",
 * "Entrada MOV-…", "Recomposição") e quando. Na aba Material da OS (pela OS) e na aba Reservas do item (todas as OS do item).
 */
export function ReservationHistory(source: Source) {
  const [page, setPage] = useState(1)
  const params = { page, pageSize: 10 }
  const byOrder = useGetOrderReservationHistory(source.orderId ?? '', params, { query: { enabled: !!source.orderId } })
  const byItem = useGetItemReservationHistory(source.itemId ?? '', params, { query: { enabled: !!source.itemId } })
  const query = source.orderId ? byOrder : byItem
  if (query.isPending) return <Skeleton className="h-10" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico da reserva." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) {
    return source.orderId
      ? <p className="text-sm text-muted-foreground">Nenhuma mudança na reserva ainda.</p>
      : <EmptyState title="Nenhuma reserva deste item ainda." description="Quando uma BOM liberada pedir este item, a reserva e cada mudança dela aparecem aqui." />
  }

  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div className="space-y-2">
      <ol className="space-y-2">
        {data.items.map((c) => (
          <li key={c.id} className="rounded-md border border-border bg-card px-3 py-2 text-sm">
            <p>
              <b className="font-semibold">{CHANGE_KIND[c.kind]}</b> <span className="tabular-nums">{deltaText(c.quantityDelta)} {c.unitCode}</span> de <span className="font-mono">{c.itemCode}</span> em {c.locationCode}
              {source.itemId ? <> para <Link to={'/app/producao/os/$id' as never} params={{ id: c.productionOrderId } as never} search={{ aba: 'material' } as never} className="font-semibold text-primary hover:underline">{c.orderNumber ?? 'OS'}</Link></> : null}
              <span className="text-muted-foreground"> · reservado {formatQuantity(c.quantityAfter)}, falta {formatQuantity(c.shortageAfter)}</span>
            </p>
            <p className="text-muted-foreground">{c.origin}{c.isBackfill ? ' · Calculado na implantação' : ''} · <time className="tabular-nums" dateTime={c.at}>{formatDateTime(c.at)}</time></p>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="flex items-center gap-2 text-sm">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Anterior</button>
          <span className="tabular-nums">{page} de {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Próxima</button>
        </div>
      ) : null}
    </div>
  )
}

/** Aba Reservas do item (EST-02 CA 13 e 18): físico, reservado e disponível somados nos locais e o histórico da reserva. */
export function ItemReservations({ itemId }: { itemId: string }) {
  const context = useGetItemStockContext(itemId)
  const positions = context.data?.positions ?? []
  const reserved = positions.reduce((s, p) => s + p.reserved, 0)
  const available = positions.reduce((s, p) => s + p.available, 0)
  return (
    <div className="mt-3 space-y-3">
      {context.data ? (
        <p className="flex flex-wrap items-center gap-x-1 text-sm tabular-nums">
          Físico <b>{formatQuantity(context.data.totalOnHand)} {context.data.unitCode}</b> ·
          <span className="inline-flex items-center">Reservado<Dica nome="Reservado" texto={RESERVATION_DICAS.reservado} /></span> <b>{formatQuantity(reserved)}</b> ·
          <span className="inline-flex items-center">Disponível<Dica nome="Disponível" texto={RESERVATION_DICAS.disponivel} /></span> <b>{formatQuantity(available)}</b>
          {reserved > 0 ? <Link to={'/app/estoque/faltas' as never} search={{ q: context.data.code } as never} className="ml-2 font-semibold text-primary">Ver faltas do item</Link> : null}
        </p>
      ) : null}
      <ReservationHistory itemId={itemId} />
    </div>
  )
}
