import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useListPayables } from '@/api/generated/bemaqui'
import type { PayableDetail } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { useCan } from '@/pages/use-can'
import { today } from '../receivables/receivables'
import { METHOD_LABELS, ORIGIN_LABELS, PAYABLE_SITUATIONS, payableSituation } from './payables'

/** Financeiro → A pagar (FIN-02): o que a empresa precisa pagar, por vencimento; só a OP aprovada paga. */
export function PayablesPage() {
  const [search] = useTableSearch()
  const { situacao } = useTableFilters(['situacao'])
  const query = useListPayables({ q: search.q, situation: situacao || undefined, page: search.page, pageSize: search.pageSize, sort: search.sort })
  const table = { ...query, data: query.data?.page }
  const totals = query.data?.totals
  const navigate = useNavigate()
  const canCreate = useCan('finance.create')
  const [selected, setSelected] = useState<string[]>([])
  const limit = new Date(Date.parse(today() + 'T00:00:00Z') + 7 * 86_400_000).toISOString().slice(0, 10)
  const payable = (p: PayableDetail) => (p.status === 'Open' || p.status === 'PartiallyPaid') && !!p.paymentMethod
  const dueSoon = (query.data?.page.items ?? []).filter((p) => payable(p) && !!p.dueDate && p.dueDate <= limit).map((p) => p.id)
  const createOrder = () => void navigate({ to: '/app/financeiro/ordens-de-pagamento/nova', search: { titulos: selected.join(',') } } as never)

  const columns: ColumnDef<PayableDetail>[] = [
    { id: 'number', header: 'Número', meta: { sortField: 'number' }, cell: ({ row: { original: p } }) => <span className="font-mono">{p.number}</span> },
    { id: 'party', header: 'Fornecedor', cell: ({ row: { original: p } }) => p.partyName },
    { id: 'description', header: 'Descrição', meta: { secondary: true }, cell: ({ row: { original: p } }) => <span className="inline-flex items-center gap-1.5">{p.description}{ORIGIN_LABELS[p.origin] ? <Tag>{ORIGIN_LABELS[p.origin]}</Tag> : null}{p.isEstimate ? <Tag>Estimado</Tag> : null}</span> },
    { id: 'method', header: 'Forma', meta: { secondary: true }, cell: ({ row: { original: p } }) => (p.paymentMethod ? METHOD_LABELS[p.paymentMethod] : '—') },
    { id: 'due', header: 'Vencimento', meta: { sortField: 'dueDate' }, cell: ({ row: { original: p } }) => <span className="tabular-nums">{p.dueDate ? formatDate(p.dueDate) : '—'}</span> },
    { id: 'net', header: 'Líquido', meta: { numeric: true }, cell: ({ row: { original: p } }) => <Money value={p.netAmount} /> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: p } }) => { const s = payableSituation(p); return <StatusChip tone={s.tone}>{s.label}</StatusChip> } },
  ]

  return (
    <>
      <PageHeading title="Financeiro: A pagar">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">O que a empresa precisa pagar, por vencimento.</p>
          <Can perm="finance.create">
            <Link to="/app/financeiro/pagar/novo" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo título a pagar</Link>
          </Can>
        </div>
      </PageHeading>
      <FinanceTabs />
      {canCreate ? (
        <div className="mb-3 flex flex-wrap items-center gap-2">
          {dueSoon.length > 0 ? <button type="button" onClick={() => setSelected([...new Set([...selected, ...dueSoon])])} className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold">Selecionar os que vencem até {formatDate(limit)}</button> : null}
          {selected.length > 0 ? <button type="button" onClick={createOrder} className="rounded-md bg-primary px-3 py-1.5 text-sm font-semibold text-primary-foreground">Criar ordem de pagamento ({selected.length})</button> : null}
        </div>
      ) : null}
      <DataTable
        label="Títulos a pagar"
        columns={columns}
        query={table}
        getRowId={(p) => p.id}
        selectable={canCreate ? { selected, onChange: setSelected, rowLabel: (p) => `título ${p.number}` } : undefined}
        rowHref={(p) => `/app/financeiro/pagar/${p.id}`}
        rowClassName={(p) => (p.isOverdue ? 'border-l-4 border-l-crit' : undefined)}
        searchPlaceholder="Buscar pelo número ou descrição"
        filters={[{ key: 'situacao', label: 'Situação', options: PAYABLE_SITUATIONS }]}
        totals={totals ? { net: <span className="block">Em aberto <Money value={totals.open} /><br /><span className="text-crit-ink">Vencido <Money value={totals.overdue} /></span></span> } : undefined}
        empty={{
          title: 'Lance a primeira conta a pagar ou cadastre as despesas fixas do mês.',
          description: 'Cada título tem fornecedor, conta de despesa e vencimento. O pagamento sai pela ordem de pagamento.',
          action: (
            <Can perm="finance.create">
              <span className="flex flex-wrap justify-center gap-3">
                <Link to="/app/financeiro/pagar/novo" className="text-sm font-semibold text-primary">Novo título a pagar</Link>
                <Link to="/app/financeiro/recorrentes" className="text-sm font-semibold text-primary">Nova despesa recorrente</Link>
              </span>
            </Can>
          ),
        }}
      />
    </>
  )
}
