import { useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { applyStockShortfall, getListStockShortfallsQueryKey, useListLocations, useListStockShortfalls } from '@/api/generated/bemaqui'
import type { StockShortfallRow } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { AdjustmentDialog } from './AdjustmentDialog'
import { PostingDialog } from './PostingDialog'
import { formatQuantity } from './stock'
import { applyLabel, canApply, SHORTFALL_DICAS } from './shortfalls'

const linkButton = 'text-left text-sm font-semibold text-primary hover:underline disabled:text-muted-foreground disabled:no-underline'

/** As vendas do caixa com falta do item, com o número e quanto faltou em cada uma. */
function SaleList({ row }: { row: StockShortfallRow }) {
  return (
    <ul className="space-y-0.5">
      {row.sales.map((s) => (
        <li key={s.shortfallId} className="tabular-nums">
          <span className="font-semibold">Venda {s.sourceNumber}</span>
          <span className="text-muted-foreground"> · falta {formatQuantity(s.quantity)} · {formatDateTime(s.occurredAt)}</span>
        </li>
      ))}
    </ul>
  )
}

type Open = { adjust: string } | { transfer: StockShortfallRow } | null

/**
 * Estoque → Divergências (PDV-05 CA 7): por loja (o local de estoque dela) e item, a falta das vendas do caixa que o estoque não tinha, desde
 * quando e as vendas ligadas. Ações: "Aplicar baixa" (com o disponível cobrindo a falta), "Ajustar estoque" e "Transferir do depósito".
 */
export function StockShortfallsPage() {
  const [search] = useTableSearch()
  const { local } = useTableFilters(['local'])
  const query = useListStockShortfalls({ q: search.q, locationId: local || undefined, page: search.page, pageSize: search.pageSize })
  const locations = useListLocations({ pageSize: 100 })
  const client = useQueryClient()
  const idempotency = useIdempotencyKey()
  const [open, setOpen] = useState<Open>(null)
  const refresh = () => client.invalidateQueries({ queryKey: getListStockShortfallsQueryKey().slice(0, 1) })

  const actions = (r: StockShortfallRow) => (
    <div className="flex flex-col items-start gap-1">
      <Can perm="inventory.adjust">
        <ConfirmDialog verb="Aplicar baixa" done="Baixa aplicada" title={`Aplicar a baixa de ${r.itemCode}?`}
          description={`Sai do estoque de ${r.locationName} o que o disponível cobre, da venda mais antiga para a mais nova. ${applyLabel(r)}`}
          onConfirm={async () => { await applyStockShortfall({ itemId: r.itemId, locationId: r.locationId }, idempotency.options); idempotency.renew(); await refresh() }}
          onReload={refresh}
          trigger={<button type="button" disabled={!canApply(r)} className={linkButton}>Aplicar baixa</button>} />
        <button type="button" onClick={() => setOpen({ adjust: r.locationId })} className={linkButton}>Ajustar estoque</button>
      </Can>
      <Can perm="inventory.create">
        <button type="button" onClick={() => setOpen({ transfer: r })} className={linkButton}>Transferir do depósito</button>
      </Can>
    </div>
  )

  const columns: ColumnDef<StockShortfallRow>[] = [
    { id: 'item', header: 'Item', cell: ({ row: { original: r } }) => <span><span className="font-mono">{r.itemCode}</span> {r.itemName}</span> },
    { id: 'location', header: 'Loja (local)', cell: ({ row: { original: r } }) => r.locationName },
    {
      id: 'quantity', header: () => <span className="inline-flex items-center">Falta<Dica nome="Falta" texto={SHORTFALL_DICAS.falta} /></span>,
      cell: ({ row: { original: r } }) => <StatusChip tone="crit">{formatQuantity(r.quantity)} {r.unitCode}</StatusChip>,
    },
    { id: 'available', header: () => <span className="inline-flex items-center">Disponível<Dica nome="Disponível" texto={SHORTFALL_DICAS.disponivel} /></span>, meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => formatQuantity(r.available) },
    { id: 'since', header: 'Desde', meta: { secondary: true }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{formatDateTime(r.since)}</span> },
    { id: 'sales', header: 'Vendas', meta: { secondary: true }, cell: ({ row: { original: r } }) => <SaleList row={r} /> },
    { id: 'actions', header: 'Ações', cell: ({ row: { original: r } }) => actions(r) },
  ]

  return (
    <>
      <PageHeading title="Estoque: Divergências" description="Vendas do caixa que saíram sem saldo no sistema. A venda vale; ajuste o estoque e aplique a baixa." />
      <DataTable label="Divergências de estoque" columns={columns} query={query} getRowId={(r) => `${r.locationId}:${r.itemId}`} searchPlaceholder="Buscar pelo código ou nome do item"
        filters={[{ key: 'local', label: 'Local', options: (locations.data?.items ?? []).map((l) => ({ value: l.id, label: `${l.code} — ${l.name}` })) }]}
        rowClassName={() => 'border-l-4 border-l-crit'}
        expandable={{
          label: (r) => `divergência de ${r.itemCode} em ${r.locationName}`,
          render: (r) => (
            <div className="space-y-2 text-sm">
              <p className="tabular-nums">Disponível {formatQuantity(r.available)} {r.unitCode} · desde {formatDateTime(r.since)}</p>
              <SaleList row={r} />
            </div>
          ),
        }}
        empty={{ title: 'Nenhuma divergência. O estoque das lojas está batendo.', description: 'Quando o caixa vender um item que o sistema não tinha em saldo, a falta aparece aqui.' }} />
      {open && 'adjust' in open ? <AdjustmentDialog locationId={open.adjust} onClose={() => setOpen(null)} onDone={() => { setOpen(null); void refresh() }} /> : null}
      {open && 'transfer' in open ? (
        <PostingDialog kind="Transfer" item={{ id: open.transfer.itemId, label: `${open.transfer.itemCode} — ${open.transfer.itemName}` }}
          onClose={() => setOpen(null)} onPosted={() => { setOpen(null); void refresh() }} />
      ) : null}
    </>
  )
}
