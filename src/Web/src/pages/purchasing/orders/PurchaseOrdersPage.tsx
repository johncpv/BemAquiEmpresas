import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListPurchaseOrders, useListPurchaseProjectOptions } from '@/api/generated/bemaqui'
import type { PurchaseOrderStatus, PurchaseOrderSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { lateLabel, ORDER_STATUS, orderDestination, SHOW_OPTIONS, STATUS_OPTIONS } from './purchase-orders'

const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/**
 * Fábrica → Compras → Pedidos (CMP-03a CA 20): número, fornecedor, destino, total, previsão de entrega e situação. Para quem aprova,
 * o padrão é "Aguardando você" (só o que pode decidir agora); atraso de entrega com faixa vermelha e "Atrasado {n} d". A 400 px ficam
 * número, fornecedor, total e situação.
 */
export function PurchaseOrdersPage() {
  const [search] = useTableSearch()
  const canApprove = useCan('purchasing.approve')
  const { filtro, situacao, projeto } = useTableFilters(['filtro', 'situacao', 'projeto'])
  const show = filtro ?? (canApprove ? 'aguardando-voce' : 'abertos')
  const projects = useListPurchaseProjectOptions()
  const list = useListPurchaseOrders({
    show, status: (situacao || undefined) as PurchaseOrderStatus | undefined, projectId: projeto || undefined, q: search.q, page: search.page, pageSize: search.pageSize,
  })

  const columns: ColumnDef<PurchaseOrderSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: o } }) => <Link to={'/app/compras/pedidos/$id' as never} params={{ id: o.id } as never} className="font-semibold tabular-nums text-primary hover:underline">{o.number}</Link> },
    { id: 'party', header: 'Fornecedor', cell: ({ row: { original: o } }) => <span>{o.partyName}</span> },
    { id: 'destination', header: 'Destino', meta: { secondary: true }, cell: ({ row: { original: o } }) => orderDestination(o.projectNumber, o.projectName) },
    { id: 'total', header: 'Total', meta: { numeric: true }, cell: ({ row: { original: o } }) => <Money value={o.total} className="font-semibold" /> },
    {
      id: 'expected', header: 'Previsão de entrega', meta: { secondary: true }, cell: ({ row: { original: o } }) => (
        <span className="flex flex-wrap items-center gap-1 tabular-nums">{formatDate(o.expectedDate)}{lateLabel(o.daysLate) ? <StatusChip tone="crit">{lateLabel(o.daysLate)}</StatusChip> : null}</span>
      ),
    },
    { id: 'status', header: 'Situação', cell: ({ row: { original: o } }) => <StatusChip tone={ORDER_STATUS[o.status].tone}>{ORDER_STATUS[o.status].label}</StatusChip> },
  ]

  const create = <Can perm="purchasing.create"><Link to={'/app/compras/pedidos/novo' as never} className={primary}>Novo pedido</Link></Can>
  const requests = <Link to={'/app/compras/solicitacoes' as never} className={secondary}>Ver solicitações</Link>
  const showLabel = SHOW_OPTIONS.find((o) => o.value === show)?.label ?? 'Todos'
  return (
    <>
      <PageHeading title="Compras: Pedidos">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Pedidos aos fornecedores, da aprovação ao recebimento. Aprovado, o pagamento previsto entra no fluxo de caixa.</p>
          <span className="flex flex-wrap gap-2">{requests}{create}</span>
        </div>
      </PageHeading>
      <p className="mb-3 text-sm text-muted-foreground">Mostrando: {showLabel}</p>
      <DataTable label="Pedidos de compra" columns={columns} query={list} getRowId={(o) => o.id} searchPlaceholder="Buscar pelo número (PC-…)"
        filters={[
          { key: 'filtro', label: 'Mostrar', options: SHOW_OPTIONS },
          { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS },
          { key: 'projeto', label: 'Projeto', options: (projects.data ?? []).map((p) => ({ value: p.id, label: `${p.number} — ${p.name}` })) },
        ]}
        rowClassName={(o) => (lateLabel(o.daysLate) ? 'border-l-4 border-l-crit' : undefined)}
        expandable={{
          label: (o) => `detalhes de ${o.number}`,
          render: (o) => <p className="text-sm">{o.itemSummary} · {orderDestination(o.projectNumber, o.projectName)} · previsão {formatDate(o.expectedDate)}{lateLabel(o.daysLate) ? ` · ${lateLabel(o.daysLate)}` : ''}</p>,
        }}
        empty={{ title: 'Nenhum pedido.', action: <span className="flex flex-wrap justify-center gap-2">{create}{requests}</span> }} />
    </>
  )
}
