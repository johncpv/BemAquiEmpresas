import { Link } from '@tanstack/react-router'
import {
  useGetDashboardActiveProjects, useGetDashboardBacklog, useGetDashboardDeliveries, useGetDashboardMargin, useGetDashboardProduction, useListDashboardActiveProjectMargins,
  useListDashboardActiveProjectsReceived,
} from '@/api/generated/bemaqui'
import type { ActiveProjectItem } from '@/api/generated/model'
import { ColumnChart } from '@/components/ui/column-chart'
import { Kpi } from '@/components/ui/kpi'
import { Money } from '@/components/ui/money'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { PROJECT_LETTERS, PROJECT_STAGES, projectStage } from '@/pages/contracts/contracts'
import { costLabel, marginContext, pct1, pp } from '@/pages/projects/margins/margins'
import { useCan } from '@/pages/use-can'
import { PanelCard, primaryAction } from './cards'
import { useGo } from './use-go'
import { HOW, plural } from './dashboard'
import { deliveryOf, finishedMarginHref, occupancyPoints, PROJECTS_ORIGIN, RESULTS_ROUTE } from './owner'

const quotesSent = <Link to={'/app/orcamentos' as never} search={{ situacao: 'enviado' } as never} className={primaryAction}>Ver orçamentos enviados</Link>
const noActive = { title: 'Nenhum projeto em andamento.', description: 'Os projetos nascem quando um orçamento é aprovado.', action: quotesSent }

/**
 * Margem (DSH-01b CA 1 e 2), só com projects.view_cost: a projetada (com custos reais) dos em andamento contra a orçada, a realizada dos
 * encerrados no ano e os 5 piores pelo desvio, cada um até a aba Custos com a natureza que mais desviou (propositivo).
 */
export function MarginKpi({ className }: { className?: string }) {
  const query = useGetDashboardMargin()
  const go = useGo()
  const canResults = useCan('finance.view')
  return (
    <PanelCard code="margin" title="Margem dos projetos" howWeCalculate={HOW.margin} origin="/app/projetos/margens" query={query} className={className}
      empty={(card) => (card.activeCount === 0 && card.finishedCount === 0 ? noActive : null)}>
      {(card) => (
        <div className="space-y-2">
          <Kpi label="Margem projetada (com custos reais)" value={pct1(card.projectedMarginPct)} onClick={() => go('/app/projetos/margens')} actionLabel="Margem projetada: ver o Orçado × realizado"
            context={marginContext({ budgetedMarginPct: card.budgetedMarginPct, marginDeviationPp: card.deviationPp })}
            meter={typeof card.budgetedMarginPct === 'number' && typeof card.projectedMarginPct === 'number' && card.budgetedMarginPct > 0
              ? { value: Math.max(0, card.projectedMarginPct), max: card.budgetedMarginPct, label: `Margem projetada de ${pct1(card.projectedMarginPct)} sobre ${pct1(card.budgetedMarginPct)} orçados`, tone: (card.deviationPp ?? 0) < 0 ? 'warn' : 'ok' }
              : undefined} />
          <p className="inline-flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
            <Dica nome="Margem projetada (com custos reais)" texto={HOW.margin} />
            <Link to={finishedMarginHref(card.year).to as never} search={finishedMarginHref(card.year).search as never} className="font-semibold text-primary hover:underline">
              Margem realizada: {pct1(card.finishedRealizedMarginPct)}
            </Link>
            <Dica nome="Margem realizada" texto={HOW.realizedMargin} />
            <span>({plural(card.finishedCount, 'projeto encerrado', 'projetos encerrados')} em {card.year}{typeof card.finishedDeviationPp === 'number' ? `, ${pp(card.finishedDeviationPp)} da orçada` : ''})</span>
          </p>
          {card.worst.length > 0 ? (
            <details className="text-sm">
              <summary className="inline-flex min-h-[44px] cursor-pointer items-center font-semibold text-primary sm:min-h-0">{plural(card.worst.length, 'pior projeto', 'piores projetos')} pelo desvio</summary>
              <ol className="mt-1 space-y-1">
                {card.worst.map((w) => (
                  <li key={w.projectId}>
                    <Link to={'/app/projetos/$id' as never} params={{ id: w.projectId } as never} search={{ aba: 'custos', ...(w.worstCostType ? { natureza: w.worstCostType } : {}) } as never}
                      className="flex min-h-[44px] items-center justify-between gap-2 rounded-sm px-1 hover:bg-muted sm:min-h-0">
                      <span className="min-w-0 truncate">{w.number} · {w.name}{w.worstCostType ? <span className="block text-xs text-muted-foreground">mais desviou: {costLabel(w.worstCostType)}</span> : null}</span>
                      <span className={cn('flex-none tabular-nums', (w.deviationPp ?? 0) < 0 && 'font-semibold text-warn-ink')}>{pp(w.deviationPp)}</span>
                    </Link>
                  </li>
                ))}
              </ol>
            </details>
          ) : null}
          {canResults ? <Link to={RESULTS_ROUTE as never} className="inline-flex min-h-[44px] items-center text-sm font-semibold text-primary hover:underline sm:min-h-0">Resultado por projeto</Link> : null}
        </div>
      )}
    </PanelCard>
  )
}

