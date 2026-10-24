import { Link, useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListPurchaseProjectOptions, useListPurchaseRequests, useListPurchaseRequestsByItem } from '@/api/generated/bemaqui'
import type { PurchaseItemRow, PurchaseRequestOrigin, PurchaseRequestStatus, PurchaseRequestSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { cn } from '@/lib/utils'
import { destination, ORIGIN, ORIGIN_OPTIONS, PURCHASE_DICAS, rowAccent, SHOW_OPTIONS, STATUS, STATUS_OPTIONS, urgency } from './purchasing'

const newButton = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const EMPTY = 'Nenhuma solicitação aberta. As faltas da BOM e o estoque mínimo abrem solicitações sozinhos.'

function NeededBy({ date }: { date: string | null | undefined }) {
  if (!date) return <span>—</span>
  const u = urgency(date)
  return (
    <span className="flex flex-wrap items-center gap-1 tabular-nums">
      {formatDate(date)}
      {u ? <StatusChip tone={u.tone}>{u.label}</StatusChip> : null}
    </span>
  )
}

const originTag = (origin: PurchaseRequestOrigin) => <Tag>{ORIGIN[origin]}</Tag>

/**
 * Fábrica → Compras → Solicitações (CMP-01 CA 12): número, origem, destino, OS, itens, a necessidade mais próxima com o chip de
 * urgência e a situação. Para quem edita, o padrão é "Aguardando você" (padrão 5 da Fase 3); a visão "Por item" soma as linhas abertas
 * por item. Filtros na URL; a 400 px ficam número, itens, necessidade e situação.
 */
export function PurchaseRequestsPage() {
  const [search] = useTableSearch()
  const navigate = useNavigate()
  const canEdit = useCan('purchasing.edit')
  const { filtro, origem, situacao, projeto, item, os, visao } = useTableFilters(['filtro', 'origem', 'situacao', 'projeto', 'item', 'os', 'visao'])
  const byItem = visao === 'item'
  const show = filtro ?? (canEdit ? 'aguardando-voce' : 'abertas')
  const projects = useListPurchaseProjectOptions()
  const list = useListPurchaseRequests({
    show, origin: (origem || undefined) as PurchaseRequestOrigin | undefined, status: (situacao || undefined) as PurchaseRequestStatus | undefined, projectId: projeto || undefined,
    itemId: item || undefined, productionOrderId: os || undefined, q: search.q, page: search.page, pageSize: search.pageSize,
  }, { query: { enabled: !byItem } })
  const items = useListPurchaseRequestsByItem({ q: search.q, projectId: projeto || undefined, page: search.page, pageSize: search.pageSize }, { query: { enabled: byItem } })

  const columns: ColumnDef<PurchaseRequestSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: r } }) => <Link to={'/app/compras/solicitacoes/$id' as never} params={{ id: r.id } as never} className="font-semibold tabular-nums text-primary hover:underline">{r.number}</Link> },
    {
      id: 'origin', header: () => <span className="inline-flex items-center">Origem<Dica nome="Falta da BOM" texto={PURCHASE_DICAS.faltaBom} /></span>, meta: { secondary: true },
      cell: ({ row: { original: r } }) => originTag(r.origin),
    },
    { id: 'destination', header: 'Destino', meta: { secondary: true }, cell: ({ row: { original: r } }) => destination(r.projectNumber, r.projectName) },
    {
      id: 'order', header: 'OS', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.productionOrderId
        ? <Link to={'/app/producao/os/$id' as never} params={{ id: r.productionOrderId } as never} search={{ aba: 'material' } as never} className="font-semibold text-primary hover:underline">{r.orderNumber ?? 'OS'}</Link>
        : '—',
    },
    { id: 'items', header: 'Itens', cell: ({ row: { original: r } }) => <span>{r.itemSummary}</span> },
    { id: 'needed', header: () => <span className="inline-flex items-center">Necessidade<Dica nome="Necessidade" texto={PURCHASE_DICAS.necessidade} /></span>, cell: ({ row: { original: r } }) => <NeededBy date={r.nearestNeededBy} /> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => <StatusChip tone={STATUS[r.status].tone}>{STATUS[r.status].label}</StatusChip> },
  ]

  const itemColumns: ColumnDef<PurchaseItemRow>[] = [
    { id: 'item', header: 'Item', cell: ({ row: { original: r } }) => <span><span className="font-mono">{r.itemCode}</span> {r.itemName}</span> },
    { id: 'quantity', header: 'A comprar', meta: { numeric: true }, cell: ({ row: { original: r } }) => <b className="tabular-nums">{formatQuantity(r.openQuantity)} {r.unitCode}</b> },
    { id: 'needed', header: 'Necessidade', cell: ({ row: { original: r } }) => <NeededBy date={r.nearestNeededBy} /> },
    {
      id: 'requests', header: 'Solicitações', meta: { secondary: true }, cell: ({ row: { original: r } }) => (
        <ul className="space-y-0.5">
          {r.requests.map((q) => (
            <li key={`${q.requestId}-${q.neededBy}-${q.quantity}`} className="tabular-nums">
              <Link to={'/app/compras/solicitacoes/$id' as never} params={{ id: q.requestId } as never} className="font-semibold text-primary hover:underline">{q.number}</Link>
              <span className="text-muted-foreground"> · {formatQuantity(q.quantity)} · {formatDate(q.neededBy)} · {q.projectNumber ?? 'Estoque geral'}{q.orderNumber ? ` · ${q.orderNumber}` : ''}</span>
            </li>
          ))}
        </ul>
      ),
    },
  ]

  const create = <Can perm="purchasing.create"><Link to={'/app/compras/solicitacoes/nova' as never} className={newButton}>Nova solicitação</Link></Can>
  const views = [{ id: 'lista', label: 'Por solicitação' }, { id: 'item', label: 'Por item' }]
  const showLabel = SHOW_OPTIONS.find((o) => o.value === show)?.label ?? 'Todas'
  return (
    <>
      <PageHeading title="Compras: Solicitações">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="inline-flex items-center text-muted-foreground">O que precisa ser comprado e para quando. A falta da BOM e o estoque mínimo<Dica nome="Estoque mínimo" texto={PURCHASE_DICAS.minimo} /> abrem solicitações sozinhos.</p>
          {create}
        </div>
      </PageHeading>
      <div role="group" aria-label="Visão da lista" className="mb-3 flex flex-wrap items-center gap-2">
        {views.map((v) => (
          <button key={v.id} type="button" aria-pressed={(v.id === 'item') === byItem}
            onClick={() => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, visao: v.id === 'item' ? 'item' : undefined, page: undefined }) } as never)}
            className={cn('rounded-md border px-3 py-1.5 text-sm font-semibold', (v.id === 'item') === byItem ? 'border-primary bg-primary-soft text-foreground' : 'border-border bg-card text-muted-foreground')}>{v.label}</button>
        ))}
        {!byItem ? <span className="text-sm text-muted-foreground">Mostrando: {showLabel}</span> : <span className="text-sm text-muted-foreground">Linhas abertas de todas as solicitações, somadas por item.</span>}
        {item || os ? (
          <Link to={'/app/compras/solicitacoes' as never} search={{ filtro: 'todas' } as never} className="text-sm font-semibold text-primary">
            {item ? 'Filtrando por um item' : 'Filtrando por uma OS'} · Limpar filtro
          </Link>
        ) : null}
      </div>
      {byItem ? (
        <DataTable label="Itens a comprar" columns={itemColumns} query={items} getRowId={(r) => r.itemId} searchPlaceholder="Buscar pelo código ou nome do item"
          filters={[{ key: 'projeto', label: 'Projeto', options: (projects.data ?? []).map((p) => ({ value: p.id, label: `${p.number} — ${p.name}` })) }]}
          rowClassName={(r) => rowAccent(r.nearestNeededBy)}
          expandable={{ label: (r) => `solicitações de ${r.itemCode}`, render: (r) => <p className="text-sm tabular-nums">{r.requestCount} {r.requestCount === 1 ? 'solicitação' : 'solicitações'}: {r.requests.map((q) => q.number).join(', ')}</p> }}
          empty={{ title: EMPTY, action: create }} />
      ) : (
        <DataTable label="Solicitações de compra" columns={columns} query={list} getRowId={(r) => r.id} searchPlaceholder="Buscar pelo número (SC-…)"
          filters={[
            { key: 'filtro', label: 'Mostrar', options: SHOW_OPTIONS },
            { key: 'origem', label: 'Origem', options: ORIGIN_OPTIONS },
            { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS },
            { key: 'projeto', label: 'Projeto', options: (projects.data ?? []).map((p) => ({ value: p.id, label: `${p.number} — ${p.name}` })) },
          ]}
          rowClassName={(r) => (r.status === 'Open' || r.status === 'Quoting' ? rowAccent(r.nearestNeededBy) : undefined)}
          expandable={{
            label: (r) => `detalhes de ${r.number}`,
            render: (r) => <p className="text-sm">{ORIGIN[r.origin]} · {destination(r.projectNumber, r.projectName)}{r.orderNumber ? ` · ${r.orderNumber}` : ''} · {r.openLineCount} de {r.lineCount} {r.lineCount === 1 ? 'linha aberta' : 'linhas abertas'}</p>,
          }}
          empty={{ title: EMPTY, action: create }} />
      )}
    </>
  )
}
