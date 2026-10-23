import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListStockShortages, useSearchStockProjects } from '@/api/generated/bemaqui'
import type { ShortageRow } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { RESERVATION_DICAS, shortageLabel } from './reservations'
import { formatQuantity } from './stock'

/** As OS de um item com falta, com o link para a aba Material de cada uma. */
function OrderLinks({ row }: { row: ShortageRow }) {
  return (
    <ul className="space-y-0.5">
      {row.orders.map((o) => (
        <li key={o.productionOrderId} className="tabular-nums">
          <Link to={'/app/producao/os/$id' as never} params={{ id: o.productionOrderId } as never} search={{ aba: 'material' } as never} className="font-semibold text-primary hover:underline">{o.orderNumber ?? 'OS'}</Link>
          <span className="text-muted-foreground"> · {o.projectNumber ?? 'projeto'} · falta {formatQuantity(o.shortQty)}</span>
          {o.isBackfill ? <Tag className="ml-1">Calculado na implantação</Tag> : null}
        </li>
      ))}
    </ul>
  )
}

/**
 * Estoque → Faltas (EST-02 CA 15): por item, a falta somada das OS abertas, as OS e projetos afetados, a data prometida mais próxima e
 * o caminho para a solicitação de compra (CMP-01). Filtros na URL (busca e projeto). A 400 px ficam item, falta e OS; o resto vai
 * para a linha expansível (padrão 8 da Fase 3).
 */
export function ShortagesPage() {
  const [search] = useTableSearch()
  const { projeto } = useTableFilters(['projeto'])
  const query = useListStockShortages({ q: search.q, projectId: projeto || undefined, page: search.page, pageSize: search.pageSize })
  const projects = useSearchStockProjects()

  const columns: ColumnDef<ShortageRow>[] = [
    { id: 'item', header: 'Item', cell: ({ row: { original: r } }) => <Link to={'/app/estoque/itens/$id' as never} params={{ id: r.itemId } as never} className="font-semibold text-primary hover:underline"><span className="font-mono">{r.itemCode}</span> {r.itemName}</Link> },
    {
      id: 'short', header: () => <span className="inline-flex items-center">Falta<Dica nome="Falta" texto={RESERVATION_DICAS.falta} /></span>,
      cell: ({ row: { original: r } }) => <StatusChip tone="crit">{shortageLabel(r.shortQty, r.purchaseRequestLink)} {r.unitCode}</StatusChip>,
    },
    { id: 'available', header: () => <span className="inline-flex items-center">Disponível<Dica nome="Disponível" texto={RESERVATION_DICAS.disponivel} /></span>, meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => formatQuantity(r.available) },
    { id: 'orders', header: 'OS', cell: ({ row: { original: r } }) => <OrderLinks row={r} /> },
    { id: 'promised', header: 'Prometido', meta: { secondary: true }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{r.nearestPromisedDate ? formatDate(r.nearestPromisedDate) : '—'}</span> },
    {
      id: 'action', header: 'Compra', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.purchaseRequestLink
        ? <a href={r.purchaseRequestLink} className="font-semibold text-primary">Ver solicitação</a>
        : <Link to={'/app/compras' as never} className="font-semibold text-primary">Gerar solicitação de compra</Link>,
    },
  ]

  return (
    <>
      <PageHeading title="Estoque: Faltas">
        <p className="mt-2 text-muted-foreground">O que as OS abertas precisam e o estoque ainda não tem livre. Cada falta vira solicitação de compra.</p>
      </PageHeading>
      <InventoryTabs />
      <DataTable label="Faltas de material" columns={columns} query={query} getRowId={(r) => r.itemId} searchPlaceholder="Buscar pelo código ou nome do item"
        filters={[{ key: 'projeto', label: 'Projeto', options: (projects.data ?? []).map((p) => ({ value: p.id, label: `${p.number} — ${p.name}` })) }]}
        rowClassName={() => 'border-l-4 border-l-crit'}
        expandable={{
          label: (r) => `falta de ${r.itemCode}`,
          render: (r) => (
            <p className="text-sm tabular-nums">
              Disponível {formatQuantity(r.available)} {r.unitCode} · Prometido {r.nearestPromisedDate ? formatDate(r.nearestPromisedDate) : '—'} ·{' '}
              {r.purchaseRequestLink ? <a href={r.purchaseRequestLink} className="font-semibold text-primary">Ver solicitação</a> : <Link to={'/app/compras' as never} className="font-semibold text-primary">Gerar solicitação de compra</Link>}
            </p>
          ),
        }}
        empty={{ title: 'Nenhuma falta para as OS abertas.', description: 'Quando uma BOM liberada pedir mais do que o estoque tem livre, a falta aparece aqui.', action: <Link to={'/app/estoque/saldos' as never} className="text-sm font-semibold text-primary">Ver saldos</Link> }} />
    </>
  )
}
