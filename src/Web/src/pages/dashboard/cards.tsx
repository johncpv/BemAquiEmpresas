import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import {
  useGetDashboardBalances, useGetDashboardCashForecast, useGetDashboardCommercial, useGetDashboardDecisions, useGetDashboardOverdue, useGetDashboardRevenue,
} from '@/api/generated/bemaqui'
import { ToggleGroup } from '@/components/form/choice'
import { Chart, type ChartSeries } from '@/components/ui/chart'
import { DashboardCard } from '@/components/ui/dashboard-card'
import { Kpi } from '@/components/ui/kpi'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { barWidth, pctLabel } from '@/pages/quotes/funnel/funnel'
import { QUOTE_STATUS } from '@/pages/quotes/quotes'
import {
  cashFlowHref, cashPoints, HOW, linkParts, meterWidth, monthOf, plural, REVENUE_ORIGIN, type Scenario, SCENARIOS, scenarioOf, scenarioParam, workingDaysLabel,
} from './dashboard'
import { useGo } from './use-go'

export const primaryAction = 'inline-flex min-h-[44px] items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground sm:min-h-0'
const BUCKETS = ['0–30 dias', '31–60 dias', '61–90 dias', 'Mais de 90 dias']

/**
 * Card do painel sobre o `DashboardCard` da DSH-02: `data-card` e a ordem a 400 px no invólucro; "Atualizado às" pela hora em que o
 * servidor calculou o número (cache de 60 s, CA 12).
 */
export function PanelCard<T extends { updatedAt: string }>({ code, className, query, ...props }: {
  code: string
  className?: string
  title: string
  subtitle?: string
  howWeCalculate: string
  origin?: string
  query: { data?: T; isPending: boolean; isError: boolean; refetch: () => unknown }
  empty?: (data: T) => { title: string; description?: string; action?: ReactNode } | null
  children: (data: T) => ReactNode
}) {
  const go = useGo()
  const empty = query.data && props.empty ? props.empty(query.data) : null
  return (
    <div data-card={code} className={className}>
      <DashboardCard title={props.title} subtitle={props.subtitle} howWeCalculate={props.howWeCalculate} className="h-full"
        onOrigin={props.origin ? () => go(props.origin!) : undefined} updatedAt={query.data ? Date.parse(query.data.updatedAt) : undefined}
        query={{ isPending: query.isPending, isError: query.isError || (!query.isPending && !query.data), refetch: query.refetch }}
        empty={empty ? { when: true, ...empty } : undefined}>
        {query.data ? props.children(query.data) : null}
      </DashboardCard>
    </div>
  )
}

/** Saldo em contas (FIN-05): consolidado e por conta; o número leva a Contas e caixas. */
export function BalancesKpi({ className }: { className?: string }) {
  const query = useGetDashboardBalances()
  const go = useGo()
  return (
    <PanelCard code="balances" title="Saldo em contas" howWeCalculate={HOW.balances} origin="/app/financeiro/contas" query={query} className={className}
      empty={(card) => (card.accounts.length === 0 ? { title: 'Sem contas ainda.', description: 'Comece pela implantação financeira.', action: <Link to={'/app/financeiro/implantacao' as never} className={primaryAction}>Implantação financeira</Link> } : null)}>
      {(card) => (
        <Kpi label="Saldo de hoje" value={<Money value={card.consolidated} cents={false} highlightNegative />} onClick={() => go('/app/financeiro/contas')} actionLabel="Saldo em contas: ver origem"
          context={<>
            {card.accounts.map((a) => `${a.name} ${formatMoney(a.balance, false)}`).join(', ')}
            {card.openingMissing ? <span className="block text-warn-ink">Há conta sem saldo inicial: o saldo pode estar incompleto.</span> : null}
          </>} />
      )}
    </PanelCard>
  )
}

