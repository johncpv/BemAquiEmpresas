import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListPaymentOrders } from '@/api/generated/bemaqui'
import type { PaymentOrderSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { ORDER_SITUATIONS, ORDER_STATUS } from './payment-orders'

/** Financeiro → Ordens de pagamento (FIN-03a CA 23): nenhum pagamento sai sem ordem aprovada. */
export function PaymentOrdersPage() {
  const [search] = useTableSearch()
  const { situacao, filtro } = useTableFilters(['situacao', 'filtro'])
  const query = useListPaymentOrders({ situation: situacao || undefined, awaitingMe: filtro === 'aguardando-voce' || undefined, selfApproved: filtro === 'propria' || undefined, page: search.page, pageSize: search.pageSize })
  const awaiting = filtro === 'aguardando-voce'
  const columns: ColumnDef<PaymentOrderSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: o } }) => <Link to={'/app/financeiro/ordens-de-pagamento/$id' as never} params={{ id: o.id } as never} className="font-semibold text-primary hover:underline">{o.number}</Link> },
    { id: 'requester', header: 'Solicitante', meta: { secondary: true }, cell: ({ row: { original: o } }) => o.requesterName ?? '—' },
    { id: 'total', header: 'Total', meta: { numeric: true }, cell: ({ row: { original: o } }) => <Money value={o.total} /> },
    { id: 'expected', header: 'Data prevista', cell: ({ row: { original: o } }) => <span className="tabular-nums">{formatDate(o.expectedDate)}</span> },
    { id: 'items', header: 'Títulos', meta: { numeric: true, secondary: true }, cell: ({ row: { original: o } }) => o.items },
    { id: 'status', header: 'Situação', cell: ({ row: { original: o } }) => <span className="inline-flex flex-wrap items-center gap-1.5"><StatusChip tone={ORDER_STATUS[o.status]?.tone ?? 'neutral'}>{ORDER_STATUS[o.status]?.label ?? o.status}</StatusChip>{o.selfApproved ? <Tag>Aprovada pelo próprio solicitante</Tag> : null}</span> },
  ]
  const goPayables = <Link to={'/app/financeiro/pagar' as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ir para A pagar</Link>
  return (
    <>
      <PageHeading title="Financeiro: Ordens de pagamento">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Nenhum pagamento sai sem ordem aprovada.</p>
          {goPayables}
        </div>
      </PageHeading>
      <FinanceTabs />
      <DataTable
        label="Ordens de pagamento"
        columns={columns}
        searchable={false}
        getRowId={(o) => o.id}
        query={query}
        filters={[{ key: 'situacao', label: 'Situação', options: ORDER_SITUATIONS }, { key: 'filtro', label: 'Mostrar', options: [{ value: 'aguardando-voce', label: 'Aguardando você' }, { value: 'propria', label: 'Aprovadas pelo próprio solicitante' }] }]}
        empty={awaiting ? { title: 'Nada para aprovar agora.' } : { title: 'Selecione títulos em A pagar e crie a primeira ordem de pagamento.', action: goPayables }}
        rowClassName={(o) => (o.status === 'Pending' ? 'border-l-4 border-l-warn' : undefined)}
        expandable={{ label: (o) => `detalhes de ${o.number}`, render: (o) => <p className="text-sm sm:hidden">{o.requesterName ?? '—'} · {o.items} {o.items === 1 ? 'título' : 'títulos'}</p> }}
      />
    </>
  )
}
