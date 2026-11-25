import { Download } from 'lucide-react'
import { useState } from 'react'
import { getExportSaleReturnsUrl, useGetSaleReturnsReport, useListStores } from '@/api/generated/bemaqui'
import type { SaleReturnReportGroup } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifyError } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'

/** Hoje (em São Paulo) e o primeiro dia do mês, no formato da API. */
function defaultPeriod(now = new Date()): { from: string; to: string } {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now)
  return { from: `${today.slice(0, 8)}01`, to: today }
}

const dateTime = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })

function Totals({ title, groups }: { title: string; groups: readonly SaleReturnReportGroup[] }) {
  return (
    <section className="rounded-md border border-border p-3">
      <h2 className="mb-2 text-sm font-semibold">{title}</h2>
      <ul className="space-y-1 text-sm">
        {groups.map((g) => (
          <li key={g.key} className="flex justify-between gap-2">
            <span className="truncate">{g.label}</span>
            <span className="shrink-0 tabular-nums">{g.count} · <Money value={Number(g.amount)} /></span>
          </li>
        ))}
      </ul>
    </section>
  )
}

/**
 * Loja → Devoluções (PDV-06 CA 12): as devoluções concluídas no período, por operador, motivo e forma de reembolso, com a exportação. Carregando,
 * vazio ("Nenhuma devolução no período.") e erro.
 */
export function SaleReturnsPage() {
  const [period, setPeriod] = useState(defaultPeriod)
  const [storeId, setStoreId] = useState('')
  const stores = useListStores()
  const params = { From: period.from, To: period.to, StoreId: storeId || undefined }
  const valid = !!period.from && !!period.to && period.from <= period.to
  const report = useGetSaleReturnsReport(params, { query: { enabled: valid } })
  const [downloading, setDownloading] = useState(false)
  const rows = report.data?.rows ?? []

  const exportCsv = async () => {
    setDownloading(true)
    try {
      await apiDownload(getExportSaleReturnsUrl(params), `devolucoes-${period.from}-a-${period.to}.csv`)
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível exportar. Tente de novo.')
    } finally {
      setDownloading(false)
    }
  }

  const exportButton = (
    <button type="button" disabled={!valid || downloading || rows.length === 0} onClick={() => void exportCsv()}
      className="inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60">
      <Download aria-hidden="true" className="size-4" />{downloading ? 'Exportando…' : 'Exportar'}
    </button>
  )

  return (
    <>
      <PageHeading title="Devoluções" description="Devoluções e trocas feitas nos caixas, por operador, motivo e forma de reembolso." actions={exportButton} />
      <FormGrid>
        <Field label="De" required size="sm" dica="Data da devolução, no horário de Brasília. Escolha até um ano por vez.">
          {(p) => <input {...p} type="date" value={period.from} onChange={(e) => setPeriod({ ...period, from: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Até" required size="sm">
          {(p) => <input {...p} type="date" value={period.to} onChange={(e) => setPeriod({ ...period, to: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Loja" size="md">
          {(p) => (
            <select {...p} value={storeId} onChange={(e) => setStoreId(e.target.value)} className={inputClass}>
              <option value="">Todas as lojas</option>
              {(stores.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          )}
        </Field>
      </FormGrid>
      <div className="mt-4 space-y-4">
        {!valid ? <p role="alert" className="text-sm text-crit-ink">A data final tem de ser igual ou depois da inicial.</p>
          : report.isPending ? <Skeleton className="h-48" />
          : report.isError ? <ErrorState title="Não foi possível carregar as devoluções." message={report.error instanceof ApiError ? report.error.message : undefined} onRetry={() => report.refetch()} />
          : rows.length === 0 ? (
            <EmptyState title="Nenhuma devolução no período." description="As devoluções aparecem aqui quando o caixa conclui uma devolução ou troca. Mude o período ou a loja para ver outras." />
          ) : (
            <>
              <div className="grid gap-3 md:grid-cols-3">
                <Totals title="Por operador" groups={report.data?.byOperator ?? []} />
                <Totals title="Por motivo" groups={report.data?.byReason ?? []} />
                <Totals title="Por forma de reembolso" groups={report.data?.byRefundKind ?? []} />
              </div>
              {report.data?.truncated ? <p className="text-sm text-muted-foreground">Mostrando as 2.000 mais recentes. Diminua o período para ver todas.</p> : null}
              <div className="overflow-x-auto rounded-md border border-border">
                <table className="w-full text-sm">
                  <caption className="sr-only">Devoluções de {formatDate(period.from)} a {formatDate(period.to)}</caption>
                  <thead className="bg-muted text-left">
                    <tr>
                      <th className="p-2">Devolução</th><th className="p-2">Quando</th><th className="p-2">Loja</th><th className="p-2">Operador</th><th className="p-2">Venda</th>
                      <th className="p-2">Reembolso<Dica nome="Reembolso" texto="Vale-troca: o cliente leva um vale com código e validade. Dinheiro: saiu do caixa, com o supervisor. Estorno no cartão: feito na maquininha, com o NSU." /></th>
                      <th className="p-2">Motivos</th><th className="p-2 text-right">Valor</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {rows.map((r) => (
                      <tr key={r.id}>
                        <td className="p-2 tabular-nums">{r.number}</td>
                        <td className="p-2 tabular-nums">{dateTime(r.completedAt)}</td>
                        <td className="p-2">{r.storeName}</td>
                        <td className="p-2">{r.operatorName}</td>
                        <td className="p-2 tabular-nums">{r.saleNumber}</td>
                        <td className="p-2">{r.refundLabel}{r.isExchange ? ' · troca' : ''}</td>
                        <td className="p-2">{r.reasons}</td>
                        <td className="p-2 text-right"><Money value={Number(r.total)} /></td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot className="border-t-2 border-border font-semibold">
                    <tr><td className="p-2" colSpan={7}>Total no período</td><td className="p-2 text-right"><Money value={Number(report.data?.total ?? 0)} /></td></tr>
                  </tfoot>
                </table>
              </div>
            </>
          )}
      </div>
    </>
  )
}
