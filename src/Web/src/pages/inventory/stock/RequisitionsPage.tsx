import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListMaterialRequisitions, useListRequisitionsToPick } from '@/api/generated/bemaqui'
import type { MaterialRequisitionKind, MaterialRequisitionStatus, RequisitionSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { pickingSummary, REQUISITION_DICAS, REQUISITION_KIND, REQUISITION_STATUS } from './requisitions'
import { formatQuantity } from './stock'

const STATUS_OPTIONS = (Object.keys(REQUISITION_STATUS) as MaterialRequisitionStatus[]).map((value) => ({ value, label: REQUISITION_STATUS[value].label }))
const KIND_OPTIONS = (Object.keys(REQUISITION_KIND) as MaterialRequisitionKind[]).map((value) => ({ value, label: REQUISITION_KIND[value] }))
const SHOW_LABELS: Record<string, string> = { 'aguardando-voce': 'Aguardando você', todos: 'Todas as requisições' }

/** "Para separar hoje" (propositivo): OS em aberto com material reservado a baixar, as com pedido do operador primeiro. */
function ToPick() {
  const query = useListRequisitionsToPick()
  if (query.isPending) return <Skeleton className="h-20" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o que há para separar." onRetry={() => query.refetch()} />
  if (query.data.length === 0) return null
  return (
    <section aria-labelledby="para-separar" className="mb-4 space-y-2">
      <h2 id="para-separar" className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Para separar hoje · {pickingSummary(query.data.length)}</h2>
      <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {query.data.map((r) => (
          <li key={r.productionOrderId} className={r.openRequests > 0 ? 'rounded-md border border-l-4 border-border border-l-warn bg-card p-3 text-sm' : 'rounded-md border border-border bg-card p-3 text-sm'}>
            <Link to={'/app/producao/os/$id' as never} params={{ id: r.productionOrderId } as never} search={{ aba: 'material' } as never} className="font-semibold text-primary hover:underline">{r.orderNumber ?? 'OS'}</Link>
            <span className="text-muted-foreground"> · {r.projectNumber ?? '—'}</span>
            <p className="tabular-nums">{r.items} {r.items === 1 ? 'item' : 'itens'} · {formatQuantity(r.reserved)} reservado · etapa {r.currentStepName ?? '—'}</p>
            {r.openRequests > 0 ? <Tag>{r.openRequests === 1 ? '1 pedido do operador' : `${r.openRequests} pedidos do operador`}</Tag> : null}
          </li>
        ))}
      </ul>
    </section>
  )
}

/**
 * Estoque → Requisições (EST-03, padrão 5): o filtro padrão "Aguardando você" junta os pedidos dos operadores (para quem baixa) e as
 * requisições aguardando aprovação (para quem aprova); em cima, "Para separar hoje". A 400 px, linhas em cartões.
 */
export function RequisitionsPage() {
  const [search] = useTableSearch()
  const { filtro, situacao, tipo } = useTableFilters(['filtro', 'situacao', 'tipo'])
  const show = filtro ?? 'aguardando-voce'
  const awaiting = show === 'aguardando-voce'
  const query = useListMaterialRequisitions({
    awaitingMe: awaiting || undefined, status: awaiting ? undefined : (situacao || undefined) as MaterialRequisitionStatus | undefined,
    kind: (tipo || undefined) as MaterialRequisitionKind | undefined, page: search.page, pageSize: search.pageSize,
  })
  const columns: ColumnDef<RequisitionSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: r } }) => <Link to={'/app/estoque/requisicoes/$id' as never} params={{ id: r.id } as never} className="font-semibold text-primary hover:underline">{r.number}</Link> },
    { id: 'kind', header: 'Tipo', meta: { secondary: true }, cell: ({ row: { original: r } }) => REQUISITION_KIND[r.kind] },
    { id: 'order', header: 'OS', cell: ({ row: { original: r } }) => <span>{r.orderNumber ?? '—'}{r.stepName ? <span className="text-muted-foreground"> · etapa {r.stepName}</span> : null}</span> },
    { id: 'item', header: 'Material', meta: { secondary: true }, cell: ({ row: { original: r } }) => `${r.firstItem ?? '—'}${r.lineCount > 1 ? ` e mais ${r.lineCount - 1}` : ''}` },
    { id: 'value', header: 'Valor', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => (r.value > 0 ? <Money value={r.value} /> : r.overBomValue > 0 ? <span className="text-muted-foreground">acima da BOM <Money value={r.overBomValue} /></span> : '—') },
    { id: 'by', header: 'Quem pediu', meta: { secondary: true }, cell: ({ row: { original: r } }) => `${r.requestedByName ?? '—'} · ${formatDateTime(r.requestedAt)}` },
    { id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => <StatusChip tone={REQUISITION_STATUS[r.status].tone}>{REQUISITION_STATUS[r.status].label}</StatusChip> },
  ]
  return (
    <>
      <PageHeading title="Estoque: Requisições">
        <p className="mt-2 inline-flex items-center text-muted-foreground">Material baixado para as OS, pedidos dos operadores e devoluções.<Dica nome="Consumo acima da BOM" texto={REQUISITION_DICAS.consumoAcima} /></p>
      </PageHeading>
      <InventoryTabs />
      <ToPick />
      <p className="mb-2 text-sm text-muted-foreground">Mostrando: {SHOW_LABELS[show] ?? SHOW_LABELS.todos}</p>
      <DataTable label="Requisições de material" columns={columns} query={query} searchable={false} getRowId={(r) => r.id}
        filters={[
          { key: 'filtro', label: 'Mostrar', options: [{ value: 'aguardando-voce', label: 'Aguardando você' }, { value: 'todos', label: 'Todas as requisições' }] },
          { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS },
          { key: 'tipo', label: 'Tipo', options: KIND_OPTIONS },
        ]}
        rowClassName={(r) => (r.awaitingMe ? 'border-l-4 border-l-warn' : undefined)}
        expandable={{ label: (r) => `detalhes de ${r.number}`, render: (r) => <p className="text-sm">{REQUISITION_KIND[r.kind]} · {r.firstItem ?? '—'} · {r.projectNumber ?? '—'} · {r.requestedByName ?? '—'}</p> }}
        empty={{
          title: awaiting ? 'Nenhum pedido de material aguardando.' : 'Nenhuma requisição ainda.',
          action: <Link to={'/app/producao/os' as never} className="text-sm font-semibold text-primary">Ver OS com material reservado</Link>,
        }} />
    </>
  )
}
