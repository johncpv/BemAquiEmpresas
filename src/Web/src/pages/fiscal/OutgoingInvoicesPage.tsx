import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { createOutgoingInvoice, useListInvoicesToIssue, useListOutgoingInvoices } from '@/api/generated/bemaqui'
import type { InvoiceQueueRow, InvoiceType, OutgoingInvoiceStatus } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate, formatInstantDate, formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { nfseLabel } from './nfse'
import { NfseQueue } from './NfseQueue'
import { INVOICE_DICAS, INVOICE_STATUS, INVOICE_STATUSES, INVOICE_TEXTS, invoiceLabel } from './outgoing-invoices'

const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-11 items-center rounded-md border border-border bg-card px-3 text-sm font-semibold'

/**
 * Fiscal → Notas de saída (/app/fiscal/saida, FIS-03a CA 16 e 17): "A emitir" (padrão para quem emite: romaneios expedidos sem nota, com
 * "Emitir NF-e" e o atraso acima de 1 dia útil em destaque) e "Emitidas" (filtros na URL e os totais do filtro).
 */
export function OutgoingInvoicesPage() {
  const canCreate = useCan('fiscal.create')
  return (
    <>
      <PageHeading title="Notas de saída">
        <p className="mt-2 text-muted-foreground">A NF-e do equipamento sai do romaneio expedido; a NFS-e da instalação sai no aceite técnico. A receita e o imposto do projeto contam pela data da nota.</p>
        <p className="mt-1 flex flex-wrap items-center gap-x-3 text-xs text-muted-foreground"><span className="inline-flex items-center">Venda mista<Dica nome="Venda mista" texto={INVOICE_DICAS.vendaMista} /></span><Link to="/app/fiscal/regras" className="font-semibold text-primary hover:underline">Regras fiscais</Link></p>
      </PageHeading>
      <UrlTabs label="Notas de saída" fallback={canCreate ? 'a-emitir' : 'emitidas'} tabs={[
        { id: 'a-emitir', label: 'A emitir', disabled: !canCreate, disabledReason: 'Só quem emite notas vê a fila.', content: <><ToIssue /><NfseQueue /></> },
        { id: 'emitidas', label: 'Emitidas', content: <Issued /> },
      ]} />
    </>
  )
}