/** Faturado no mês × meta (FIN-08 e CA 9): medidor, chip "Provisório" e "Definir meta" para quem tem settings.edit. */
export function RevenueKpi({ className }: { className?: string }) {
  const query = useGetDashboardRevenue()
  const go = useGo()
  const title = query.data ? `Faturado em ${monthOf(query.data.month)}` : 'Faturado no mês'
  const define = <Link to={'/app/configuracoes/parametros' as never} hash="metas-de-faturamento" className="font-semibold text-primary hover:underline">Definir meta</Link>
  return (
    <PanelCard code="revenue" title={title} howWeCalculate={HOW.revenue} origin={REVENUE_ORIGIN} query={query} className={className}>
      {(card) => {
        const target = card.monthTarget
        const chip = card.provisional ? <span className="mt-1 inline-flex items-center"><StatusChip tone="info">Provisório</StatusChip><Dica nome="Provisório" texto={HOW.provisional} /></span> : null
        return (
          <div className="space-y-2">
            <Kpi label="Receita bruta do mês" onClick={() => go(REVENUE_ORIGIN)} actionLabel={`${title}: ver origem`}
              value={<><Money value={card.monthAmount ?? 0} cents={false} />{typeof target === 'number' ? <small className="ml-1 text-sm font-medium text-muted-foreground">/ {formatMoney(target, false)}</small> : null}</>}
              meter={typeof card.monthPct === 'number' ? { value: meterWidth(card.monthPct), max: 100, label: `${pctLabel(card.monthPct)} da meta`, tone: card.monthPct >= 100 ? 'ok' : 'info' } : undefined}
              context={typeof target === 'number'
                ? <>{pctLabel(card.monthPct)} da meta, {workingDaysLabel(card.workingDaysLeft)}. No ano: {formatMoney(card.yearAmount ?? 0, false)}{typeof card.yearTarget === 'number' ? ` de ${formatMoney(card.yearTarget, false)}` : ''}.</>
                : <>Sem meta para o mês. No ano: {formatMoney(card.yearAmount ?? 0, false)}.</>} />
            {chip}
            {typeof target !== 'number' && card.canEditTargets ? <p className="text-sm">{define}</p> : null}
          </div>
        )
      }}
    </PanelCard>
  )
}

/** A receber vencido (FIN-06a): total, faixas, os 5 maiores clientes e o maior atraso; leva à Inadimplência. */
export function OverdueKpi({ className }: { className?: string }) {
  const query = useGetDashboardOverdue()
  const go = useGo()
  return (
    <PanelCard code="overdue" title="A receber vencido" howWeCalculate={HOW.overdue} origin="/app/financeiro/inadimplencia" query={query} className={className}
      empty={(card) => (card.count === 0 ? { title: 'Nenhum título vencido. Bom sinal.', action: <Link to={'/app/financeiro/receber' as never} className={primaryAction}>Ver a receber</Link> } : null)}>
      {(card) => (
        <div className="space-y-3">
          <Kpi label="Total vencido" value={<Money value={card.total} cents={false} />} onClick={() => go('/app/financeiro/inadimplencia')} actionLabel="A receber vencido: ver origem"
            context={`${plural(card.customers, 'cliente', 'clientes')}, maior atraso ${plural(card.maxDaysLate, 'dia', 'dias')}`} />
          <ul aria-label="Faixas de atraso" className="grid grid-cols-2 gap-1.5 text-xs">
            {card.buckets.map((b, i) => (
              <li key={b.label} className="rounded-sm bg-muted px-2 py-1">
                <span className="block text-muted-foreground">{BUCKETS[i] ?? b.label}</span>
                <Money value={b.total} cents={false} className="font-semibold" />
              </li>
            ))}
          </ul>
          <details className="text-sm">
            <summary className="inline-flex min-h-[44px] cursor-pointer items-center font-semibold text-primary sm:min-h-0">{plural(card.topCustomers.length, 'maior cliente', 'maiores clientes')}</summary>
            <ol className="mt-1 space-y-1">
              {card.topCustomers.map((c) => (
                <li key={c.partyId} className="flex justify-between gap-2">
                  <Link to={`/app/financeiro/receber?situacao=vencido&cliente=${c.partyId}` as never} className="truncate text-primary hover:underline">{c.partyName || 'Cliente'}</Link>
                  <Money value={c.total} cents={false} />
                </li>
              ))}
            </ol>
          </details>
        </div>
      )}
    </PanelCard>
  )
}

