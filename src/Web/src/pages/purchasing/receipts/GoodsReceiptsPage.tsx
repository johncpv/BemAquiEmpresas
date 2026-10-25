import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListGoodsReceipts, useListPurchaseOrders } from '@/api/generated/bemaqui'
import type { GoodsReceiptStatus, GoodsReceiptSummary, PurchaseOrderSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { lateLabel, ORDER_STATUS, orderDestination } from '../orders/purchase-orders'
import { RECEIPT_STATUS } from './receipts'

const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'
const STATUS_OPTIONS = (Object.keys(RECEIPT_STATUS) as GoodsReceiptStatus[]).map((value) => ({ value, label: RECEIPT_STATUS[value].label }))

/** Aba "A receber" (CA 15, padrão): pedidos aprovados com saldo, pela previsão de entrega; atraso com faixa vermelha e "Atrasado {n} d". */
function ToReceive() {
  const [search] = useTableSearch()
  const canReceive = useCan('purchasing.receive')
  const list = useListPurchaseOrders({ show: 'a-receber', q: search.q, page: search.page, pageSize: search.pageSize })
  const columns: ColumnDef<PurchaseOrderSummary>[] = [
    { id: 'number', header: 'Pedido', cell: ({ row: { original: o } }) => <Link to={'/app/compras/pedidos/$id' as never} params={{ id: o.id } as never} className="font-semibold tabular-nums text-primary hover:underline">{o.number}</Link> },
    { id: 'party', header: 'Fornecedor', cell: ({ row: { original: o } }) => o.partyName },
    { id: 'items', header: 'Itens', meta: { secondary: true }, cell: ({ row: { original: o } }) => o.itemSummary },
    {
      id: 'expected', header: 'Previsão de entrega', cell: ({ row: { original: o } }) => (
        <span className="flex flex-wrap items-center gap-1 tabular-nums">{formatDate(o.expectedDate)}{lateLabel(o.daysLate) ? <StatusChip tone="crit">{lateLabel(o.daysLate)}</StatusChip> : null}</span>
      ),
    },
    { id: 'status', header: 'Situação', meta: { secondary: true }, cell: ({ row: { original: o } }) => <StatusChip tone={ORDER_STATUS[o.status].tone}>{ORDER_STATUS[o.status].label}</StatusChip> },
    {
      id: 'action', header: 'Ação', cell: ({ row: { original: o } }) => canReceive
        ? <Link to={'/app/compras/pedidos/$id/recebimento' as never} params={{ id: o.id } as never} className="font-semibold text-primary">Registrar recebimento</Link>
        : <Link to={'/app/compras/pedidos/$id' as never} params={{ id: o.id } as never} className="font-semibold text-primary">Ver pedido</Link>,
    },
  ]
  return (
    <div className="mt-3">
      <DataTable label="Pedidos a receber" columns={columns} query={list} getRowId={(o) => o.id} searchPlaceholder="Buscar pelo número do pedido (PC-…)"
        rowClassName={(o) => (lateLabel(o.daysLate) ? 'border-l-4 border-l-crit' : undefined)}
        expandable={{ label: (o) => `detalhes de ${o.number}`, render: (o) => <p className="text-sm">{o.itemSummary} · {orderDestination(o.projectNumber, o.projectName)} · total <Money value={o.total} /></p> }}
        empty={{ title: 'Nenhum pedido a receber. Os pedidos aprovados aparecem aqui.', action: <Link to={'/app/compras/pedidos' as never} search={{ filtro: 'abertos' } as never} className={secondary}>Ver pedidos</Link> }} />
    </div>
  )
}

/** Aba "Recebidos" (CA 15): número, data, pedido, fornecedor, itens, valor e situação (Registrado ok, Estornado neutro). */
function Received() {
  const [search] = useTableSearch()
  const { situacao } = useTableFilters(['situacao'])
  const list = useListGoodsReceipts({ status: (situacao || undefined) as GoodsReceiptStatus | undefined, q: search.q, page: search.page, pageSize: search.pageSize })
  const columns: ColumnDef<GoodsReceiptSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: r } }) => <Link to={'/app/compras/recebimentos/$id' as never} params={{ id: r.id } as never} className="font-semibold tabular-nums text-primary hover:underline">{r.number}</Link> },
    { id: 'date', header: 'Data', meta: { secondary: true }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{formatDate(r.receivedOn)}</span> },
    { id: 'order', header: 'Pedido', meta: { secondary: true }, cell: ({ row: { original: r } }) => <Link to={'/app/compras/pedidos/$id' as never} params={{ id: r.purchaseOrderId } as never} className="tabular-nums text-primary hover:underline">{r.purchaseOrderNumber}</Link> },
    { id: 'party', header: 'Fornecedor', cell: ({ row: { original: r } }) => r.partyName },
    { id: 'items', header: 'Itens', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.itemSummary },
    { id: 'amount', header: 'Valor', meta: { numeric: true }, cell: ({ row: { original: r } }) => <Money value={r.amount} className="font-semibold" /> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => <StatusChip tone={RECEIPT_STATUS[r.status].tone}>{RECEIPT_STATUS[r.status].label}</StatusChip> },
  ]
  return (
    <div className="mt-3">
      <DataTable label="Recebimentos registrados" columns={columns} query={list} getRowId={(r) => r.id} searchPlaceholder="Buscar pelo número (REC-… ou PC-…)"
        filters={[{ key: 'situacao', label: 'Situação', options: STATUS_OPTIONS }]}
        expandable={{ label: (r) => `detalhes de ${r.number}`, render: (r) => <p className="text-sm">{r.itemSummary} · pedido {r.purchaseOrderNumber} · {formatDate(r.receivedOn)}</p> }}
        empty={{ title: 'Nenhum recebimento.', action: <Link to={'/app/compras/recebimentos' as never} className={secondary}>Ver pedidos a receber</Link> }} />
    </div>
  )
}

/**
 * Fábrica → Compras → Recebimentos (CMP-04 CA 15): abre em "A receber" (o que o almoxarife vai receber, pela previsão de entrega);
 * "Recebidos" lista os recebimentos registrados e estornados. A 400 px ficam o número, o fornecedor, a previsão (ou o valor) e a ação.
 */
export function GoodsReceiptsPage() {
  return (
    <>
      <PageHeading title="Compras: Recebimentos">
        <p className="mt-2 text-muted-foreground">Dê entrada no material que chegou conferindo com o pedido: o estoque, o custo médio e o título a pagar saem juntos.</p>
      </PageHeading>
      <UrlTabs label="Recebimentos" fallback="a-receber" tabs={[
        { id: 'a-receber', label: 'A receber', content: <ToReceive /> },
        { id: 'recebidos', label: 'Recebidos', content: <Received /> },
      ]} />
    </>
  )
}
