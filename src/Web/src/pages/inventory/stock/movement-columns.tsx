import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import type { MovementRow } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDateTime } from '@/lib/format'
import { formatQuantity, formatUnitCost, MOVEMENT_TYPE } from './stock'

/** Colunas comuns de Movimentos e do kardex (CA 24): número, data, tipo, local, lote, quantidade com sinal, custo e origem. */
export function movementColumns({ kardex = false }: { kardex?: boolean } = {}): ColumnDef<MovementRow>[] {
  return [
    { id: 'number', header: 'Número', cell: ({ row: { original: m } }) => <Link to={'/app/estoque/movimentos/$id' as never} params={{ id: m.id } as never} className="font-semibold text-primary hover:underline">{m.number}</Link> },
    { id: 'at', header: 'Data', cell: ({ row: { original: m } }) => <span className="tabular-nums">{formatDateTime(m.at)}</span> },
    {
      id: 'type', header: 'Tipo', cell: ({ row: { original: m } }) => (
        <span className="inline-flex flex-wrap items-center gap-1">
          {MOVEMENT_TYPE[m.type]}
          {m.reversesMovementId ? <Tag>Estorno</Tag> : null}
          {m.reversedByMovementId ? <StatusChip tone="neutral">Estornado</StatusChip> : null}
        </span>
      ),
    },
    ...(kardex ? [] : [{ id: 'item', header: 'Item', cell: ({ row: { original: m } }: { row: { original: MovementRow } }) => <span><span className="font-mono">{m.itemCode}</span> {m.itemName}</span> } as ColumnDef<MovementRow>]),
    { id: 'location', header: 'Local', meta: { secondary: true }, cell: ({ row: { original: m } }) => m.locationCode },
    { id: 'lot', header: 'Lote', meta: { secondary: true }, cell: ({ row: { original: m } }) => m.lotNumber ?? '—' },
    { id: 'quantity', header: 'Quantidade', meta: { numeric: true }, cell: ({ row: { original: m } }) => <span className={m.quantity < 0 ? 'text-crit-ink' : undefined}>{formatQuantity(m.quantity, true)} {m.unitCode}</span> },
    ...(kardex
      ? [{ id: 'balance', header: 'Saldo depois', meta: { numeric: true }, cell: ({ row: { original: m } }: { row: { original: MovementRow } }) => formatQuantity(m.balanceAfter) } as ColumnDef<MovementRow>,
        { id: 'avg', header: 'Custo médio depois', meta: { numeric: true, secondary: true }, cell: ({ row: { original: m } }: { row: { original: MovementRow } }) => formatUnitCost(m.averageCostAfter) } as ColumnDef<MovementRow>]
      : []),
    { id: 'unitCost', header: 'Custo unitário', meta: { numeric: true, secondary: true }, cell: ({ row: { original: m } }) => formatUnitCost(m.unitCost) },
    { id: 'total', header: 'Total', meta: { numeric: true, secondary: true }, cell: ({ row: { original: m } }) => <Money value={m.totalCost} /> },
    { id: 'origin', header: 'Origem', meta: { secondary: true }, cell: ({ row: { original: m } }) => (m.sourceLink ? <Link to={m.sourceLink as never} className="text-primary hover:underline">{m.sourceNumber ?? 'Lançamento'}</Link> : (m.sourceNumber ?? m.sourceType)) },
    { id: 'user', header: 'Quem lançou', meta: { secondary: true }, cell: ({ row: { original: m } }) => m.userName ?? 'Sistema' },
  ]
}