/** Caixa projetado das próximas 12 semanas (FIN-07): Os dois / Base / Ajustado na URL, no `Chart` da FIN-07 com "Saldo zero". */
export function CashForecastPanel({ className }: { className?: string }) {
  const query = useGetDashboardCashForecast()
  const search = useSearch({ strict: false }) as { cenario?: string }
  const navigate = useNavigate()
  const scenario = scenarioOf(search.cenario)
  const choose = (value: Scenario) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, cenario: scenarioParam(value) }), replace: true } as never)
  const series: ChartSeries[] = [
    ...(scenario !== 'adjusted' ? [{ key: 'base', label: 'Base', kind: 'base' as const, color: 2 }] : []),
    ...(scenario !== 'base' ? [{ key: 'ajustado', label: 'Ajustado', kind: 'adjusted' as const, color: 1 }] : []),
  ]
  return (
    <PanelCard code="cash-forecast" title="Caixa projetado, próximas 12 semanas" subtitle="Saldo no fim de cada semana" howWeCalculate={HOW.cash} origin={cashFlowHref(scenario)} query={query} className={className}
      empty={(card) => (card.weeks.length === 0 ? { title: 'Sem projeção para as próximas semanas.', description: 'Lance os títulos a receber e a pagar para ver o caixa.', action: <Link to={'/app/financeiro/fluxo-de-caixa' as never} className={primaryAction}>Abrir o fluxo de caixa</Link> } : null)}>
      {(card) => {
        const negative = card.firstAdjustedNegative
        const summary = negative
          ? <>No cenário ajustado, o caixa fica <b className="text-crit-ink">negativo em {formatDate(negative.date)}</b> ({formatMoney(negative.balance, false)}). Hoje: {formatMoney(card.currentBalance ?? 0, false)}.</>
          : <>O caixa fica positivo nas próximas 12 semanas no cenário ajustado. Hoje: {formatMoney(card.currentBalance ?? 0, false)}.</>
        return (
          <div className="space-y-3">
            <ToggleGroup label="Cenário" options={SCENARIOS} value={scenario} onChange={choose} />
            <Chart title="Saldo projetado no fim de cada semana" data={cashPoints(card.weeks, scenario)} xKey="semana" xLabel="Semana até" series={series}
              format={(v) => formatMoney(v, false)} yReferences={[{ y: 0, label: 'Saldo zero' }]} summary={summary} height={240} />
            <dl className="grid gap-2 text-sm sm:grid-cols-3">
              {card.lowestAdjusted ? <div className="rounded-sm bg-muted p-2"><dt className="text-xs text-muted-foreground">Menor saldo previsto (ajustado)</dt><dd className="font-semibold tabular-nums"><span className={card.lowestAdjusted.balance < 0 ? 'text-crit-ink' : 'text-warn-ink'}>{formatMoney(card.lowestAdjusted.balance, false)}</span> em {formatDate(card.lowestAdjusted.date)}</dd></div> : null}
              {card.lowestBase ? <div className="rounded-sm bg-muted p-2"><dt className="text-xs text-muted-foreground">Menor saldo previsto (base)</dt><dd className="font-semibold tabular-nums">{formatMoney(card.lowestBase.balance, false)} em {formatDate(card.lowestBase.date)}</dd></div> : null}
              {typeof card.difference === 'number' ? <div className="rounded-sm bg-muted p-2"><dt className="text-xs text-muted-foreground">Diferença causada por atrasos</dt><dd className="font-semibold tabular-nums">{formatMoney(card.difference, false)}</dd></div> : null}
            </dl>
            {card.horizons.length > 0 ? (
              <table className="w-full text-sm">
                <caption className="sr-only">Saldo final em 30, 60 e 90 dias</caption>
                <thead><tr className="text-left text-xs text-muted-foreground"><th scope="col">Saldo final</th><th scope="col" className="text-right">Base</th><th scope="col" className="text-right">Ajustado</th></tr></thead>
                <tbody>{card.horizons.map((h) => <tr key={h.days} className="border-t border-border"><th scope="row" className="py-1 text-left font-normal">Em {h.days} dias</th><td className="text-right tabular-nums">{formatMoney(h.baseBalance, false)}</td><td className="text-right tabular-nums">{formatMoney(h.adjustedBalance, false)}</td></tr>)}</tbody>
              </table>
            ) : null}
          </div>
        )
      }}
    </PanelCard>
  )
}

