import { useGetItemKardex, useGetItemStockContext } from '@/api/generated/bemaqui'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { formatQuantity, formatUnitCost } from './stock'
import { movementColumns } from './movement-columns'

/**
 * Aba "Movimentos" do item (kardex, CA 24): pela ordem de lançamento, nunca pela data de negócio, com o saldo do local e o custo
 * médio depois de cada movimento. A 400 px fica data, tipo, quantidade e saldo depois.
 */
export function ItemKardex({ itemId }: { itemId: string }) {
  const [search] = useTableSearch()
  const { local } = useTableFilters(['local'])
  const context = useGetItemStockContext(itemId)
  const query = useGetItemKardex(itemId, { locationId: local || undefined, page: search.page, pageSize: search.pageSize })
  const columns = movementColumns({ kardex: true }).filter((c) => c.id !== 'origin' && c.id !== 'user' && c.id !== 'total' && c.id !== 'unitCost')
  const positions = context.data?.positions ?? []
  return (
    <div className="mt-3 space-y-3">
      {context.data ? (
        <p className="text-sm tabular-nums">
          Físico total <b>{formatQuantity(context.data.totalOnHand)} {context.data.unitCode}</b> · custo médio <b>{formatUnitCost(context.data.averageCost)}</b>
          {positions.length > 1 ? <> · {positions.map((p) => `${p.locationCode} ${formatQuantity(p.onHand)}`).join(' · ')}</> : null}
        </p>
      ) : null}
      <DataTable label="Movimentos do item" columns={columns} query={query} searchable={false} getRowId={(m) => m.id}
        filters={positions.length > 1 ? [{ key: 'local', label: 'Local', options: positions.map((p) => ({ value: p.locationId, label: `${p.locationCode} — ${p.locationName}` })) }] : []}
        expandable={{ label: (m) => `detalhes de ${m.number}`, render: (m) => <p className="text-sm tabular-nums">{m.locationCode}{m.lotNumber ? ` · lote ${m.lotNumber}` : ''} · custo {formatUnitCost(m.unitCost)} · médio depois {formatUnitCost(m.averageCostAfter)}</p> }}
        empty={{ title: 'Este item ainda não tem movimento.', description: 'Lance o saldo inicial ou uma entrada em Saldos.' }} />
    </div>
  )
}