/** Entrega do projeto: atrasado, em risco (com os dias) ou no prazo, sempre com o texto (nunca só cor). */
function Delivery({ item }: { item: ActiveProjectItem }) {
  const d = deliveryOf(item)
  return (
    <span className="inline-flex flex-col items-start gap-0.5">
      {d.tone ? (
        <Link to={'/app/projetos/$id' as never} params={{ id: item.id } as never} search={{ aba: 'cronograma' } as never} aria-label={`${d.label}: ver o cronograma de ${item.number}`}>
          <StatusChip tone={d.tone}>{d.label}</StatusChip>
        </Link>
      ) : <span className="text-xs text-muted-foreground">{d.label}</span>}
      <span className="text-xs tabular-nums text-muted-foreground">{item.promisedDate ? `prometido ${formatDate(item.promisedDate)}` : 'sem data prometida'}</span>
    </span>
  )
}

/**
 * Projetos em andamento (DSH-01b CA 1): até 8, atrasados e em risco primeiro; etapa, entrega, contratado e, com as permissões, margem orçada →
 * projetada e o recebido. A 400 px: projeto, etapa e entrega; o resto na linha expansível.
 */
export function ActiveProjectsPanel({ className }: { className?: string }) {
  const query = useGetDashboardActiveProjects()
  const canCost = useCan('projects.view_cost')
  const canFinance = useCan('finance.view')
  const margins = useListDashboardActiveProjectMargins({ query: { enabled: canCost } })
  const received = useListDashboardActiveProjectsReceived({ query: { enabled: canFinance } })
  const marginOf = (id: string) => margins.data?.find((m) => m.projectId === id)
  const receivedOf = (id: string) => received.data?.find((r) => r.projectId === id)
  const Extra = ({ item }: { item: ActiveProjectItem }) => {
    const m = marginOf(item.id)
    const r = receivedOf(item.id)
    return (
      <dl className="grid grid-cols-2 gap-2 text-xs">
        <div><dt className="text-muted-foreground">Contratado</dt><dd><Money value={item.contractedAmount} cents={false} /></dd></div>
        {canCost ? <div><dt className="text-muted-foreground">Margem</dt><dd className="tabular-nums">{m ? `${pct1(m.budgetedMarginPct)} → ${pct1(m.projectedMarginPct)}` : '—'}</dd></div> : null}
        {canFinance ? <div><dt className="text-muted-foreground">Recebido</dt><dd className="tabular-nums">{r ? pct1(r.receivedPct) : '—'}</dd></div> : null}
      </dl>
    )
  }
  return (
    <PanelCard code="active-projects" title="Projetos em andamento" subtitle={canCost ? 'Margem prevista no contrato e projetada com os custos reais' : undefined} howWeCalculate={HOW.activeProjects}
      origin={PROJECTS_ORIGIN} query={query} className={className} empty={(card) => (card.total === 0 ? noActive : null)}>
      {(card) => (
        <div className="space-y-2">
          <table className="w-full text-sm">
            <caption className="sr-only">Projetos em andamento, os atrasados e em risco primeiro</caption>
            <thead className="border-b border-border text-left text-xs text-muted-foreground">
              <tr>
                <th scope="col" className="py-2 pr-2">Projeto</th>
                <th scope="col" className="py-2 pr-2">Etapa atual</th>
                <th scope="col" className="py-2 pr-2">Entrega</th>
                <th scope="col" className="hidden py-2 pr-2 text-right sm:table-cell">Contratado</th>
                {canCost ? <th scope="col" className="hidden py-2 pr-2 text-right sm:table-cell">Margem<Dica nome="Margem projetada (com custos reais)" texto={HOW.margin} /></th> : null}
                {canFinance ? <th scope="col" className="hidden py-2 text-right sm:table-cell">Recebido</th> : null}
              </tr>
            </thead>
            <tbody>
              {card.items.map((item) => {
                const m = marginOf(item.id)
                const r = receivedOf(item.id)
                const stage = projectStage(item.status)
                return (
                  <tr key={item.id} data-risk={item.risk} className={cn('border-b border-border align-top last:border-0', item.risk === 'Late' && 'border-l-4 border-l-crit', item.risk === 'AtRisk' && 'border-l-4 border-l-warn')}>
                    <td className="py-2 pr-2 pl-2">
                      <Link to={'/app/projetos/$id' as never} params={{ id: item.id } as never} className="font-semibold text-primary hover:underline">{item.number}</Link>
                      <span className="block max-w-[12rem] truncate text-xs text-muted-foreground">{item.name}{item.partyName ? ` · ${item.partyName}` : ''}</span>
                      <details className="mt-1 sm:hidden">
                        <summary className="inline-flex min-h-[44px] cursor-pointer items-center text-xs font-semibold text-primary">Valores</summary>
                        <Extra item={item} />
                      </details>
                    </td>
                    <td className="py-2 pr-2">{stage < 0 ? <StatusChip tone="neutral">Cancelado</StatusChip> : <StageIndicator stages={PROJECT_STAGES} letters={PROJECT_LETTERS} current={stage} />}</td>
                    <td className="py-2 pr-2"><Delivery item={item} /></td>
                    <td className="hidden py-2 pr-2 text-right sm:table-cell"><Money value={item.contractedAmount} cents={false} /></td>
                    {canCost ? <td className="hidden py-2 pr-2 text-right tabular-nums sm:table-cell">{m ? <>{pct1(m.budgetedMarginPct)} → <b className={(m.deviationPp ?? 0) < 0 ? 'text-warn-ink' : undefined}>{pct1(m.projectedMarginPct)}</b></> : '—'}</td> : null}
                    {canFinance ? <td className="hidden py-2 text-right tabular-nums sm:table-cell">{r ? pct1(r.receivedPct) : '—'}</td> : null}
                  </tr>
                )
              })}
            </tbody>
          </table>
          {card.total > card.items.length ? <Link to={PROJECTS_ORIGIN as never} className="inline-flex min-h-[44px] items-center text-sm font-semibold text-primary hover:underline sm:min-h-0">Ver os {card.total} projetos</Link> : null}
        </div>
      )}
    </PanelCard>
  )
}

