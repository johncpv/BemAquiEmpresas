import { Link, useNavigate } from '@tanstack/react-router'
import { FileText, FileUp, Keyboard } from 'lucide-react'
import { useState } from 'react'
import { startManualIncomingInvoice, useListIncomingInvoices } from '@/api/generated/bemaqui'
import type { IncomingInvoiceStatus, IncomingInvoiceSummary, IncomingReadingSummary } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useTableFilters } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime, formatInstantDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { PageHeading } from '@/shell/PageHeading'
import { formatCnpj, INCOMING_STATUS, STATUS_FILTERS } from './incoming'
import { failedTitle, SOURCE_MARK } from './reading'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'inline-flex min-h-11 items-center gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-50'

/**
 * Fiscal → Notas de entrada (FIS-04a, `/app/fiscal/entrada`): as NF-e importadas pelo XML do fornecedor, da mais recente para a mais antiga,
 * com o filtro de situação na URL; "Importar XML", "Importar PDF da DANFE" e "Digitar a nota" (FIS-05) com `fiscal.import_incoming`, e as
 * leituras de PDF ainda não conferidas. A nota sem XML leva a marca "Lida do PDF, sem XML" ou "Digitada, sem XML".
 */
export function IncomingInvoicesPage() {
  const navigate = useNavigate()
  const { situacao } = useTableFilters(['situacao'])
  const status = STATUS_FILTERS.find((f) => f.value === situacao)?.value as IncomingInvoiceStatus | undefined
  const list = useListIncomingInvoices({ status, pageSize: 100 }, { query: { retry: false } })
  const setFilter = (value: string | undefined) => void navigate({ to: '.', search: (s: Record<string, unknown>) => ({ ...s, situacao: value }) } as never)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const data = list.data
  const manual = async () => {
    setBusy(true)
    setError(null)
    try {
      const reading = await startManualIncomingInvoice()
      await navigate({ to: '/app/fiscal/entrada/leitura/$id', params: { id: reading.id } })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível abrir o formulário. Tente de novo.')
      setBusy(false)
    }
  }
  const importButton = data?.canImport ? <Link to="/app/fiscal/entrada/importar" className={primary}><FileUp aria-hidden="true" className="size-4" />Importar XML</Link> : null
  const otherButtons = data?.canImport ? (
    <>
      <Link to="/app/fiscal/entrada/importar-pdf" className={secondary}><FileText aria-hidden="true" className="size-4" />Importar PDF da DANFE</Link>
      <button type="button" disabled={busy} onClick={() => void manual()} className={secondary}><Keyboard aria-hidden="true" className="size-4" />Digitar a nota</button>
    </>
  ) : null

  return (
    <>
      <PageHeading title="Notas de entrada" count={data?.items.filter((i) => i.status === 'Imported').length}>
        <p className="mt-2 text-muted-foreground">As NF-e dos fornecedores, importadas pelo XML (ou lidas do PDF da DANFE e conferidas) e comparadas com o pedido de compra antes de receber o material.</p>
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
        <div className="flex flex-wrap gap-2">{importButton}{otherButtons}</div>
      </div>
      {error ? <p role="alert" className="mb-3 text-sm text-crit-ink">{error}</p> : null}

      {data && data.readings.length > 0 ? (
        <section aria-label="Leituras a conferir" className="mb-4">
          <h2 className="mb-2 font-semibold">Leituras e notas digitadas a conferir</h2>
          <ul className="space-y-2">{data.readings.map((r) => <ReadingRow key={r.id} reading={r} />)}</ul>
        </section>
      ) : null}

      {list.isPending ? (
        <div className="space-y-2"><Skeleton className="h-16" /><Skeleton className="h-16" /><Skeleton className="h-16" /></div>
      ) : list.isError || !data ? (
        <ErrorState message={list.error instanceof ApiError ? list.error.message : undefined} onRetry={() => void list.refetch()} />
      ) : data.items.length === 0 ? (
        status
          ? <EmptyState title="Nenhuma nota nesta situação." description="Volte para Todas para ver as notas de entrada." />
          : <EmptyState title="Nenhuma nota de entrada. Importe o XML que o fornecedor enviou." description="Sem o XML, importe o PDF da DANFE ou digite a nota." action={importButton ?? undefined} />
      ) : (
        <ul className="space-y-2">
          {data.items.map((i) => <InvoiceRow key={i.id} invoice={i} />)}
        </ul>
      )}
    </>
  )
}

function ReadingRow({ reading }: { reading: IncomingReadingSummary }) {
  const label = reading.status === 'Reading' ? 'Lendo o PDF…' : reading.status === 'Failed' ? failedTitle(reading.result) : 'Pronta para conferir'
  const tone = reading.status === 'Failed' ? 'crit' : reading.status === 'Reading' ? 'info' : 'warn'
  return (
    <li>
      <Link to="/app/fiscal/entrada/leitura/$id" params={{ id: reading.id }}
        className={cn('flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-3 hover:bg-muted', reading.status === 'Ready' && 'border-l-4 border-l-warn')}>
        <span className="min-w-0">
          <b className="font-semibold">{reading.source === 'Manual' ? 'Nota digitada' : reading.fileName ?? 'PDF da DANFE'}</b>
          <span className="text-xs text-muted-foreground"> · desde <span className="tabular-nums">{formatDateTime(reading.createdAt)}</span></span>
        </span>
        <StatusChip tone={tone}>{label}</StatusChip>
      </Link>
    </li>
  )
}

function InvoiceRow({ invoice }: { invoice: IncomingInvoiceSummary }) {
  const status = INCOMING_STATUS[invoice.status]
  const pending = invoice.status === 'Imported' && invoice.pendingItems > 0
  const mark = SOURCE_MARK[invoice.source]
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
            {mark ? <StatusChip tone="warn">{mark}</StatusChip> : null}
            {pending ? <StatusChip tone="warn">{invoice.pendingItems === 1 ? '1 item sem de-para' : `${invoice.pendingItems} itens sem de-para`}</StatusChip> : null}
            {invoice.withoutOrder ? <StatusChip tone="info">Sem pedido</StatusChip> : null}
          </div>
        </div>
      </Link>
    </li>
  )
}
