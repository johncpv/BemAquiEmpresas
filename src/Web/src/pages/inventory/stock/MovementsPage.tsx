import { Link, useNavigate } from '@tanstack/react-router'
import { useListLocations, useListStockMovements } from '@/api/generated/bemaqui'
import type { StockMovementType } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { DateInput } from '@/components/form/inputs'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { movementColumns } from './movement-columns'
import { formatUnitCost, MOVEMENT_TYPE_OPTIONS } from './stock'

/** Estoque → Movimentos (CA 24): do mais recente para o mais antigo, com filtros na URL (item, local, tipo, período e origem). */
export function MovementsPage() {
  const [search] = useTableSearch()
  const { item, local, tipo, de, ate, origem, projeto, os } = useTableFilters(['item', 'local', 'tipo', 'de', 'ate', 'origem', 'projeto', 'os'])
  const navigate = useNavigate()
  const setPeriod = (key: 'de' | 'ate', value: string) =>
    void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ...prev, [key]: value || undefined, page: undefined }) } as never)
  const locations = useListLocations({ pageSize: 100 })
  const query = useListStockMovements({
    number: search.q, itemId: item || undefined, locationId: local || undefined, type: (tipo || undefined) as StockMovementType | undefined, from: de || undefined, to: ate || undefined,
    origin: origem || undefined, projectId: projeto || undefined, productionOrderId: os || undefined, page: search.page, pageSize: search.pageSize,
  })
  return (
    <>
      <PageHeading title="Estoque: Movimentos">
        <p className="mt-2 text-muted-foreground">Toda entrada, saída e transferência, na ordem em que foi lançada. Movimento não é apagado: é estornado.</p>
      </PageHeading>
      <InventoryTabs />
      <div role="group" aria-label="Período" className="mb-3 flex flex-wrap items-end gap-3 text-sm">
        <label className="space-y-1 font-medium">De<DateInput value={de ?? ''} onChange={(v) => setPeriod('de', v)} /></label>
        <label className="space-y-1 font-medium">Até<DateInput value={ate ?? ''} onChange={(v) => setPeriod('ate', v)} /></label>
        {de || ate ? <button type="button" onClick={() => { setPeriod('de', ''); setPeriod('ate', '') }} className="pb-2 font-semibold text-primary">Limpar período</button> : null}
      </div>
      <DataTable label="Movimentos de estoque" columns={movementColumns()} query={query} getRowId={(m) => m.id} searchPlaceholder="Número do movimento ou do documento"
        filters={[
          { key: 'local', label: 'Local', options: (locations.data?.items ?? []).map((l) => ({ value: l.id, label: `${l.code} — ${l.name}` })) },
          { key: 'tipo', label: 'Tipo', options: MOVEMENT_TYPE_OPTIONS },
          { key: 'origem', label: 'Origem', options: [{ value: 'Manual', label: 'Lançamento manual' }, { value: 'Document', label: 'Documento' }] },
        ]}
        expandable={{ label: (m) => `detalhes de ${m.number}`, render: (m) => <p className="text-sm tabular-nums">{m.locationCode}{m.lotNumber ? ` · lote ${m.lotNumber}` : ''} · {formatUnitCost(m.unitCost)} · {m.userName ?? 'Sistema'}</p> }}
        empty={{ title: 'Nenhum movimento ainda.', description: 'Os movimentos aparecem aqui quando o saldo inicial, as entradas e as saídas forem lançados.', action: <Link to={'/app/estoque/saldos' as never} className="text-sm font-semibold text-primary">Ir para Saldos</Link> }} />
    </>
  )
}
