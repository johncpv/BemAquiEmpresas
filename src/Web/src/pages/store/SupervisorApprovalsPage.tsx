import { Download } from 'lucide-react'
import { useState } from 'react'
import { getExportSupervisorApprovalsUrl, useGetBelowCostSalesReport, useGetSupervisorApprovals, useListStores } from '@/api/generated/bemaqui'
import type { UseQueryResult } from '@tanstack/react-query'
import type { BelowCostSalesReport, SupervisorApprovalGroup } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifyError } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'

/** Hoje (em São Paulo) e o primeiro dia do mês, no formato da API. */
function defaultPeriod(now = new Date()): { from: string; to: string } {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now)
  return { from: `${today.slice(0, 8)}01`, to: today }
}

const ACTIONS = [
  ['', 'Todos os tipos'], ['Discount', 'Desconto acima do limite'], ['ChangePrice', 'Troca de preço'], ['CancelItem', 'Cancelamento de item'], ['CancelSale', 'Cancelamento de venda'],
] as const

const dateTime = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })

function Totals({ title, groups }: { title: string; groups: readonly SupervisorApprovalGroup[] }) {
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
 * Loja → Aprovações de supervisor (PDV-04 CA 13): as aprovações por PIN de supervisor no balcão, por período, loja e tipo, com os totais por
 * operador, por supervisor e por tipo, e a exportação. Com <c>sales.view_cost</c>, também as vendas abaixo do custo (CA 4). Carregando, vazio
 * ("Nenhuma aprovação no período.") e erro.
 */
export function SupervisorApprovalsPage() {
  const [period, setPeriod] = useState(defaultPeriod)
  const [storeId, setStoreId] = useState('')
  const [action, setAction] = useState('')
  const stores = useListStores()
  const canSeeCost = useCan('sales.view_cost')
  const params = { From: period.from, To: period.to, StoreId: storeId || undefined, Action: (action || undefined) as 'Discount' | undefined }
  const valid = !!period.from && !!period.to && period.from <= period.to
  const report = useGetSupervisorApprovals(params, { query: { enabled: valid } })
  const belowCost = useGetBelowCostSalesReport({ from: period.from, to: period.to, storeId: storeId || undefined }, { query: { enabled: valid && canSeeCost } })
  const [downloading, setDownloading] = useState(false)
  const rows = report.data?.rows ?? []

  const exportCsv = async () => {
    setDownloading(true)
    try {
      await apiDownload(getExportSupervisorApprovalsUrl(params), `aprovacoes-de-supervisor-${period.from}-a-${period.to}.csv`)
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
      <PageHeading title="Aprovações de supervisor" description="Descontos acima do limite, trocas de preço e cancelamentos aprovados com o PIN de um supervisor no caixa." actions={exportButton} />
      <FormGrid>
        <Field label="De" required size="sm" dica="Data da aprovação, no horário de Brasília. Escolha até um ano por vez.">
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
        <Field label="Tipo" size="md">
          {(p) => (
            <select {...p} value={action} onChange={(e) => setAction(e.target.value)} className={inputClass}>
              {ACTIONS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          )}
        </Field>
      </FormGrid>
      <div className="mt-4 space-y-4">
        {!valid ? <p role="alert" className="text-sm text-crit-ink">A data final tem de ser igual ou depois da inicial.</p>
          : report.isPending ? <Skeleton className="h-48" />
          : report.isError ? <ErrorState title="Não foi possível carregar as aprovações." message={report.error instanceof ApiError ? report.error.message : undefined} onRetry={() => report.refetch()} />
          : rows.length === 0 ? (
            <EmptyState title="Nenhuma aprovação no período." description="As aprovações aparecem aqui quando um supervisor digita o PIN no caixa. Mude o período, a loja ou o tipo para ver outras." />
          ) : (
            <>
              <div className="grid gap-3 md:grid-cols-3">
                <Totals title="Por operador" groups={report.data?.byOperator ?? []} />
                <Totals title="Por supervisor" groups={report.data?.bySupervisor ?? []} />
                <Totals title="Por tipo" groups={report.data?.byAction ?? []} />
              </div>
              {report.data?.truncated ? <p className="text-sm text-muted-foreground">Mostrando as 2.000 mais recentes. Diminua o período para ver todas.</p> : null}
              <div className="overflow-x-auto rounded-md border border-border">
                <table className="w-full text-sm">
                  <caption className="sr-only">Aprovações de supervisor de {formatDate(period.from)} a {formatDate(period.to)}</caption>
                  <thead className="bg-muted text-left">
                    <tr>
                      <th className="p-2">Quando</th><th className="p-2">Loja e caixa</th><th className="p-2">Tipo</th><th className="p-2">Operador</th><th className="p-2">Supervisor</th>
                      <th className="p-2">Motivo</th><th className="p-2 text-right">Antes</th><th className="p-2 text-right">Depois</th>
                      <th className="p-2">Usada<Dica nome="Usada" texto="Sim: a venda com a ação aprovada chegou ao servidor. Não: o caixa ainda não sincronizou, ou a ação não foi feita." /></th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {rows.map((r) => (
                      <tr key={r.id}>
                        <td className="p-2 tabular-nums">{dateTime(r.approvedAt)}</td>
                        <td className="p-2">{r.storeName} · {String(r.registerNumber).padStart(2, '0')}</td>
                        <td className="p-2">{r.actionLabel}</td>
                        <td className="p-2">{r.operatorName}</td>
                        <td className="p-2">{r.supervisorName}</td>
                        <td className="p-2">{r.reason}{r.refundReference ? ` · NSU do estorno ${r.refundReference}` : ''}</td>
                        <td className="p-2 text-right"><Money value={Number(r.beforeAmount)} /></td>
                        <td className="p-2 text-right"><Money value={Number(r.afterAmount)} /></td>
                        <td className="p-2">{r.used ? 'Sim' : 'Não'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        {valid && canSeeCost ? <BelowCost query={belowCost} /> : null}
      </div>
    </>
  )
}

/** CA 4: vendas abaixo do custo (o valor líquido do desconto por unidade abaixo do custo da baixa). Só com `sales.view_cost`. */
function BelowCost({ query }: { query: UseQueryResult<BelowCostSalesReport, unknown> }) {
  const rows = query.data?.rows ?? []
  return (
    <section aria-labelledby="below-cost" className="space-y-2">
      <h2 id="below-cost" className="text-base font-semibold">
        Vendas abaixo do custo<Dica nome="Vendas abaixo do custo" texto="Linhas vendidas, já com o desconto, por menos que o custo médio da baixa do estoque. O custo nunca vai para o caixa." />
      </h2>
      {query.isPending ? <Skeleton className="h-24" />
        : query.isError ? <ErrorState title="Não foi possível carregar as vendas abaixo do custo." onRetry={() => query.refetch()} />
        : rows.length === 0 ? <EmptyState title="Nenhuma venda abaixo do custo no período." description="Quando um desconto ou uma troca de preço deixar o item abaixo do custo, a venda aparece aqui." />
        : (
          <div className="overflow-x-auto rounded-md border border-border">
            <table className="w-full text-sm">
              <thead className="bg-muted text-left">
                <tr>
                  <th className="p-2">Venda</th><th className="p-2">Loja</th><th className="p-2">Item</th><th className="p-2 text-right">Preço líquido</th><th className="p-2 text-right">Custo</th>
                  <th className="p-2 text-right">Perda</th><th className="p-2">Motivo</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {rows.map((r) => (
                  <tr key={`${r.saleId}-${r.description}-${r.netUnitPrice}`}>
                    <td className="p-2 tabular-nums">{r.saleNumber} · {formatDate(r.businessDate)}</td>
                    <td className="p-2">{r.storeName}</td>
                    <td className="p-2">{r.description}</td>
                    <td className="p-2 text-right"><Money value={Number(r.netUnitPrice)} /></td>
                    <td className="p-2 text-right"><Money value={Number(r.unitCost)} /></td>
                    <td className="p-2 text-right"><Money value={Number(r.loss)} /></td>
                    <td className="p-2">{r.discountReason ?? r.priceReason ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot className="border-t-2 border-border font-semibold">
                <tr><td className="p-2" colSpan={5}>Perda no período</td><td className="p-2 text-right"><Money value={Number(query.data?.loss ?? 0)} /></td><td /></tr>
              </tfoot>
            </table>
          </div>
        )}
    </section>
  )
}
