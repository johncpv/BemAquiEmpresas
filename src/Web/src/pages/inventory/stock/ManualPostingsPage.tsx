import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListManualPostings } from '@/api/generated/bemaqui'
import type { ManualPostingKind, ManualPostingStatus, ManualPostingSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { DICAS, KIND, POSTING_STATUS } from './stock'

const STATUS_OPTIONS = (Object.keys(POSTING_STATUS) as ManualPostingStatus[]).map((value) => ({ value, label: POSTING_STATUS[value].label }))
const KIND_OPTIONS = (Object.keys(KIND) as ManualPostingKind[]).map((value) => ({ value, label: KIND[value] }))

/**
 * Estoque → Lançamentos pendentes (CA 9, 10): "Aguardando você" é o filtro padrão de quem aprova (padrão 5 da Fase 3); quem não
 * aprova vê os pendentes de todos.
 */
export function ManualPostingsPage() {
  const [search] = useTableSearch()
  const canApprove = useCan('inventory.approve')
  const { filtro, situacao, tipo } = useTableFilters(['filtro', 'situacao', 'tipo'])
  const show = filtro ?? (canApprove ? 'aguardando-voce' : 'pendentes')
  const awaiting = show === 'aguardando-voce'
  const status = (situacao || (show === 'pendentes' ? 'Pending' : undefined)) as ManualPostingStatus | undefined
  const query = useListManualPostings({ awaitingMe: awaiting || undefined, status: awaiting ? undefined : status, kind: (tipo || undefined) as ManualPostingKind | undefined, page: search.page, pageSize: search.pageSize })
  const columns: ColumnDef<ManualPostingSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: p } }) => <Link to={'/app/estoque/lancamentos/$id' as never} params={{ id: p.id } as never} className="font-semibold text-primary hover:underline">{p.number}</Link> },
    { id: 'kind', header: 'Tipo', cell: ({ row: { original: p } }) => KIND[p.kind] },
    { id: 'item', header: 'Item', meta: { secondary: true }, cell: ({ row: { original: p } }) => `${p.itemSummary ?? '—'}${p.lineCount > 1 ? ` e mais ${p.lineCount - 1}` : ''}` },
    { id: 'location', header: 'Local', meta: { secondary: true }, cell: ({ row: { original: p } }) => p.locationName ?? '—' },
    { id: 'value', header: 'Valor', meta: { numeric: true }, cell: ({ row: { original: p } }) => <Money value={p.estimatedValue} /> },
    { id: 'reason', header: 'Por que aprovar', meta: { secondary: true }, cell: ({ row: { original: p } }) => p.approvalReason ?? '—' },
    { id: 'by', header: 'Quem lançou', meta: { secondary: true }, cell: ({ row: { original: p } }) => `${p.requestedByName ?? '—'} · ${formatDateTime(p.requestedAt)}` },
    { id: 'status', header: 'Situação', cell: ({ row: { original: p } }) => <StatusChip tone={POSTING_STATUS[p.status].tone}>{POSTING_STATUS[p.status].label}</StatusChip> },
  ]
  return (
    <>
      <PageHeading title="Estoque: Lançamentos pendentes">
        <p className="mt-2 inline-flex items-center text-muted-foreground">Entradas e saídas manuais que esperam a decisão de outra pessoa.<Dica nome="Aguardando aprovação" texto={DICAS.aprovacao} /></p>
      </PageHeading>
      <InventoryTabs />
      <DataTable label="Lançamentos manuais" columns={columns} query={query} searchable={false} getRowId={(p) => p.id}
        filters={[
          { key: 'filtro', label: 'Mostrar', options: [{ value: 'aguardando-voce', label: 'Aguardando você' }, { value: 'pendentes', label: 'Todos os pendentes' }, { value: 'todos', label: 'Todos os lançamentos' }] },
          { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS },
          { key: 'tipo', label: 'Tipo', options: KIND_OPTIONS },
        ]}
        rowClassName={(p) => (p.awaitingMe ? 'border-l-4 border-l-warn' : undefined)}
        expandable={{ label: (p) => `detalhes de ${p.number}`, render: (p) => <p className="text-sm">{p.itemSummary ?? '—'} · {p.locationName ?? '—'} · {p.approvalReason ?? 'sem aprovação'} · {p.requestedByName ?? '—'}</p> }}
        empty={{ title: awaiting || show === 'pendentes' ? 'Nada aguardando aprovação.' : 'Nenhum lançamento manual ainda.', action: <Link to={'/app/estoque/saldos' as never} className="text-sm font-semibold text-primary">Ir para Saldos</Link> }} />
    </>
  )
}
