import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListItemSerials } from '@/api/generated/bemaqui'
import type { ItemSerialRow } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableSearch } from '@/components/data-table/table-search'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { FINISHED_GOODS, SERIAL_STATUS } from '@/pages/production/orders/fabrication'

/**
 * Aba "Séries" do item (PRD-06a2 CA 11): série, OS, projeto, situação e local, as mais novas primeiro, com inventory.view. Produto sem
 * controle de série mostra o vazio "Este produto não controla série.". A 400 px ficam série e situação; OS, projeto e local vão para a linha
 * expansível.
 */
export function ItemSerials({ itemId, tracksSerial }: { itemId: string; tracksSerial: boolean }) {
  const [search] = useTableSearch()
  const query = useListItemSerials(itemId, { page: search.page, pageSize: search.pageSize }, { query: { enabled: tracksSerial } })
  if (!tracksSerial) return <EmptyState className="mt-3" title={FINISHED_GOODS.semSerie} description="A entrada do produto acabado é por quantidade. Para controlar série, marque o item em Dados." />
  const columns: ColumnDef<ItemSerialRow>[] = [
    // EST-04 CA 1: a série abre a rastreabilidade (de onde veio: OS, projeto, cliente, romaneio e lotes).
    {
      id: 'serial', header: 'Série',
      cell: ({ row }) => <Link to={'/app/estoque/rastreabilidade' as never} search={{ item: itemId, serie: row.original.id } as never} className="font-semibold tabular-nums text-primary underline">{row.original.serialNumber}</Link>,
    },
    { id: 'status', header: 'Situação', cell: ({ row }) => <StatusChip tone={SERIAL_STATUS[row.original.status]?.tone ?? 'neutral'}>{SERIAL_STATUS[row.original.status]?.label ?? row.original.status}</StatusChip> },
    {
      id: 'order', header: 'OS', meta: { secondary: true },
      cell: ({ row }) => row.original.productionOrderId ? <Link to="/app/producao/os/$id" params={{ id: row.original.productionOrderId }} search={{ aba: 'series' } as never} className="text-primary underline">{row.original.productionOrderNumber}</Link> : '—',
    },
    {
      id: 'project', header: 'Projeto', meta: { secondary: true },
      cell: ({ row }) => row.original.projectId ? <Link to="/app/projetos/$id" params={{ id: row.original.projectId }} className="text-primary underline">{row.original.projectNumber}</Link> : '—',
    },
    { id: 'location', header: 'Local', meta: { secondary: true }, cell: ({ row }) => row.original.locationName ?? '—' },
    { id: 'createdAt', header: 'Entrada', meta: { secondary: true }, cell: ({ row }) => <span className="tabular-nums">{formatDateTime(row.original.createdAt)}</span> },
  ]
  return (
    <div className="mt-3">
      <DataTable label="Séries do item" columns={columns} query={query} searchable={false} getRowId={(s) => s.id}
        expandable={{ label: (s) => `detalhes de ${s.serialNumber}`, render: (s) => <p className="text-sm">{s.productionOrderNumber ?? 'Sem OS'} · {s.projectNumber ?? 'sem projeto'} · {s.locationName ?? 'sem local'}</p> }}
        empty={{ title: 'Nenhuma série ainda.', description: 'As séries nascem quando a OS de fabricação deste produto é concluída.' }} />
    </div>
  )
}
