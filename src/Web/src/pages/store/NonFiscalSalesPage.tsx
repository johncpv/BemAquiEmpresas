import { Download } from 'lucide-react'
import { useState } from 'react'
import { getExportNonFiscalSalesReportUrl, useGetNonFiscalSalesReport, useListStores } from '@/api/generated/bemaqui'
import { ApiError, apiDownload } from '@/api/http'
import { Field, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifyError } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'

/** Hoje (data de negócio em São Paulo) e o primeiro dia do mês, no formato da API. */
function defaultPeriod(now = new Date()): { from: string; to: string } {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now)
  return { from: `${today.slice(0, 8)}01`, to: today }
}

const PERIOD_DICA = 'O dia da venda é a data de negócio, no horário de Brasília. Escolha até um ano por vez.'

/**
 * Loja → Vendas sem documento fiscal (IMP-01 CA 12): por período, loja e operador, a quantidade e o valor das vendas do balcão concluídas sem
 * NFC-e nem NF-e, com exportação em CSV. Carregando, vazio ("Nenhuma venda sem documento fiscal no período.") e erro.
 */
export function NonFiscalSalesPage() {
  const [period, setPeriod] = useState(defaultPeriod)
  const [storeId, setStoreId] = useState('')
  const [operatorId, setOperatorId] = useState('')
  const stores = useListStores()
  const params = { from: period.from, to: period.to, storeId: storeId || undefined, operatorId: operatorId || undefined }
  const valid = !!period.from && !!period.to && period.from <= period.to
  const report = useGetNonFiscalSalesReport(params, { query: { enabled: valid } })
  const [downloading, setDownloading] = useState(false)
  const rows = report.data?.rows ?? []
  const operators = [...new Map(rows.filter((r) => r.operatorId).map((r) => [r.operatorId!, r.operatorName])).entries()]

  const exportCsv = async () => {
    setDownloading(true)
    try {
      await apiDownload(getExportNonFiscalSalesReportUrl(params), `vendas-sem-documento-fiscal-${period.from}-a-${period.to}.csv`)
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
      <PageHeading title="Vendas sem documento fiscal" description="Vendas do balcão concluídas com o comprovante “Não é documento fiscal”, por loja e operador." actions={exportButton} />
      <FormGrid>
        <Field label="De" required size="sm" dica={PERIOD_DICA}>
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
        <Field label="Operador" size="md">
          {(p) => (
            <select {...p} value={operatorId} onChange={(e) => setOperatorId(e.target.value)} className={inputClass}>
              <option value="">Todos os operadores</option>
              {operators.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
            </select>
          )}
        </Field>
      </FormGrid>
      <div className="mt-4">
        {!valid ? <p role="alert" className="text-sm text-crit-ink">A data final tem de ser igual ou depois da inicial.</p>
          : report.isPending ? <Skeleton className="h-48" />
          : report.isError ? <ErrorState title="Não foi possível carregar o relatório." message={report.error instanceof ApiError ? report.error.message : undefined} onRetry={() => report.refetch()} />
          : rows.length === 0 ? (
            <EmptyState title="Nenhuma venda sem documento fiscal no período." description="As vendas do caixa aparecem aqui quando ele sincroniza. Mude o período ou a loja para ver outras." />
          ) : (
            <div className="overflow-x-auto rounded-md border border-border">
              <table className="w-full text-sm">
                <caption className="sr-only">Vendas sem documento fiscal de {formatDate(period.from)} a {formatDate(period.to)}</caption>
                <thead className="bg-muted text-left">
                  <tr><th className="p-2">Loja</th><th className="p-2">Operador</th><th className="p-2 text-right">Vendas</th><th className="p-2 text-right">Valor</th></tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {rows.map((r) => (
                    <tr key={`${r.storeId}-${r.operatorId}`}>
                      <td className="p-2">{r.storeName}</td>
                      <td className="p-2">{r.operatorName}</td>
                      <td className="p-2 text-right tabular-nums">{r.count}</td>
                      <td className="p-2 text-right"><Money value={Number(r.total)} /></td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="border-t-2 border-border font-semibold">
                  <tr>
                    <td className="p-2" colSpan={2}>Total<Dica nome="Total" texto="Soma das vendas concluídas sem NFC-e nem NF-e. Canceladas ficam de fora." /></td>
                    <td className="p-2 text-right tabular-nums">{report.data?.count}</td>
                    <td className="p-2 text-right"><Money value={Number(report.data?.total ?? 0)} /></td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}
      </div>
    </>
  )
}