/** "Aguardando sua decisão" (DSH-04): o nível 1 da pauta; cada item leva à ação, onde a decisão é tomada. */
export function DecisionsPanel({ className }: { className?: string }) {
  const query = useGetDashboardDecisions()
  return (
    <PanelCard code="decisions" title="Aguardando sua decisão" howWeCalculate={HOW.decisions} query={query} className={className}
      empty={(card) => (card.total === 0 ? { title: 'Nada esperando a sua decisão agora.', description: 'Quando uma ordem de pagamento, um aditivo ou um pedido precisar de você, ele aparece aqui.' } : null)}>
      {(card) => (
        <div className="space-y-2">
          <p className="text-sm text-muted-foreground">{plural(card.total, 'item', 'itens')}, <Money value={card.totalAmount} cents={false} /> no total{card.firstDue ? `; o primeiro vence em ${formatDate(card.firstDue)}` : ''}.</p>
          <ul className="divide-y divide-border rounded-md border border-border">
            {card.items.map((item) => (
              <li key={item.kind + item.url}>
                <Link to={linkParts(item.url).to as never} search={linkParts(item.url).search as never} className="flex min-h-[44px] items-center justify-between gap-2 px-3 py-2 text-sm hover:bg-muted">
                  <span className="min-w-0"><b className="block truncate">{item.title}</b>{item.detail ? <span className="block truncate text-xs text-muted-foreground">{item.detail}</span> : null}</span>
                  <span className="flex-none text-right text-xs">{typeof item.amount === 'number' ? <Money value={item.amount} cents={false} className="block text-sm font-semibold" /> : null}{item.dueDate ? `vence ${formatDate(item.dueDate)}` : null}</span>
                </Link>
              </li>
            ))}
          </ul>
        </div>
      )}
    </PanelCard>
  )
}

/** Comercial (COM-08): em aberto, conversão, vencendo em 7 dias e as barras do funil, cada uma até a lista de orçamentos. */
export function CommercialPanel({ className }: { className?: string }) {
  const query = useGetDashboardCommercial()
  return (
    <PanelCard code="commercial" title="Comercial" subtitle="Orçamentos dos últimos 90 dias" howWeCalculate={HOW.commercial} origin="/app/orcamentos/funil" query={query} className={className}
      empty={(card) => (card.stages.every((s) => s.count === 0) ? { title: 'Sem orçamentos nos últimos 90 dias.', action: <Link to={'/app/orcamentos' as never} className={primaryAction}>Ver orçamentos</Link> } : null)}>
      {(card) => {
        const max = Math.max(...card.stages.map((s) => s.count), 0)
        return (
          <div className="space-y-4">
            <dl className="grid grid-cols-3 gap-2 text-sm">
              <div><dt className="text-xs text-muted-foreground">Em aberto</dt><dd><Link to={'/app/orcamentos/funil' as never} className="font-semibold tabular-nums hover:underline"><Money value={card.openAmount} cents={false} /></Link><span className="block text-xs text-muted-foreground">{plural(card.openCount, 'orçamento', 'orçamentos')}</span></dd></div>
              <div><dt className="text-xs text-muted-foreground">Conversão</dt><dd className="font-semibold tabular-nums">{pctLabel(card.conversionByCount)}</dd></div>
              <div><dt className="text-xs text-muted-foreground">Vencem em 7 dias</dt><dd className={card.expiringCount > 0 ? 'font-semibold tabular-nums text-warn-ink' : 'font-semibold tabular-nums'}>{card.expiringCount}</dd></div>
            </dl>
            <ul aria-label="Funil" className="space-y-1.5">
              {card.stages.map((s, i) => (
                <li key={s.status}>
                  <Link to={'/app/orcamentos' as never} search={{ situacao: s.situation, de: card.from, ate: card.to } as never} className="block rounded-sm p-1 hover:bg-muted">
                    <span className="flex justify-between gap-2 text-sm"><span>{QUOTE_STATUS[s.status]?.label ?? s.status}</span><span className="tabular-nums"><b>{s.count}</b> · <Money value={s.amount} cents={false} /></span></span>
                    <span aria-hidden="true" className="mt-1 block h-2 rounded-sm bg-muted"><span className="block h-2 rounded-sm" style={{ width: `${barWidth(s.count, max)}%`, background: `var(--chart-${(i % 6) + 1})` }} /></span>
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        )
      }}
    </PanelCard>
  )
}