/** Produção (DSH-01b CA 1): projetos em andamento, OS atrasadas e em risco (Kanban filtrado) e a ocupação da semana atual e das 4 seguintes. */
export function ProductionPanel({ className }: { className?: string }) {
  const query = useGetDashboardProduction()
  return (
    <PanelCard code="production" title="Produção" subtitle="Ocupação da fábrica, semana atual e as 4 seguintes" howWeCalculate={HOW.production} origin="/app/producao/planejamento?aba=ocupacao"
      query={query} className={className}>
      {(card) => {
        const points = occupancyPoints(card.weeks)
        const highest = points.reduce((max, p) => (typeof p.ocupacao === 'number' && p.ocupacao > (max?.ocupacao ?? -1) ? p : max), points[0])
        return (
          <div className="space-y-3">
            <dl className="grid grid-cols-3 gap-2 text-sm">
              <div><dt className="text-xs text-muted-foreground">Projetos em andamento</dt><dd className="font-semibold tabular-nums">{card.activeProjects}</dd></div>
              <div><dt className="text-xs text-muted-foreground">OS atrasadas</dt><dd>
                <Link to={'/app/producao/kanban' as never} search={{ atrasadas: '1' } as never} className={cn('inline-flex min-h-[44px] items-center font-semibold tabular-nums hover:underline sm:min-h-0', card.lateOrders > 0 && 'text-crit-ink')}>{card.lateOrders}</Link>
              </dd></div>
              <div><dt className="text-xs text-muted-foreground">OS em risco<Dica nome="Em risco" texto={HOW.atRisk} /></dt><dd>
                <Link to={'/app/producao/kanban' as never} search={{ risco: '1' } as never} className={cn('inline-flex min-h-[44px] items-center font-semibold tabular-nums hover:underline sm:min-h-0', card.atRiskOrders > 0 && 'text-warn-ink')}>{card.atRiskOrders}</Link>
              </dd></div>
            </dl>
            <ColumnChart title="Ocupação da fábrica por semana" data={points} xKey="semana" xLabel="Semana" series={[{ key: 'ocupacao', label: 'Ocupação', fill: 'solid' }]}
              format={(v) => `${v.toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%`} references={[{ y: 100, label: 'capacidade' }]} height={200}
              summary={highest && typeof highest.ocupacao === 'number' ? `A semana mais cheia é ${highest.semana}, com ${highest.ocupacao.toLocaleString('pt-BR', { maximumFractionDigits: 1 })}% da capacidade.${card.busyUntil ? ` Fábrica ocupada até ${formatDate(card.busyUntil)}.` : ''}` : 'Sem capacidade cadastrada para as próximas semanas.'} />
          </div>
        )
      }}
    </PanelCard>
  )
}

