import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { useGetCashFlow, useListBankAccounts } from '@/api/generated/bemaqui'
import { inputClass } from '@/components/form/form-utils'
import { Chart } from '@/components/ui/chart'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { CASH_FLOW_DICAS, chartPoints, chartReferences, HORIZONS, VIEWS } from './cash-flow'
import { CashFlowDayPanel } from './CashFlowDayPanel'
import { CashFlowTable } from './CashFlowTable'

/** Financeiro → Fluxo de caixa (FIN-05): realizado e projetado, com o menor saldo e o alerta de caixa negativo. */
export function CashFlowPage() {
  const search = useSearch({ strict: false }) as { visao?: string; horizonte?: string; conta?: string }
  const navigate = useNavigate()
  const view = VIEWS.find((v) => v.value === search.visao) ?? VIEWS[0]
  const days = HORIZONS.find((h) => String(h) === search.horizonte) ?? 90
  const flow = useGetCashFlow({ view: view.api, days, bankAccountId: search.conta || undefined })
  const accounts = useListBankAccounts({ pageSize: 100 })
  const [day, setDay] = useState<string | null>(null)
  const set = (patch: Record<string, string | undefined>) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, ...patch }), replace: true } as never)

  let body
  if (flow.isPending || accounts.isPending) body = <div className="space-y-3"><Skeleton className="h-64" /><Skeleton className="h-48" /></div>
  else if (flow.isError || accounts.isError) body = <ErrorState title="Não foi possível montar o fluxo de caixa." onRetry={() => { void flow.refetch(); void accounts.refetch() }} />
  else if (accounts.data.items.length === 0) {
    body = <EmptyState title="Sem contas ou títulos ainda." description="Faça a implantação financeira para ver o fluxo." action={<Link to={'/app/financeiro/implantacao' as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Implantação financeira</Link>} />
  } else {
    const f = flow.data
    body = (
      <div className="space-y-4">
        {f.firstNegative ? (
          <div role="alert" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
            <span>O caixa fica negativo em {formatDate(f.firstNegative.date)}: {formatMoney(f.firstNegative.balance)}.</span>
            <button type="button" onClick={() => setDay(f.firstNegative?.date ?? null)} className="font-semibold underline">Ver o que vence nesse dia</button>
          </div>
        ) : null}
        <section aria-label="Resumo" className="grid gap-3 sm:grid-cols-3">
          <p className="rounded-md border border-border bg-card p-3 text-sm"><span className="block text-xs text-muted-foreground">Saldo atual</span><Money value={f.currentBalance} highlightNegative className="text-lg font-semibold" /></p>
          <p className="rounded-md border border-border bg-card p-3 text-sm">
            <span className="block text-xs text-muted-foreground">Menor saldo previsto</span>
            {f.lowest ? <><Money value={f.lowest.balance} highlightNegative className="text-lg font-semibold" /> <span className="text-muted-foreground">em {formatDate(f.lowest.date)}</span></> : <span className="text-muted-foreground">Só no consolidado</span>}
          </p>
          <p className="rounded-md border border-border bg-card p-3 text-sm">
            <span className="flex items-center text-xs text-muted-foreground">A receber sem data (aguardando marco)<Dica nome="A receber sem data" texto={CASH_FLOW_DICAS.semData} /></span>
            <Money value={f.undatedReceivables.total} className="text-lg font-semibold" /> <span className="text-muted-foreground">{f.undatedReceivables.count} {f.undatedReceivables.count === 1 ? 'título' : 'títulos'}</span>
            {f.undatedReceivables.count > 0 ? <Link to={'/app/financeiro/receber' as never} search={{ situacao: 'previsto' } as never} className="block text-xs font-semibold text-primary hover:underline">Ver os títulos</Link> : null}
          </p>
        </section>
        {f.consolidated ? null : <p role="note" className="text-sm text-muted-foreground">Títulos ainda não têm conta definida; a projeção é consolidada.</p>}
        <Chart title="Saldo do realizado ao projetado" data={chartPoints(f.periods, f.view)} xKey="periodo" xLabel="Período" format={(v) => formatMoney(v, false)}
          series={[{ key: 'realizado', label: 'Realizado' }, { key: 'projetado', label: 'Projetado', kind: 'projected' }]} references={chartReferences(f)} />
        <CashFlowTable flow={f} onOpenDay={setDay} />
        <p className="text-xs text-muted-foreground">Folha e pedidos de compra entram quando os módulos forem ativados.</p>
      </div>
    )
  }

  return (
    <>
      <PageHeading title="Financeiro: Fluxo de caixa">
        <p className="mt-2 text-muted-foreground">Entradas, saídas e saldo, do realizado ao previsto.</p>
      </PageHeading>
      <FinanceTabs />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="space-y-1 text-sm font-medium">Visão
          <select value={view.value} onChange={(e) => set({ visao: e.target.value === 'diaria' ? undefined : e.target.value })} className={inputClass}>{VIEWS.map((v) => <option key={v.value} value={v.value}>{v.label}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium">Horizonte
          <select value={days} onChange={(e) => set({ horizonte: e.target.value === '90' ? undefined : e.target.value })} className={inputClass}>{HORIZONS.map((h) => <option key={h} value={h}>{h} dias</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium">Conta
          <select value={search.conta ?? ''} onChange={(e) => set({ conta: e.target.value || undefined })} className={inputClass}>
            <option value="">Todas (consolidado)</option>
            {(accounts.data?.items ?? []).filter((a) => a.isActive).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select>
        </label>
      </div>
      {body}
      {day ? <CashFlowDayPanel date={day} bankAccountId={search.conta || undefined} onClose={() => setDay(null)} /> : null}
    </>
  )
}