function ToIssue() {
  const navigate = useNavigate()
  const queue = useListInvoicesToIssue()
  const idempotency = useIdempotencyKey()
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const issue = async (row: InvoiceQueueRow) => {
    setBusy(row.shipmentId)
    setError(null)
    try {
      const created = await createOutgoingInvoice({ shipmentId: row.shipmentId }, idempotency.options)
      idempotency.renew()
      void navigate({ to: '/app/fiscal/saida/$id', params: { id: created.id } } as never)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível criar a nota. Tente de novo.')
    } finally {
      setBusy(null)
    }
  }

  if (queue.isPending) return <div className="space-y-2"><Skeleton className="h-16" /><Skeleton className="h-16" /></div>
  if (queue.isError) return <ErrorState title="Não foi possível carregar a fila." onRetry={() => void queue.refetch()} />
  if (queue.data.length === 0) {
    return <EmptyState title={INVOICE_TEXTS.emptyQueue} description="Quando um romaneio for expedido, ele aparece aqui para emitir a NF-e." action={<Link to={'/app/producao/expedicao' as never} className={secondary}>Ver romaneios</Link>} />
  }

  const rows = [...queue.data].sort((a, b) => Number(b.late) - Number(a.late) || a.shippedOn.localeCompare(b.shippedOn))
  return (
    <div className="space-y-2">
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
      <ul className="space-y-2" aria-label="Romaneios esperando nota">
        {rows.map((r) => (
          <li key={r.shipmentId} className={cn('rounded-md border border-border bg-card p-3 text-sm', r.late && 'border-l-4 border-l-crit')}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="min-w-0">
                <p className="font-semibold">
                  <Link to={'/app/producao/expedicao/$id' as never} params={{ id: r.shipmentId } as never} className="text-primary tabular-nums hover:underline">{r.shipmentNumber}</Link>
                  <span className="text-muted-foreground"> · </span>
                  <Link to={'/app/projetos/$id' as never} params={{ id: r.projectId } as never} className="tabular-nums hover:underline">{r.projectNumber}</Link>
                </p>
                <p className="text-muted-foreground">{r.partyName} · expedido em <span className="tabular-nums">{formatDate(r.shippedOn)}</span> · <span className="tabular-nums">{r.daysSinceShipment}</span> dias</p>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                {r.late ? <StatusChip tone="crit">Atrasada</StatusChip> : null}
                <span className="tabular-nums font-semibold">{formatMoney(r.estimatedAmount)}</span>
                <button type="button" disabled={busy !== null} onClick={() => void issue(r)} className={primary}>{busy === r.shipmentId ? 'Criando…' : 'Emitir NF-e'}</button>
              </div>
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}

function Issued() {
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as { de?: string; ate?: string; situacao?: OutgoingInvoiceStatus; tipo?: InvoiceType; cliente?: string; projeto?: string; pagina?: number }
  const page = Number(search.pagina) > 0 ? Number(search.pagina) : 1
  const list = useListOutgoingInvoices({ from: search.de, to: search.ate, status: search.situacao, type: search.tipo, partyId: search.cliente, projectId: search.projeto, page, pageSize: 25 })
  const go = (next: Partial<typeof search>) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, pagina: undefined, ...next }) } as never)
  const input = 'min-h-11 rounded-md border border-input bg-card px-2 text-sm'

  let body
  if (list.isPending) body = <Skeleton className="h-40" />
  else if (list.isError) body = <ErrorState title="Não foi possível carregar as notas." onRetry={() => void list.refetch()} />
  else if (list.data.items.length === 0) {
    body = <EmptyState title={INVOICE_TEXTS.emptyIssued} action={<button type="button" className={secondary} onClick={() => go({ aba: 'a-emitir' } as never)}>Ver a fila A emitir</button>} />
  } else {
    const data = list.data
    const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
    body = (
      <>
        <p className="mb-2 text-sm tabular-nums">{data.total} notas · valor {formatMoney(data.totalAmount)} · impostos {formatMoney(data.taxAmount)}</p>
        <div className="overflow-x-auto rounded-md border border-border bg-card">
          <table className="w-full text-sm">
            <caption className="sr-only">Notas emitidas</caption>
            <thead className="border-b border-border text-left text-xs uppercase text-muted-foreground">
              <tr><th className="p-2">Número</th><th className="p-2 max-sm:hidden">Data</th><th className="p-2">Cliente</th><th className="p-2 max-sm:hidden">Projeto</th><th className="p-2 text-right">Valor</th><th className="p-2 max-sm:hidden">Ambiente</th><th className="p-2">Situação</th></tr>
            </thead>
            <tbody className="divide-y divide-border">
              {data.items.map((r) => (
                <tr key={r.id}>
                  <td className="p-2">{r.type === 'NFSe'
                    ? <Link to={'/app/fiscal/saida/nfse/$id' as never} params={{ id: r.id } as never} className="font-semibold text-primary tabular-nums hover:underline">{nfseLabel(r.number)}</Link>
                    : <Link to={'/app/fiscal/saida/$id' as never} params={{ id: r.id } as never} className="font-semibold text-primary tabular-nums hover:underline">{invoiceLabel(r.series, r.number)}</Link>}</td>
                  <td className="p-2 tabular-nums max-sm:hidden">{r.issuedOn ? formatDate(r.issuedOn) : formatInstantDate(r.createdAt)}</td>
                  <td className="p-2">{r.partyName}</td>
                  <td className="p-2 tabular-nums max-sm:hidden">{r.projectNumber ?? '—'}</td>
                  <td className="p-2 text-right tabular-nums">{formatMoney(r.totalAmount)}</td>
                  <td className="p-2 max-sm:hidden">{r.environment === 'Production' ? 'Produção' : 'Homologação'}</td>
                  <td className="p-2"><StatusChip tone={INVOICE_STATUS[r.status].tone}>{INVOICE_STATUS[r.status].label}</StatusChip></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {pages > 1 ? (
          <div className="mt-3 flex items-center gap-2 text-sm">
            <button type="button" disabled={page <= 1} onClick={() => go({ pagina: page - 1 })} className="min-h-11 rounded-md border border-border px-3 font-semibold disabled:opacity-50">Anterior</button>
            <span className="tabular-nums">{page} de {pages}</span>
            <button type="button" disabled={page >= pages} onClick={() => go({ pagina: page + 1 })} className="min-h-11 rounded-md border border-border px-3 font-semibold disabled:opacity-50">Próxima</button>
          </div>
        ) : null}
      </>
    )
  }

  return (
    <div>
      <div role="group" aria-label="Filtros" className="mb-3 flex flex-wrap items-end gap-2">
        <label className="flex flex-col text-sm"><span className="font-medium">De</span><input type="date" value={search.de ?? ''} onChange={(e) => go({ de: e.target.value || undefined })} className={input} /></label>
        <label className="flex flex-col text-sm"><span className="font-medium">Até</span><input type="date" value={search.ate ?? ''} onChange={(e) => go({ ate: e.target.value || undefined })} className={input} /></label>
        <label className="flex flex-col text-sm">
          <span className="font-medium">Situação</span>
          <select value={search.situacao ?? ''} onChange={(e) => go({ situacao: (e.target.value || undefined) as OutgoingInvoiceStatus | undefined })} className={input}>
            <option value="">Todas</option>
            {INVOICE_STATUSES.map((s) => <option key={s} value={s}>{INVOICE_STATUS[s].label}</option>)}
          </select>
        </label>
        <label className="flex flex-col text-sm">
          <span className="font-medium">Tipo</span>
          <select value={search.tipo ?? ''} onChange={(e) => go({ tipo: (e.target.value || undefined) as InvoiceType | undefined })} className={input}>
            <option value="">Todas</option>
            <option value="NFe">NF-e</option>
            <option value="NFSe">NFS-e</option>
          </select>
        </label>
        {search.cliente || search.projeto ? <button type="button" className={secondary} onClick={() => go({ cliente: undefined, projeto: undefined })}>Tirar o filtro de cliente e projeto</button> : null}
      </div>
      {body}
    </div>
  )
}
