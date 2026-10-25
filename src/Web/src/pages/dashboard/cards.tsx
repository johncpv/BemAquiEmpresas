import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import {
  useGetDashboardBalances, useGetDashboardCashForecast, useGetDashboardCommercial, useGetDashboardDecisions, useGetDashboardOverdue, useGetDashboardRevenue,
} from '@/api/generated/bemaqui'
import { ToggleGroup } from '@/components/form/choice'
import { Money } from '@/components/ui/money'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { barWidth, pctLabel } from '@/pages/quotes/funnel/funnel'
import { QUOTE_STATUS } from '@/pages/quotes/quotes'
import { CashChart } from './CashChart'
import { DashboardCard, Kpi, OriginLink } from './DashboardCard'
import { cashFlowHref, HOW, meterWidth, monthOf, plural, type Scenario, SCENARIOS, scenarioOf, scenarioParam, workingDaysLabel } from './dashboard'

const primaryAction = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground sm:min-h-0'
const BUCKETS = ['0–30 dias', '31–60 dias', '61–90 dias', 'Mais de 90 dias']

function Empty({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return <EmptyState className="py-6" title={title} description={description} action={action} />
}

/** Saldo em contas (FIN-05): consolidado e por conta; "Ver origem" leva a Contas e caixas. */
export function BalancesKpi({ className }: { className?: string }) {
  const query = useGetDashboardBalances()
  return (
    <DashboardCard code="balances" title="Saldo em contas" how={HOW.balances} query={query} compact className={className}>
      {(card) => card.accounts.length === 0 ? (
        <Empty title="Sem contas ainda." description="Comece pela implantação financeira." action={<Link to={'/app/financeiro/implantacao' as never} className={primaryAction}>Implantação financeira</Link>} />
      ) : (
        <Kpi label="Saldo em contas" href="/app/financeiro/contas" value={<Money value={card.consolidated} cents={false} highlightNegative />}
          sub={<>
            <span>{card.accounts.map((a) => `${a.name} ${formatMoney(a.balance, false)}`).join(', ')}</span>
            {card.openingMissing ? <span className="block text-warn-ink">Há conta sem saldo inicial: o saldo pode estar incompleto.</span> : null}
          </>} />
      )}
    </DashboardCard>
  )
}

/** Faturado no mês × meta (FIN-08 e CA 9): medidor, chip "Provisório" e "Definir meta" para quem tem settings.edit. */
export function RevenueKpi({ className }: { className?: string }) {
  const query = useGetDashboardRevenue()
  const define = <Link to={'/app/configuracoes/parametros' as never} hash="metas-de-faturamento" className="font-semibold text-primary hover:underline">Definir meta</Link>
  return (
    <DashboardCard code="revenue" title={query.data ? `Faturado em ${monthOf(query.data.month)}` : 'Faturado no mês'} how={HOW.revenue} query={query} compact className={className}>
      {(card) => {
        const target = card.monthTarget
        const chip = card.provisional ? <span className="inline-flex items-center"><StatusChip tone="info">Provisório</StatusChip><Dica nome="Provisório" texto={HOW.provisional} /></span> : null
        if (!card.available) {
          return (
            <Kpi label="Faturado no mês" value="—"
              sub={<>
                <span className="block">O faturado do mês aparece aqui quando o resumo de faturamento for ativado.</span>
                {typeof target === 'number' ? <span className="block">Meta do mês: <Money value={target} cents={false} /></span> : card.canEditTargets ? define : null}
              </>} />
          )
        }

        const amount = card.monthAmount ?? 0
        return (
          <Kpi label={`Faturado em ${monthOf(card.month)}`} href="/app/financeiro/dre?linha=receita-bruta" chip={chip}
            value={<><Money value={amount} cents={false} />{typeof target === 'number' ? <small className="ml-1 text-sm font-medium text-muted-foreground">/ {formatMoney(target, false)}</small> : null}</>}
            meter={typeof card.monthPct === 'number' ? { pct: meterWidth(card.monthPct), label: `${pctLabel(card.monthPct)} da meta` } : undefined}
            sub={typeof target === 'number'
              ? <span>{pctLabel(card.monthPct)} da meta, {workingDaysLabel(card.workingDaysLeft)}. No ano: <Money value={card.yearAmount ?? 0} cents={false} />{typeof card.yearTarget === 'number' ? ` de ${formatMoney(card.yearTarget, false)}` : ''}.</span>
              : <span>Sem meta para o mês. {card.canEditTargets ? define : null}</span>} />
        )
      }}
    </DashboardCard>
  )
}

/** A receber vencido (FIN-06a): total, faixas, os 5 maiores clientes e o maior atraso; "Ver origem" leva à Inadimplência. */
export function OverdueKpi({ className }: { className?: string }) {
  const query = useGetDashboardOverdue()
  return (
    <DashboardCard code="overdue" title="A receber vencido" how={HOW.overdue} query={query} compact className={className}>
      {(card) => card.count === 0 ? (
        <Empty title="Nenhum título vencido. Bom sinal." action={<OriginLink href="/app/financeiro/receber">Ver a receber</OriginLink>} />
      ) : (
        <div className="space-y-3">
          <Kpi label="A receber vencido" href="/app/financeiro/inadimplencia" value={<Money value={card.total} cents={false} />}
            sub={`${plural(card.customers, 'cliente', 'clientes')}, maior atraso ${plural(card.maxDaysLate, 'dia', 'dias')}`} />
          <ul aria-label="Faixas de atraso" className="grid grid-cols-2 gap-1.5 text-xs">
            {card.buckets.map((b, i) => (
              <li key={b.label} className="rounded-sm bg-muted px-2 py-1">
                <span className="block text-muted-foreground">{BUCKETS[i] ?? b.label}</span>
                <Money value={b.total} cents={false} className="font-semibold" />
              </li>
            ))}
          </ul>
          <details className="text-sm">
            <summary className="inline-flex min-h-11 cursor-pointer items-center font-semibold text-primary sm:min-h-0">{plural(card.topCustomers.length, 'maior cliente', 'maiores clientes')}</summary>
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
    </DashboardCard>
  )
}

/** Caixa projetado das próximas 12 semanas (FIN-07): Os dois / Base / Ajustado na URL, menores saldos e a diferença. */
export function CashForecastPanel({ className }: { className?: string }) {
  const query = useGetDashboardCashForecast()
  const search = useSearch({ strict: false }) as { cenario?: string }
  const navigate = useNavigate()
  const scenario = scenarioOf(search.cenario)
  const choose = (value: Scenario) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, cenario: scenarioParam(value) }), replace: true } as never)
  const selector = query.data?.available ? <ToggleGroup label="Cenário" options={SCENARIOS} value={scenario} onChange={choose} /> : null
  return (
    <DashboardCard code="cash-forecast" title="Caixa projetado, próximas 12 semanas" description="Saldo no fim de cada semana" how={HOW.cash} query={query} action={selector} className={className}>
      {(card) => {
        if (!card.available) {
          return <Empty title="A projeção ajustada pelo histórico dos clientes ainda não está disponível nesta versão." description="Enquanto isso, o fluxo de caixa mostra o realizado e o previsto pelos vencimentos."
            action={<Link to={'/app/financeiro/fluxo-de-caixa' as never} className={primaryAction}>Abrir o fluxo de caixa</Link>} />
        }

        const negative = card.firstAdjustedNegative
        return (
          <div className="space-y-3">
            <p role="note" className="text-sm">
              {negative
                ? <>No cenário ajustado, o caixa fica <b className="text-crit-ink">negativo em {formatDate(negative.date)}</b> ({formatMoney(negative.balance, false)}).</>
                : <>O caixa fica positivo nas próximas 12 semanas nos dois cenários.</>}
              {' '}Hoje: <Money value={card.currentBalance ?? 0} cents={false} />.
            </p>
            <CashChart weeks={card.weeks} scenario={scenario} />
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
            <OriginLink href={cashFlowHref(scenario)}>Ver origem: fluxo de caixa {scenario === 'base' ? 'base' : 'ajustado'}</OriginLink>
          </div>
        )
      }}
    </DashboardCard>
  )
}

