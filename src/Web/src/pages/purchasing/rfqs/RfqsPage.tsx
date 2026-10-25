import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListRfqs } from '@/api/generated/bemaqui'
import type { RfqStatus, RfqSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { RFQ_STATUS, SHOW_OPTIONS, STATUS_OPTIONS } from './rfqs'

const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/**
 * Fábrica → Compras → Cotações (CMP-02): número, itens, fornecedores (respondidas de quantos), situação e quando nasceu. Abre nas
 * abertas; a 400 px ficam número, itens e situação.
 */
export function RfqsPage() {
  const [search] = useTableSearch()
  const { filtro, situacao } = useTableFilters(['filtro', 'situacao'])
  const show = filtro ?? 'abertas'
  const list = useListRfqs({ show, status: (situacao || undefined) as RfqStatus | undefined, q: search.q, page: search.page, pageSize: search.pageSize })

  const columns: ColumnDef<RfqSummary>[] = [
    { id: 'number', header: 'Número', cell: ({ row: { original: r } }) => <Link to={'/app/compras/cotacoes/$id' as never} params={{ id: r.id } as never} className="font-semibold tabular-nums text-primary hover:underline">{r.number}</Link> },
    { id: 'items', header: 'Itens', cell: ({ row: { original: r } }) => <span>{r.itemSummary}</span> },
    { id: 'suppliers', header: 'Respostas', meta: { secondary: true, numeric: true }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{r.respondedCount} de {r.supplierCount}</span> },
    { id: 'created', header: 'Criada em', meta: { secondary: true }, cell: ({ row: { original: r } }) => <span className="tabular-nums">{formatDateTime(r.createdAt)}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => <StatusChip tone={RFQ_STATUS[r.status].tone}>{RFQ_STATUS[r.status].label}</StatusChip> },
  ]

  const quote = <Can perm="purchasing.create"><Link to={'/app/compras/solicitacoes' as never} search={{ visao: 'item' } as never} className={primary}>Cotar itens das solicitações</Link></Can>
  const typed = <Can perm="purchasing.create"><Link to={'/app/compras/cotacoes/nova' as never} className={secondary}>Nova cotação sem solicitação</Link></Can>
  const showLabel = SHOW_OPTIONS.find((o) => o.value === show)?.label ?? 'Todas'
  return (
    <>
      <PageHeading title="Compras: Cotações">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Peça preço a mais de um fornecedor, compare lado a lado e gere os pedidos do vencedor.</p>
          <span className="flex flex-wrap gap-2">{typed}{quote}</span>
        </div>
      </PageHeading>
      <p className="mb-3 text-sm text-muted-foreground">Mostrando: {showLabel}</p>
      <DataTable label="Cotações" columns={columns} query={list} getRowId={(r) => r.id} searchPlaceholder="Buscar pelo número (COT-…)"
        filters={[{ key: 'filtro', label: 'Mostrar', options: SHOW_OPTIONS }, { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS }]}
        expandable={{ label: (r) => `detalhes de ${r.number}`, render: (r) => <p className="text-sm">{r.lineCount} {r.lineCount === 1 ? 'item' : 'itens'} · {r.respondedCount} de {r.supplierCount} fornecedores responderam</p> }}
        empty={{ title: 'Nenhuma cotação aberta.', action: <span className="flex flex-wrap justify-center gap-2">{quote}{typed}</span> }} />
    </>
  )
}
