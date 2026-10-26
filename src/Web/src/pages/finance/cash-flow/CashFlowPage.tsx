import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { type ReactNode, useState } from 'react'
import { useGetCashFlow, useListBankAccounts } from '@/api/generated/bemaqui'
import type { CashFlowAdjusted, CashFlowResult } from '@/api/generated/model'
import { ToggleGroup } from '@/components/form/choice'
import { inputClass } from '@/components/form/form-utils'
import { Chart } from '@/components/ui/chart'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { adjustedOf, CASH_FLOW_DICAS, chartPoints, chartReferences, chartSeries, chartSummary, HORIZONS, SCENARIOS, type Scenario, VIEWS } from './cash-flow'
import { CashFlowDayPanel } from './CashFlowDayPanel'
import { CashFlowTable } from './CashFlowTable'

const card = 'rounded-md border border-border bg-card p-3 text-sm'

function Highlight({ label, dica, children }: { label: string; dica: string; children: ReactNode }) {
  return (
    <div className={card}>
      <span className="flex items-center text-xs text-muted-foreground">{label}<Dica nome={label} texto={dica} /></span>
      {children}
    </div>
  )
}

/** Destaques (FIN-07 CA 8): menor saldo de cada cenário e a diferença causada pelos atrasos, com "como calculamos". */
function Highlights({ flow, adjusted }: { flow: CashFlowResult; adjusted: CashFlowAdjusted | null }) {
  const point = (p: { date: string; balance: number } | null | undefined) =>
    p ? <><Money value={p.balance} highlightNegative className="text-lg font-semibold" /> <span className="text-muted-foreground">em {formatDate(p.date)}</span></> : <span className="text-muted-foreground">Só no consolidado</span>
  return (
    <section aria-label="Resumo" className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <Highlight label="Saldo atual" dica="Saldo inicial das contas mais tudo o que entrou e saiu até hoje."><Money value={flow.currentBalance} highlightNegative className="block text-lg font-semibold" /></Highlight>
      {adjusted ? (
        <>
          <Highlight label="Menor saldo previsto (ajustado)" dica={`${CASH_FLOW_DICAS.menorSaldo} ${CASH_FLOW_DICAS.ajustado}`}><p>{point(adjusted.lowest)}</p></Highlight>
          <Highlight label="Menor saldo previsto (base)" dica={`${CASH_FLOW_DICAS.menorSaldo} ${CASH_FLOW_DICAS.base}`}><p>{point(flow.lowest)}</p></Highlight>
          <Highlight label="Diferença causada por atrasos" dica={CASH_FLOW_DICAS.diferenca}><Money value={adjusted.lowestDifference ?? 0} className="block text-lg font-semibold" /></Highlight>
        </>
      ) : (
        <Highlight label="Menor saldo previsto" dica={CASH_FLOW_DICAS.menorSaldo}><p>{point(flow.lowest)}</p></Highlight>
      )}
      <Highlight label="A receber sem data (aguardando marco)" dica={CASH_FLOW_DICAS.semData}>
        <p><Money value={flow.undatedReceivables.total} className="text-lg font-semibold" /> <span className="text-muted-foreground">{flow.undatedReceivables.count} {flow.undatedReceivables.count === 1 ? 'título' : 'títulos'}</span></p>
        {flow.undatedReceivables.count > 0 ? <Link to={'/app/financeiro/receber' as never} search={{ situacao: 'previsto' } as never} className="block text-xs font-semibold text-primary hover:underline">Ver os títulos</Link> : null}
      </Highlight>
    </section>
  )
}