/** "Aguardando sua decisão" (DSH-04): o nível 1 da pauta; cada item leva ao documento, onde a decisão é tomada. */
export function DecisionsPanel({ className }: { className?: string }) {
  const query = useGetDashboardDecisions()
  return (
    <DashboardCard code="decisions" title="Aguardando sua decisão" how={HOW.decisions} query={query} className={className}>
      {(card) => {
        if (!card.available) return <Empty title="A pauta de decisões ainda não está disponível nesta versão." description="As ordens de pagamento que esperam você estão em Financeiro → Ordens de pagamento." action={<Link to={'/app/financeiro/ordens-de-pagamento' as never} className={primaryAction}>Ver ordens de pagamento</Link>} />
        if (card.total === 0) return <Empty title="Nada esperando a sua decisão agora." description="Quando uma ordem de pagamento, um aditivo ou um pedido precisar de você, ele aparece aqui." />
        return (
          <div className="space-y-2">
            <p className="text-sm text-muted-foreground">{plural(card.total, 'item', 'itens')}, <Money value={card.totalAmount} cents={false} /> no total{card.firstDue ? `; o primeiro vence em ${formatDate(card.firstDue)}` : ''}.</p>
            <ul className="divide-y divide-border rounded-md border border-border">
              {card.items.map((item) => (
                <li key={item.url + item.title}>
                  <Link to={item.url as never} className="flex min-h-11 items-center justify-between gap-2 px-3 py-2 text-sm hover:bg-muted">
                    <span className="min-w-0"><b className="block truncate">{item.title}</b>{item.detail ? <span className="block truncate text-xs text-muted-foreground">{item.detail}</span> : null}</span>
                    <span className="flex-none text-right text-xs">{typeof item.amount === 'number' ? <Money value={item.amount} cents={false} className="block text-sm font-semibold" /> : null}{item.dueDate ? `vence ${formatDate(item.dueDate)}` : null}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        )
      }}
    </DashboardCard>
  )
}

/** Comercial (COM-08): em aberto, conversão, vencendo em 7 dias e as barras do funil, cada uma até a lista de orçamentos. */
export function CommercialPanel({ className }: { className?: string }) {
  const query = useGetDashboardCommercial()
  return (
    <DashboardCard code="commercial" title="Comercial" description="Orçamentos dos últimos 90 dias" how={HOW.commercial} query={query} className={className}
      action={<OriginLink href="/app/orcamentos/funil">Abrir o funil</OriginLink>}>
      {(card) => {
        const max = Math.max(...card.stages.map((s) => s.count), 0)
        if (max === 0) return <Empty title="Sem orçamentos nos últimos 90 dias." action={<Link to={'/app/orcamentos' as never} className={primaryAction}>Ver orçamentos</Link>} />
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
                    <span className="flex justify-between gap-2 text-sm"><span>{QUOTE_STATUS[s.status as keyof typeof QUOTE_STATUS]?.label ?? s.status}</span><span className="tabular-nums"><b>{s.count}</b> · <Money value={s.amount} cents={false} /></span></span>
                    <span aria-hidden="true" className="mt-1 block h-2 rounded-sm bg-muted"><span className="block h-2 rounded-sm" style={{ width: `${barWidth(s.count, max)}%`, background: `var(--chart-${(i % 6) + 1})` }} /></span>
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        )
      }}
    </DashboardCard>
  )
}
