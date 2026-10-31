import { Link, useNavigate } from '@tanstack/react-router'
import { FileUp } from 'lucide-react'
import { useListIncomingInvoices } from '@/api/generated/bemaqui'
import type { IncomingInvoiceStatus, IncomingInvoiceSummary } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useTableFilters } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatInstantDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { PageHeading } from '@/shell/PageHeading'
import { formatCnpj, INCOMING_STATUS, STATUS_FILTERS } from './incoming'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/**
 * Fiscal → Notas de entrada (FIS-04a, `/app/fiscal/entrada`): as NF-e importadas pelo XML do fornecedor, da mais recente para a mais antiga,
 * com o filtro de situação na URL; "Importar XML" com `fiscal.import_incoming`.
 */
export function IncomingInvoicesPage() {
  const navigate = useNavigate()
  const { situacao } = useTableFilters(['situacao'])
  const status = STATUS_FILTERS.find((f) => f.value === situacao)?.value as IncomingInvoiceStatus | undefined
  const list = useListIncomingInvoices({ status, pageSize: 100 }, { query: { retry: false } })
  const setFilter = (value: string | undefined) => void navigate({ to: '.', search: (s: Record<string, unknown>) => ({ ...s, situacao: value }) } as never)
  const data = list.data
  const importButton = data?.canImport ? <Link to="/app/fiscal/entrada/importar" className={primary}><FileUp aria-hidden="true" className="size-4" />Importar XML</Link> : null

  return (
    <>
      <PageHeading title="Notas de entrada" count={data?.items.filter((i) => i.status === 'Imported').length}>
        <p className="mt-2 text-muted-foreground">As NF-e dos fornecedores, importadas pelo XML e conferidas contra o pedido de compra antes de receber o material.</p>
      </PageHeading>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
        <div role="group" aria-label="Situação" className="flex flex-wrap gap-2">
          {STATUS_FILTERS.map((f) => (
            <button key={f.label} type="button" aria-pressed={status === f.value} onClick={() => setFilter(f.value)}
              className={cn('min-h-11 rounded-full border px-3 text-sm font-semibold', status === f.value ? 'border-primary bg-primary-soft text-primary' : 'border-border bg-card')}>
              {f.label}
            </button>
          ))}
        </div>
        {importButton}
      </div>

      {list.isPending ? (
        <div className="space-y-2"><Skeleton className="h-16" /><Skeleton className="h-16" /><Skeleton className="h-16" /></div>
      ) : list.isError || !data ? (
        <ErrorState message={list.error instanceof ApiError ? list.error.message : undefined} onRetry={() => void list.refetch()} />
      ) : data.items.length === 0 ? (
        status
          ? <EmptyState title="Nenhuma nota nesta situação." description="Volte para Todas para ver as notas de entrada." />
          : <EmptyState title="Nenhuma nota de entrada. Importe o XML que o fornecedor enviou." action={importButton ?? undefined} />
      ) : (
        <ul className="space-y-2">
          {data.items.map((i) => <InvoiceRow key={i.id} invoice={i} />)}
        </ul>
      )}
    </>
  )
}

function InvoiceRow({ invoice }: { invoice: IncomingInvoiceSummary }) {
  const status = INCOMING_STATUS[invoice.status]
  const pending = invoice.status === 'Imported' && invoice.pendingItems > 0
  return (
    <li>
      <Link to="/app/fiscal/entrada/$id" params={{ id: invoice.id }}
        className={cn('block rounded-md border border-border bg-card p-3 hover:bg-muted', pending && 'border-l-4 border-l-warn')}>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="font-semibold">
              NF-e <span className="tabular-nums">{invoice.number}</span> · série <span className="tabular-nums">{invoice.series}</span> — {invoice.issuerName}
            </p>
            <p className="text-xs text-muted-foreground">
              CNPJ <span className="tabular-nums">{formatCnpj(invoice.issuerDocument)}</span> · emitida em <span className="tabular-nums">{formatInstantDate(invoice.issuedAt)}</span>
              {' · '}<span className="tabular-nums">{invoice.itemsCount}</span> {invoice.itemsCount === 1 ? 'item' : 'itens'}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Money value={invoice.total} className="font-semibold" />
            <StatusChip tone={status.tone}>{status.label}</StatusChip>
            {pending ? <StatusChip tone="warn">{invoice.pendingItems === 1 ? '1 item sem de-para' : `${invoice.pendingItems} itens sem de-para`}</StatusChip> : null}
            {invoice.withoutOrder ? <StatusChip tone="info">Sem pedido</StatusChip> : null}
          </div>
        </div>
      </Link>
    </li>
  )
}
