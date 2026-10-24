import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { completeStockOpening, getGetStockOpeningQueryKey, getListStockBalancesQueryKey, useGetStockOpening, useListItemCategories, useListLocations, useListStockBalances } from '@/api/generated/bemaqui'
import type { BalanceRow, ItemType, ManualPostingDetail, ManualPostingKind, StockSituation } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { TYPE_LABELS } from '../item-labels'
import { InventoryTabs } from '../InventoryTabs'
import { AdjustmentDialog } from './AdjustmentDialog'
import { OpeningBatchDialog } from './OpeningBatchDialog'
import { RESERVATION_DICAS } from './reservations'
import { PostingDialog } from './PostingDialog'
import { DICAS, formatQuantity, formatUnitCost, SITUATION, SITUATION_OPTIONS } from './stock'

type Open = { kind: ManualPostingKind } | { batch: true } | { adjust: true } | null

const secondaryButton = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/**
 * Estoque → Saldos (EST-01a CA 28): por item e local, físico, reservado, disponível, mínimo, custo médio e situação. "Lançar
 * entrada" é a ação principal; saldo inicial enquanto a implantação está aberta. A 400 px fica item, local, disponível e
 * situação; o resto vai para a linha expansível (padrão 8 da Fase 3).
 */
export function BalancesPage() {
  const [search] = useTableSearch()
  const { local, categoria, tipo, situacao } = useTableFilters(['local', 'categoria', 'tipo', 'situacao'])
  const client = useQueryClient()
  const navigate = useNavigate()
  const canCreate = useCan('inventory.create')
  const query = useListStockBalances({ q: search.q, locationId: local || undefined, categoryId: categoria || undefined, type: (tipo || undefined) as ItemType | undefined, situation: (situacao || undefined) as StockSituation | undefined, page: search.page, pageSize: search.pageSize })
  const opening = useGetStockOpening()
  const locations = useListLocations({ pageSize: 100 })
  const categories = useListItemCategories()
  const [open, setOpen] = useState<Open>(null)
  const [steps, setSteps] = useState<Step[]>([])
  const openingOpen = opening.data ? !opening.data.completed : false

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getListStockBalancesQueryKey() })
    await client.invalidateQueries({ queryKey: getGetStockOpeningQueryKey() })
  }
  const posted = (kind: ManualPostingKind | 'batch') => async (detail: ManualPostingDetail) => {
    setOpen(null)
    await refresh()
    const view = { label: detail.status === 'Pending' ? 'Ver lançamento pendente' : 'Ver movimento', onSelect: () => void navigate({ to: '/app/estoque/lancamentos/$id' as never, params: { id: detail.id } as never }) }
    setSteps(kind === 'OpeningBalance' || kind === 'batch'
      ? [{ label: 'Lançar o próximo item', onSelect: () => setOpen({ kind: 'OpeningBalance' }) }, { label: 'Ver saldos', onSelect: () => setSteps([]) }, view]
      : [view, { label: 'Lançar outro', onSelect: () => setOpen({ kind: kind as ManualPostingKind }) }])
  }

  const columns: ColumnDef<BalanceRow>[] = [
    { id: 'item', header: 'Item', cell: ({ row: { original: b } }) => <Link to={'/app/estoque/itens/$id' as never} params={{ id: b.itemId } as never} search={{ aba: 'movimentos' } as never} className="font-semibold text-primary hover:underline"><span className="font-mono">{b.itemCode}</span> {b.itemName}</Link> },
    { id: 'location', header: 'Local', cell: ({ row: { original: b } }) => <span title={b.locationName}>{b.locationCode}</span> },
    { id: 'onHand', header: 'Físico', meta: { numeric: true, secondary: true }, cell: ({ row: { original: b } }) => `${formatQuantity(b.onHand)} ${b.unitCode}` },
    { id: 'reserved', header: () => <span className="inline-flex items-center">Reservado<Dica nome="Reservado" texto={RESERVATION_DICAS.reservado} /></span>, meta: { numeric: true, secondary: true }, cell: ({ row: { original: b } }) => formatQuantity(b.reserved) },
    { id: 'available', header: () => <span className="inline-flex items-center">Disponível<Dica nome="Disponível" texto={DICAS.disponivel} /></span>, meta: { numeric: true }, cell: ({ row: { original: b } }) => <b>{formatQuantity(b.available)}</b> },
    { id: 'min', header: 'Mínimo', meta: { numeric: true, secondary: true }, cell: ({ row: { original: b } }) => formatQuantity(b.minStock) },
    { id: 'avg', header: () => <span className="inline-flex items-center">Custo médio<Dica nome="Custo médio" texto={DICAS.custoMedio} /></span>, meta: { numeric: true, secondary: true }, cell: ({ row: { original: b } }) => formatUnitCost(b.averageCost) },
    { id: 'situation', header: 'Situação', cell: ({ row: { original: b } }) => <StatusChip tone={SITUATION[b.situation].tone}>{SITUATION[b.situation].label}</StatusChip> },
  ]

  const entry = <button type="button" onClick={() => setOpen({ kind: 'Receipt' })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Lançar entrada</button>
  const emptyAction = openingOpen
    ? <Can perm="inventory.create"><button type="button" onClick={() => setOpen({ kind: 'OpeningBalance' })} className="text-sm font-semibold text-primary">Lançar saldo inicial</button></Can>
    : <Can perm="inventory.create"><button type="button" onClick={() => setOpen({ kind: 'Receipt' })} className="text-sm font-semibold text-primary">Lançar entrada</button></Can>

  return (
    <>
      <PageHeading title="Estoque: Saldos">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">O que existe em cada local, o que está reservado e o custo médio de cada item.</p>
          <div className="flex flex-wrap gap-2">
            <Can perm="inventory.adjust">
              <button type="button" onClick={() => setOpen({ adjust: true })} className={secondaryButton}>Lançar ajuste</button>
            </Can>
            <Can perm="inventory.create">
              <div className="flex flex-wrap gap-2">
                {entry}
                <button type="button" onClick={() => setOpen({ kind: 'Issue' })} className={secondaryButton}>Lançar saída</button>
                <button type="button" onClick={() => setOpen({ kind: 'Transfer' })} className={secondaryButton}>Transferir</button>
                {openingOpen ? (
                  <>
                    <button type="button" onClick={() => setOpen({ kind: 'OpeningBalance' })} className={secondaryButton}>Lançar saldo inicial</button>
                    <button type="button" onClick={() => setOpen({ batch: true })} className={secondaryButton}>Saldo inicial em lote</button>
                  </>
                ) : null}
              </div>
            </Can>
          </div>
        </div>
      </PageHeading>
      <InventoryTabs />
      {opening.data?.completed && opening.data.completedAt ? (
        <p role="note" className="mb-3 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
          Implantação concluída em {formatDateTime(opening.data.completedAt)} por {opening.data.completedByName ?? 'usuário removido'}. Saldo novo entra por entrada ou ajuste de estoque.
        </p>
      ) : null}
      {openingOpen && opening.data ? (
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <span className="inline-flex items-center">Implantação do estoque aberta: lance o saldo inicial de cada item e local.<Dica nome="Saldo inicial" texto={DICAS.saldoInicial} /></span>
          <Can perm="inventory.approve">
            <ConfirmDialog size="lg" verb="Concluir implantação do estoque" done="Implantação do estoque concluída" title="Concluir a implantação do estoque?"
              description="Depois disso, saldo novo só entra por entrada manual ou ajuste. Não dá para desfazer."
              impact={<div className="space-y-1"><p>{opening.data.itemCount} itens em {opening.data.locationCount} locais com saldo inicial ({opening.data.lineCount} lançamentos)</p><p>Valor total: <Money value={opening.data.totalValue} className="font-semibold" /></p></div>}
              onConfirm={async () => { await completeStockOpening({ version: opening.data!.version }); await refresh() }} onReload={refresh}
              trigger={<button type="button" className="rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground">Concluir implantação do estoque</button>} />
          </Can>
        </div>
      ) : null}
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setSteps([])} /></div> : null}
      {!canCreate ? <p className="mb-3 text-sm text-muted-foreground">Modo leitura: lançar entrada, saída e transferência exige permissão de criar no Estoque.</p> : null}
      <DataTable label="Saldos de estoque" columns={columns} query={query} getRowId={(b) => `${b.itemId}:${b.locationId}`} searchPlaceholder="Buscar pelo código ou nome do item"
        filters={[
          { key: 'local', label: 'Local', options: (locations.data?.items ?? []).map((l) => ({ value: l.id, label: `${l.code} — ${l.name}` })) },
          { key: 'categoria', label: 'Categoria', options: (categories.data ?? []).map((c) => ({ value: c.id, label: c.name })) },
          { key: 'tipo', label: 'Tipo', options: (Object.keys(TYPE_LABELS) as ItemType[]).filter((t) => t !== 'Service').map((t) => ({ value: t, label: TYPE_LABELS[t] })) },
          { key: 'situacao', label: 'Situação', options: SITUATION_OPTIONS },
        ]}
        rowClassName={(b) => (b.situation === 'Out' ? 'border-l-4 border-l-crit' : b.situation === 'Low' ? 'border-l-4 border-l-warn' : undefined)}
        expandable={{
          label: (b) => `saldo de ${b.itemCode} em ${b.locationCode}`,
          render: (b) => <p className="text-sm tabular-nums">Físico {formatQuantity(b.onHand)} {b.unitCode} · Reservado {formatQuantity(b.reserved)} · Mínimo {formatQuantity(b.minStock)} · Custo médio {formatUnitCost(b.averageCost)}</p>,
        }}
        empty={{ title: openingOpen ? 'Nenhum saldo ainda. Comece lançando o saldo inicial de cada local.' : 'Nenhum saldo ainda.', description: openingOpen ? undefined : 'Lance uma entrada para começar.', action: emptyAction }} />
      {open && 'kind' in open ? <PostingDialog kind={open.kind} onClose={() => setOpen(null)} onPosted={posted(open.kind)} /> : null}
      {open && 'batch' in open ? <OpeningBatchDialog onClose={() => setOpen(null)} onPosted={posted('batch')} /> : null}
      {open && 'adjust' in open ? (
        <AdjustmentDialog locationId={local || undefined} onClose={() => setOpen(null)} onDone={(d) => {
          setOpen(null)
          void refresh()
          setSteps([{ label: `Ver ajuste ${d.number}`, onSelect: () => void navigate({ to: '/app/estoque/ajustes/$id' as never, params: { id: d.id } as never }) }, { label: 'Lançar outro ajuste', onSelect: () => setOpen({ adjust: true }) }])
        }} />
      ) : null}
    </>
  )
}