/** Carteira (DSH-01b CA 3): a produzir e, com finance.view, a faturar, com o chip "Provisório" antes das notas reais. */
export function BacklogPanel({ className }: { className?: string }) {
  const query = useGetDashboardBacklog()
  const go = useGo()
  return (
    <PanelCard code="backlog" title="Carteira" howWeCalculate={HOW.backlog} origin={PROJECTS_ORIGIN} query={query} className={className}>
      {(card) => (
        <div className="grid gap-2">
          <Kpi label="A produzir" value={<Money value={card.toProduce} cents={false} />} onClick={() => go(PROJECTS_ORIGIN)} actionLabel="A produzir: ver os projetos"
            context={plural(card.toProduceCount, 'projeto ainda não fabricado', 'projetos ainda não fabricados')} />
          <p className="inline-flex items-center text-xs text-muted-foreground">O que é "a produzir"<Dica nome="A produzir" texto={HOW.toProduce} /></p>
          {typeof card.toInvoice === 'number' ? (
            <>
              <Kpi label="A faturar" value={<Money value={card.toInvoice} cents={false} />} onClick={() => go(PROJECTS_ORIGIN)} actionLabel="A faturar: ver os projetos"
                context={`${formatMoney(card.contracted ?? 0, false)} contratados − ${formatMoney(card.recognized ?? 0, false)} já faturados`} />
              <p className="inline-flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
                O que é "a faturar"<Dica nome="A faturar" texto={HOW.toInvoice} />
                {card.provisional ? <><StatusChip tone="info">Provisório</StatusChip><Dica nome="Provisório" texto={HOW.provisional} /></> : null}
              </p>
            </>
          ) : null}
        </div>
      )}
    </PanelCard>
  )
}

/** Entregas (DSH-01b CA 1): previstas no mês, entregues, em risco e atrasadas; o projeto que mais vai atrasar leva ao cronograma. */
export function DeliveriesPanel({ className }: { className?: string }) {
  const query = useGetDashboardDeliveries()
  return (
    <PanelCard code="deliveries" title="Entregas do mês" howWeCalculate={HOW.deliveries} origin={PROJECTS_ORIGIN} query={query} className={className}>
      {(card) => (
        <div className="space-y-3">
          <dl className="grid grid-cols-2 gap-2 text-sm">
            <div className="rounded-sm bg-muted p-2"><dt className="text-xs text-muted-foreground">Previstas no mês</dt><dd className="font-semibold tabular-nums">{card.forecastInMonth}</dd></div>
            <div className="rounded-sm bg-muted p-2"><dt className="text-xs text-muted-foreground">Entregues no mês</dt><dd className="font-semibold tabular-nums">{card.deliveredInMonth}</dd></div>
            <div className="rounded-sm bg-muted p-2"><dt className="inline-flex items-center text-xs text-muted-foreground">Em risco<Dica nome="Em risco" texto={HOW.atRisk} /></dt><dd className={cn('font-semibold tabular-nums', card.atRisk > 0 && 'text-warn-ink')}>{card.atRisk}</dd></div>
            <div className="rounded-sm bg-muted p-2"><dt className="text-xs text-muted-foreground">Atrasadas</dt><dd className={cn('font-semibold tabular-nums', card.late > 0 && 'text-crit-ink')}>{card.late}</dd></div>
          </dl>
          {card.mostDelayed ? (
            <Link to={'/app/projetos/$id' as never} params={{ id: card.mostDelayed.projectId } as never} search={{ aba: 'cronograma' } as never}
              className="flex min-h-[44px] flex-col rounded-md border border-l-4 border-warn-soft border-l-warn p-2 text-sm hover:bg-muted">
              <b>{card.mostDelayed.name || card.mostDelayed.number} vai atrasar {plural(card.mostDelayed.delayDays, 'dia', 'dias')}</b>
              <span className="text-xs text-muted-foreground">Previsão {card.mostDelayed.forecastDate ? formatDate(card.mostDelayed.forecastDate) : '—'}, prometido ao cliente {card.mostDelayed.promisedDate ? formatDate(card.mostDelayed.promisedDate) : '—'}. Ver o cronograma.</span>
            </Link>
          ) : <p className="text-sm text-muted-foreground">Nenhuma entrega prevista depois da data prometida.</p>}
        </div>
      )}
    </PanelCard>
  )
}