/** Risco de não receber (CA 3) e o deslocado para depois do horizonte (CA 5), com o caminho para agir. */
function AdjustedBoxes({ flow, adjusted }: { flow: CashFlowResult; adjusted: CashFlowAdjusted }) {
  return (
    <section aria-label="Efeito dos atrasos" className="grid gap-3 md:grid-cols-2">
      <div className={card}>
        <h2 className="flex items-center text-sm font-semibold">Risco de não receber no período<Dica nome="Risco de não receber" texto={`${CASH_FLOW_DICAS.risco} Limite atual: ${adjusted.thresholdPct.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}%.`} /></h2>
        <Money value={adjusted.atRiskTotal} className="block text-lg font-semibold" />
        {adjusted.atRisk.length === 0 ? <p className="text-xs text-muted-foreground">Nenhum cliente acima do limite de inadimplência.</p> : (
          <ul className="mt-1 divide-y divide-border">
            {adjusted.atRisk.map((c) => (
              <li key={c.partyId} className="flex flex-wrap items-center justify-between gap-2 py-1.5">
                <span className="min-w-0">
                  <span className="block font-medium">{c.name ?? 'Cliente'}</span>
                  <span className="flex items-center text-xs text-muted-foreground">Inadimplência de {c.defaultPct.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}% · {c.count} {c.count === 1 ? 'título' : 'títulos'}<Dica nome="Inadimplência" texto={CASH_FLOW_DICAS.inadimplencia} /></span>
                </span>
                <span className="flex flex-wrap items-center gap-3">
                  <Money value={c.atRisk} className="font-semibold" />
                  <Link to={'/app/financeiro/inadimplencia' as never} className="inline-flex min-h-11 items-center text-xs font-semibold text-primary hover:underline sm:min-h-0">Inadimplência</Link>
                  <Link to={'/app/financeiro/receber' as never} search={{ cliente: c.partyId } as never} className="inline-flex min-h-11 items-center text-xs font-semibold text-primary hover:underline sm:min-h-0">Ver títulos</Link>
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
      <div className={card}>
        <h2 className="text-sm font-semibold">Deslocado para depois de {formatDate(flow.to)}</h2>
        <Money value={adjusted.shiftedBeyond.total} className="block text-lg font-semibold" />
        <p className="text-xs text-muted-foreground">
          {adjusted.shiftedBeyond.count === 0 ? 'Nenhum recebimento sai do período pelo atraso.' : `${adjusted.shiftedBeyond.count} ${adjusted.shiftedBeyond.count === 1 ? 'recebimento vence' : 'recebimentos vencem'} no período, mas, com o atraso de sempre, ${adjusted.shiftedBeyond.count === 1 ? 'entra' : 'entram'} depois.`}
        </p>
        {adjusted.shiftedBeyond.count > 0 ? <Link to={'/app/financeiro/receber' as never} className="inline-flex min-h-11 items-center text-xs font-semibold text-primary hover:underline sm:min-h-0">Ver os títulos</Link> : null}
      </div>
    </section>
  )
}

/** Financeiro → Fluxo de caixa (FIN-05 e FIN-07): realizado, base e ajustado pelo histórico, com o menor saldo e o alerta de caixa negativo. */
export function CashFlowPage() {
  const search = useSearch({ strict: false }) as { visao?: string; horizonte?: string; conta?: string; cenario?: string }
  const navigate = useNavigate()
  const view = VIEWS.find((v) => v.value === search.visao) ?? VIEWS[0]
  const days = HORIZONS.find((h) => String(h) === search.horizonte) ?? 90
  const scenarioOption = SCENARIOS.find((s) => s.value === search.cenario) ?? SCENARIOS[0]
  const scenario: Scenario = scenarioOption.api
  const flow = useGetCashFlow({ view: view.api, days, bankAccountId: search.conta || undefined, scenario })
  const accounts = useListBankAccounts({ pageSize: 100 })
  const [day, setDay] = useState<{ date: string; scenario: 'base' | 'adjusted' } | null>(null)
  const set = (patch: Record<string, string | undefined>) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, ...patch }), replace: true } as never)

  let body
  if (flow.isPending || accounts.isPending) body = <div className="space-y-3"><Skeleton className="h-24" /><Skeleton className="h-64" /><Skeleton className="h-48" /></div>
  else if (flow.isError || accounts.isError) body = <ErrorState title="Não foi possível montar o fluxo de caixa." onRetry={() => { void flow.refetch(); void accounts.refetch() }} />
  else if (accounts.data.items.length === 0) {
    body = <EmptyState title="Sem contas ou títulos ainda." description="Faça a implantação financeira para ver o fluxo." action={<Link to={'/app/financeiro/implantacao' as never} className="inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Implantação financeira</Link>} />
  } else {
    const f = flow.data
    const adjusted = adjustedOf(f, scenario)
    const openAdjusted = (date: string) => setDay({ date, scenario: 'adjusted' })
    body = (
      <div className="space-y-4">
        {adjusted?.firstNegative ? (
          <div role="alert" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
            <span>Considerando os atrasos de sempre, o caixa fica negativo em {formatDate(adjusted.firstNegative.date)}: {formatMoney(adjusted.firstNegative.balance)}.</span>
            <button type="button" onClick={() => openAdjusted(adjusted.firstNegative!.date)} className="min-h-11 font-semibold underline sm:min-h-0">Ver o que entra e sai nesse dia</button>
          </div>
        ) : f.firstNegative && scenario !== 'adjusted' ? (
          <div role="alert" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
            <span>O caixa fica negativo em {formatDate(f.firstNegative.date)}: {formatMoney(f.firstNegative.balance)}.</span>
            <button type="button" onClick={() => setDay({ date: f.firstNegative!.date, scenario: 'base' })} className="min-h-11 font-semibold underline sm:min-h-0">Ver o que vence nesse dia</button>
          </div>
        ) : null}
        <Highlights flow={f} adjusted={adjusted} />
        {f.consolidated ? null : <p role="note" className="text-sm text-muted-foreground">Títulos ainda não têm conta definida; a projeção é consolidada.</p>}
        {adjusted && !adjusted.tenantHasHistory ? <p role="note" className="rounded-md border border-border bg-info-soft px-3 py-2 text-sm text-info-ink">Ainda não há histórico de pagamento: o ajustado é igual à base.</p> : null}
        {f.consolidated ? (
          <div className="flex flex-wrap items-center gap-2">
            <span className="flex items-center text-sm font-medium">Cenário<Dica nome="Ajustado" texto={`${CASH_FLOW_DICAS.base} ${CASH_FLOW_DICAS.ajustado}`} /></span>
            <ToggleGroup label="Cenário" value={scenarioOption.value} onChange={(v) => set({ cenario: v === 'os-dois' ? undefined : v })} options={SCENARIOS.map((s) => ({ value: s.value, label: s.label }))} />
            {adjusted ? <span className="flex items-center text-xs text-muted-foreground">Atraso médio de cada cliente<Dica nome="Atraso médio" texto={CASH_FLOW_DICAS.atrasoMedio} /> · média da empresa: {adjusted.tenantAvgDelayDays.toLocaleString('pt-BR')} dias<Dica nome="Média da empresa" texto={CASH_FLOW_DICAS.mediaEmpresa} /></span> : null}
          </div>
        ) : null}
        <Chart title="Saldo do realizado ao projetado" data={chartPoints(f.periods, f.view, adjusted)} xKey="periodo" xLabel="Período" format={(v) => formatMoney(v, false)}
          series={chartSeries(scenario, adjusted !== null)} references={chartReferences(f, adjusted)} yReferences={[{ y: 0, label: 'Saldo zero' }]} summary={chartSummary(f, adjusted)} />
        {adjusted ? <AdjustedBoxes flow={f} adjusted={adjusted} /> : null}
        <CashFlowTable flow={f} adjusted={adjusted} scenario={scenario} onOpenDay={(date) => setDay({ date, scenario: adjusted ? 'adjusted' : 'base' })} />
        <p className="text-xs text-muted-foreground">Folha e pedidos de compra entram quando os módulos forem ativados.</p>
      </div>
    )
  }

  return (
    <>
      <PageHeading title="Financeiro: Fluxo de caixa">
        <p className="mt-2 text-muted-foreground">Entradas, saídas e saldo, do realizado ao previsto, na data combinada e com o atraso de sempre de cada cliente.</p>
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
      {day ? <CashFlowDayPanel key={`${day.date}-${day.scenario}`} date={day.date} scenario={day.scenario} canAdjust={!search.conta && (flow.data?.adjusted ?? null) !== null} bankAccountId={search.conta || undefined} onClose={() => setDay(null)} /> : null}
    </>
  )
}
