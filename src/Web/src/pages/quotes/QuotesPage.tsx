import { Link, useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListQuoteCosts, useListQuotes } from '@/api/generated/bemaqui'
import type { QuoteSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { NewQuoteDialog } from './NewQuoteDialog'
import { CopyQuoteDialog } from './CopyQuoteDialog'
import { QUOTE_STATUS, validityLabel } from './quotes'
import { formatDate } from '@/lib/format'
import { today } from '../finance/receivables/receivables'

const SITUATIONS = [{ value: 'rascunho', label: 'Rascunhos' }, { value: 'enviado', label: 'Enviados' }, { value: 'perdido', label: 'Perdidos' }, { value: 'expirado', label: 'Expirados' }, { value: 'aprovado', label: 'Aprovados' }]
const pct = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${v.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`)

/** Comercial → Orçamentos (COM-01a): custo e margem só para quem tem quotes.view_cost (D7). */
export function QuotesPage() {
  const [search] = useTableSearch()
  const { situacao } = useTableFilters(['situacao'])
  const navigate = useNavigate()
  const canCost = useCan('quotes.view_cost')
  const canApprove = useCan('quotes.approve')
  const query = useListQuotes({ q: search.q, situation: situacao || undefined, page: search.page, pageSize: search.pageSize })
  const ids = (query.data?.items ?? []).map((q) => q.id).join(',')
  const costs = useListQuoteCosts({ ids }, { query: { enabled: canCost && ids.length > 0 } })
  const costOf = (id: string) => costs.data?.find((c) => c.id === id)
  const columns: ColumnDef<QuoteSummary>[] = [
    { id: 'number', header: 'Orçamento', cell: ({ row: { original: q } }) => <span><Link to={'/app/orcamentos/$id' as never} params={{ id: q.id } as never} className="font-semibold text-primary hover:underline">{q.number}{q.revision > 0 ? ` rev. ${q.revision}` : ''}</Link>{(q.revisions ?? 1) > 1 ? <span className="block text-xs text-muted-foreground">{q.revisions} revisões</span> : null}</span> },
    { id: 'party', header: 'Cliente e equipamento', cell: ({ row: { original: q } }) => <span>{q.partyName}{q.mainItem ? <span className="block text-xs text-muted-foreground">{q.mainItem}</span> : null}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: q } }) => (
      <span className="flex flex-col items-start gap-1">
        <StatusChip tone={QUOTE_STATUS[q.status]?.tone ?? 'neutral'}>{QUOTE_STATUS[q.status]?.label ?? q.status}</StatusChip>
        {canApprove && q.status === 'Sent' ? <Link to={'/app/orcamentos/$id' as never} params={{ id: q.id } as never} search={{ aprovar: '1' } as never} className="text-xs font-semibold text-primary hover:underline">Aprovar e gerar contrato</Link> : null}
      </span>
    ) },
    ...(canCost ? [
      { id: 'cost', header: 'Custo', meta: { numeric: true, secondary: true }, cell: ({ row: { original: q } }: { row: { original: QuoteSummary } }) => (costOf(q.id) ? <Money value={costOf(q.id)!.totalCost} /> : '—') },
      { id: 'margin', header: 'Margem', meta: { numeric: true, secondary: true }, cell: ({ row: { original: q } }: { row: { original: QuoteSummary } }) => <span className={(costOf(q.id)?.marginPct ?? 0) < 0 ? 'text-crit-ink tabular-nums' : 'tabular-nums'}>{pct(costOf(q.id)?.marginPct)}</span> },
    ] as ColumnDef<QuoteSummary>[] : []),
    { id: 'valid', header: 'Validade', meta: { secondary: true }, cell: ({ row: { original: q } }) => (q.validUntil ? <span className="tabular-nums">{formatDate(q.validUntil)}{q.status === 'Draft' || q.status === 'Sent' ? <span className="block text-xs text-muted-foreground">{validityLabel(q.validUntil, today())}</span> : null}</span> : '—') },
    { id: 'price', header: 'Preço', meta: { numeric: true }, cell: ({ row: { original: q } }) => <Money value={q.totalPrice} /> },
  ]
  const canCreate = useCan('quotes.create')
  const open = (id: string) => void navigate({ to: '/app/orcamentos/$id', params: { id }, search: { aba: 'itens', novo: '1' } } as never)
  const create = canCreate ? <span className="flex flex-wrap gap-2"><CopyQuoteDialog mode="quote" onDone={open} /><NewQuoteDialog onCreated={open} /></span> : null
  return (
    <>
      <PageHeading title="Comercial: Orçamentos">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Pré-propostas com validade. Ao aprovar, o sistema gera o contrato, o projeto e as parcelas a receber.</p>
          {create}
        </div>
      </PageHeading>
      <DataTable label="Orçamentos" columns={columns} query={query} getRowId={(q) => q.id} searchPlaceholder="Buscar pelo número ou cliente"
        filters={[{ key: 'situacao', label: 'Situação', options: SITUATIONS }]}
        empty={{ title: 'Crie o primeiro orçamento para um cliente.', action: create }} />
    </>
  )
}
