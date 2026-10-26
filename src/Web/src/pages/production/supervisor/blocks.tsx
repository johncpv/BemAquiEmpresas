import { type QueryClient, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { type ReactNode, useCallback, useState } from 'react'
import {
  useGetSupervisorBoard, useGetSupervisorCapacity, useGetSupervisorIndicators, useGetSupervisorQuality, useGetSupervisorShortages,
} from '@/api/generated/bemaqui'
import type { SupervisorOverview } from '@/api/generated/model'
import { DashboardCard } from '@/components/ui/dashboard-card'
import { SidePanel } from '@/components/ui/side-panel'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useProductionLive } from '@/pages/production/kanban/useBoardLive'
import { bandChip, percentText as loadPercent, shortDate } from '@/pages/production/planning/load'
import { QualityInspection } from '@/pages/production/quality/QualityInspection'
import { NC_STATUS } from '@/pages/production/quality/quality'
import {
  ageText, blocksLabel, hoursText, kanbanSearch, newRequestSearch, pendingLabel, percentText, plural, quantityText, riskText, SUPERVISOR_DICAS, SUPERVISOR_HOW, weekLabel,
} from './supervisor'

/** Recarga da qualidade e das faltas (CA 7): a cada 60 s e ao voltar o foco. */
export const POLL_MS = 60_000

const action = 'inline-flex min-h-[44px] items-center rounded-md border border-border px-3 text-sm font-semibold text-primary sm:min-h-9'
const primary = 'inline-flex min-h-[44px] items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground sm:min-h-9'
const link = 'font-semibold text-primary tabular-nums hover:underline'

export interface BlockProps {
  line?: string
  overview: SupervisorOverview
  className?: string
}

const params = (line?: string) => (line ? { line } : undefined)

/** Bloco no `DashboardCard` (padrão 2): título, "Como calculamos", "Abrir", "Atualizado às" e os estados do bloco. */
function Block<T extends { updatedAt: string }>({ code, title, subtitle, how, open, query, empty, className, children }: {
  code: string
  title: string
  subtitle?: string
  how: string
  open: { to: string; search?: Record<string, string> }
  query: { data?: T; isPending: boolean; isError: boolean; refetch: () => unknown }
  empty?: (data: T) => { title: string; description?: string; action?: ReactNode } | null
  className?: string
  children: (data: T) => ReactNode
}) {
  const navigate = useNavigate()
  const blank = query.data && empty ? empty(query.data) : null
  return (
    <div data-block={code} className={className}>
      <DashboardCard title={title} subtitle={subtitle} howWeCalculate={how} originLabel="Abrir" className="h-full"
        onOrigin={() => void navigate({ to: open.to, search: open.search } as never)} updatedAt={query.data ? Date.parse(query.data.updatedAt) : undefined}
        query={{ isPending: query.isPending, isError: query.isError || (!query.isPending && !query.data), refetch: query.refetch }}
        empty={blank ? { when: true, ...blank } : undefined}>
        {query.data ? children(query.data) : null}
      </DashboardCard>
    </div>
  )
}

/** Letra da etapa no display de andar (o mesmo do Kanban). */
function Letter({ code }: { code?: string | null }) {
  return (
    <span aria-hidden="true" className="grid h-[22px] min-w-[26px] shrink-0 place-items-center rounded-[5px] bg-rail px-1 font-display text-xs font-extrabold tracking-wider text-signal">
      {code ?? '–'}
    </span>
  )
}

/**
 * Quadro (CA 2): colunas por etapa com contagem e horas restantes, só as OS atrasadas e em risco abertas e os chips que abrem o Kanban
 * filtrado. Atualiza pelo ProductionHub (OrderBoardChanged), como o Kanban. A 400 px, só os chips e a lista.
 */
export function BoardBlock({ line, className }: BlockProps) {
  const query = useGetSupervisorBoard(params(line))
  useProductionLive(useCallback((client: QueryClient) =>
    void client.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith('/api/v1/dashboard/supervisor/board') }), []))
  const chip = 'inline-flex min-h-[44px] items-center rounded-full border px-3 text-sm font-semibold tabular-nums sm:min-h-8'
  return (
    <Block code="board" title="Quadro" subtitle="OS abertas por etapa" how={SUPERVISOR_HOW.board} open={{ to: '/app/producao/kanban', search: kanbanSearch(null, line) }} query={query} className={className}
      empty={(b) => (b.openCount === 0 ? { title: 'Nenhuma OS aberta.', description: 'As OS nascem com a aprovação do orçamento.', action: <Link to="/app/producao/os" className={action}>Ver OS de fabricação</Link> } : null)}>
      {(b) => (
        <div className="space-y-3">
          <div className="flex flex-wrap gap-2">
            <Link to="/app/producao/kanban" search={kanbanSearch('late', line) as never} className={cn(chip, b.lateCount > 0 ? 'border-crit bg-crit-soft text-crit-ink' : 'border-border')}>
              {b.lateCount} {b.lateCount === 1 ? 'atrasada' : 'atrasadas'}
            </Link>
            <Link to="/app/producao/kanban" search={kanbanSearch('risk', line) as never} className={cn(chip, b.atRiskCount > 0 ? 'border-warn bg-warn-soft text-warn-ink' : 'border-border')}>
              {b.atRiskCount} em risco
            </Link>
          </div>
          <ul aria-label="Colunas do quadro" className="grid grid-cols-2 gap-2 max-sm:hidden md:grid-cols-4 xl:grid-cols-6">
            {b.columns.map((c) => (
              <li key={c.key} className="rounded-md bg-muted/50 p-2">
                <p className="flex items-center gap-1.5 text-sm font-semibold"><Letter code={c.code} /><span className="truncate">{c.name}</span></p>
                <p className="mt-1 text-xs text-muted-foreground tabular-nums">{c.count} OS · {hoursText(c.remainingHours)} restantes</p>
                {c.lateCount + c.atRiskCount > 0 ? (
                  <p className="mt-1 flex flex-wrap gap-1">
                    {c.lateCount > 0 ? <StatusChip tone="crit">{c.lateCount} {c.lateCount === 1 ? 'atrasada' : 'atrasadas'}</StatusChip> : null}
                    {c.atRiskCount > 0 ? <StatusChip tone="warn">{c.atRiskCount} em risco</StatusChip> : null}
                  </p>
                ) : null}
              </li>
            ))}
          </ul>
          {b.cards.length === 0 ? <p role="status" className="rounded-md border border-dashed border-border px-3 py-4 text-center text-sm text-muted-foreground">Nenhuma OS atrasada ou em risco.</p> : (
            <ul aria-label="OS atrasadas e em risco" className="grid gap-2 lg:grid-cols-2">
              {b.cards.map((c) => {
                const band = riskText(c)
                return (
                  <li key={c.orderId} className={cn('rounded-md border border-l-4 border-border bg-card p-3', band.tone === 'crit' ? 'border-l-crit' : 'border-l-warn')}>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <Link to="/app/producao/os/$id" params={{ id: c.orderId }} className={link}>{c.number}</Link>
                      <span className={cn('text-xs font-semibold tabular-nums', band.tone === 'crit' ? 'text-crit-ink' : 'text-warn-ink')}>{band.text}</span>
                    </div>
                    <p className="text-sm">{c.productName}</p>
                    <p className="text-xs text-muted-foreground">{c.projectNumber}{c.partyName ? ` · ${c.partyName}` : ''}</p>
                    {c.stepNames.length > 0 ? <StageIndicator className="mt-2" stages={c.stepNames} letters={c.codes} current={c.currentStepIndex} showLabel /> : null}
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      )}
    </Block>
  )
}

/** Capacidade (CA 3): semana atual e as próximas 4 por centro, com "Folga", "Lotado" e "Acima da capacidade" e "ocupado até {data}". */
export function CapacityBlock({ line, overview, className }: BlockProps) {
  const query = useGetSupervisorCapacity(params(line), { query: { staleTime: 60_000 } })
  return (
    <Block code="capacity" title="Capacidade" subtitle="Ocupação das próximas semanas" how={SUPERVISOR_HOW.capacity}
      open={{ to: '/app/producao/planejamento', search: { aba: 'ocupacao', ...(line ? { linha: line } : {}) } }} query={query} className={className}
      empty={(c) => (c.centers.length === 0 ? { title: 'Nenhum centro de trabalho ativo.', description: 'Cadastre os centros e os turnos para ver a ocupação.', action: <Link to="/app/producao/centros-de-trabalho" className={action}>Centros de trabalho</Link> } : null)}>
      {(c) => (
        <ul aria-label="Ocupação por centro" className="space-y-3">
          {[...c.centers, c.factory].map((row) => (
            <li key={row.workCenterId ?? 'fabrica'} className={cn(!row.workCenterId && 'border-t border-border pt-3')}>
              <p className="flex flex-wrap items-baseline justify-between gap-x-2 text-sm">
                <span className="font-semibold">{row.name}</span>
                {row.busyUntil ? <span className="text-xs text-muted-foreground tabular-nums">ocupado até {shortDate(row.busyUntil, overview.today)}</span> : null}
              </p>
              <div className="mt-1 grid grid-cols-5 gap-1">
                {row.cells.map((cell, i) => {
                  const chip = bandChip(cell.band)
                  const week = c.weeks[i]
                  return (
                    <div key={cell.weekStart} data-band={cell.band} className="min-w-0 rounded bg-muted/50 px-1 py-1 text-center">
                      <span className="block text-[11px] text-muted-foreground">{week ? weekLabel(week.isoWeek) : formatDate(cell.weekStart).slice(0, 5)}</span>
                      <span className="block text-sm font-semibold tabular-nums">{loadPercent(cell.percent)}</span>
                      {chip ? <span className={cn('block truncate text-[11px] font-semibold', chip.tone === 'crit' ? 'text-crit-ink' : chip.tone === 'warn' ? 'text-warn-ink' : 'text-info-ink')}>{chip.label}</span> : null}
                    </div>
                  )
                })}
              </div>
            </li>
          ))}
        </ul>
      )}
    </Block>
  )
}

/**
 * Qualidade (CA 4): com production.approve_quality, as etapas que eu posso decidir, com "Aprovar" e "Reprovar" abrindo a inspeção da PRD-05;
 * sem ela, só a contagem. As NC abertas, da mais antiga, para todos.
 */
export function QualityBlock({ line, className }: BlockProps) {
  const query = useGetSupervisorQuality(params(line), { query: { refetchInterval: POLL_MS, refetchOnWindowFocus: true } })
  const [inspecting, setInspecting] = useState<{ stepId: string; title: string } | null>(null)
  const client = useQueryClient()
  // Fechou a inspeção: a fila e o quadro recarregam (a decisão muda a etapa da OS; o hub também avisa).
  const close = () => {
    setInspecting(null)
    void client.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && (q.queryKey[0].startsWith('/api/v1/dashboard/supervisor/quality') || q.queryKey[0].startsWith('/api/v1/dashboard/supervisor/board')) })
  }
  return (
    <>
      <Block code="quality" title="Qualidade" subtitle="Decisões e não conformidades abertas" how={SUPERVISOR_HOW.quality}
        open={query.data?.canDecide ? { to: '/app/producao/qualidade' } : { to: '/app/producao/nao-conformidades', search: { filtro: 'todas' } }} query={query} className={className}>
        {(q) => (
          <div className="space-y-4">
            <section aria-labelledby="sup-quality-steps" className="space-y-2">
              <h3 id="sup-quality-steps" className="text-sm font-semibold">Aguardando a qualidade</h3>
              {!q.canDecide ? (
                <p className="text-sm tabular-nums">{pendingLabel(q.pendingCount)}</p>
              ) : q.steps.length === 0 ? (
                <p role="status" className="text-sm text-muted-foreground">
                  Nenhuma etapa aguardando a qualidade.{q.pendingCount > 0 ? ` ${pendingLabel(q.pendingCount)}, com trabalho seu: outra pessoa decide.` : ''}
                </p>
              ) : (
                <ul className="space-y-2">
                  {q.steps.map((s) => (
                    <li key={s.stepId} className="rounded-md border border-border p-3">
                      <div className="flex flex-wrap items-center gap-2">
                        <Link to="/app/producao/os/$id" params={{ id: s.orderId }} search={{ aba: 'qualidade' } as never} className={link}>{s.orderNumber}</Link>
                        <span className="text-sm">{s.stepCode} · {s.stepName}</span>
                        {s.selfInspection ? <span className="inline-flex items-center"><StatusChip tone="warn">Autoinspeção</StatusChip><Dica nome="Autoinspeção" texto={SUPERVISOR_DICAS.autoinspecao} /></span> : null}
                        {s.openNonConformities > 0 ? <StatusChip tone="crit">NC aberta</StatusChip> : null}
                      </div>
                      <p className="text-xs text-muted-foreground">
                        {s.productName}{s.projectNumber ? ` · ${s.projectNumber}` : ''} · Tentativa {s.attempt}
                        {s.waitingSince ? ` · aguardando desde ${formatDateTime(s.waitingSince)}` : ''}
                      </p>
                      <div className="mt-2 flex flex-wrap gap-2">
                        <button type="button" className={primary} onClick={() => setInspecting({ stepId: s.stepId, title: `Aprovar qualidade · ${s.orderNumber}` })}>Aprovar</button>
                        <button type="button" className={action} onClick={() => setInspecting({ stepId: s.stepId, title: `Reprovar qualidade · ${s.orderNumber}` })}>Reprovar</button>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </section>
            <section aria-labelledby="sup-quality-nc" className="space-y-2">
              <h3 id="sup-quality-nc" className="text-sm font-semibold tabular-nums">Não conformidades abertas ({q.openNonConformities})</h3>
              {q.nonConformities.length === 0 ? <p role="status" className="text-sm text-muted-foreground">Nenhuma NC aberta.</p> : (
                <ul className="divide-y divide-border">
                  {q.nonConformities.map((n) => (
                    <li key={n.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
                      <span className="min-w-0">
                        <Link to="/app/producao/nao-conformidades/$id" params={{ id: n.id }} className={link}>{n.number}</Link>
                        <span className="block text-xs text-muted-foreground">{n.orderNumber} · {n.stepCode} {n.stepName} · {n.responsibleName}</span>
                      </span>
                      <span className="flex items-center gap-2">
                        <span className="text-xs tabular-nums text-muted-foreground">{ageText(n.ageDays)}</span>
                        <StatusChip tone={NC_STATUS[n.status].tone}>{NC_STATUS[n.status].label}</StatusChip>
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        )}
      </Block>
      <SidePanel open={!!inspecting} onOpenChange={(open) => { if (!open) close() }} title={inspecting?.title ?? 'Inspeção de qualidade'}>
        {inspecting ? <QualityInspection stepId={inspecting.stepId} queue="/app/producao/qualidade" /> : null}
      </SidePanel>
    </>
  )
}

/**
 * Faltas de material (CA 5): primeiro as que bloqueiam nos próximos 5 dias úteis, com a situação da compra (só leitura) e "Gerar solicitação"
 * para quem tem purchasing.create. Só montado com inventory.view.
 */
export function ShortagesBlock({ line, overview, className }: BlockProps) {
  const query = useGetSupervisorShortages(params(line), { query: { refetchInterval: POLL_MS, refetchOnWindowFocus: true } })
  return (
    <Block code="shortages" title="Faltas de material" subtitle={query.data ? `${plural(query.data.blocking, 'bloqueia', 'bloqueiam')} nos próximos 5 dias úteis` : undefined}
      how={SUPERVISOR_HOW.shortages} open={{ to: '/app/estoque/faltas' }} query={query} className={className}
      empty={(s) => (s.items.length === 0 ? { title: 'Nenhuma falta de material.', description: 'Todo o material das OS abertas está reservado.' } : null)}>
      {(s) => (
        <>
          <ul className="divide-y divide-border">
            {s.items.map((f) => {
              const blocks = blocksLabel(f.blocksInDays)
              return (
                <li key={`${f.orderId}-${f.itemId}`} className={cn('py-2 text-sm', f.blocks && 'border-l-4 pl-2', f.blocks && (f.blocksInDays === 0 ? 'border-l-crit' : 'border-l-warn'))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <span className="min-w-0 font-semibold">{f.itemCode ? `${f.itemCode} · ` : ''}{f.itemName}</span>
                    {f.blocks && blocks ? (
                      <span className="inline-flex items-center"><StatusChip tone={f.blocksInDays === 0 ? 'crit' : 'warn'}>{blocks}</StatusChip><Dica nome="Bloqueia em {n} dias" texto={SUPERVISOR_DICAS.bloqueia} /></span>
                    ) : null}
                  </div>
                  <p className="text-xs text-muted-foreground tabular-nums">
                    Falta {quantityText(f.shortQty)} {f.unit} · <Link to="/app/producao/os/$id" params={{ id: f.orderId }} search={{ aba: 'material' } as never} className={link}>{f.orderNumber}</Link>
                    {f.projectNumber ? ` · ${f.projectNumber}` : ''} · {f.neededOn ? `necessária em ${formatDate(f.neededOn)}` : 'sem previsão'}
                  </p>
                  <div className="mt-1 flex flex-wrap items-center gap-2">
                    <PurchaseStatus item={f} canView={overview.canViewPurchasing} />
                    {f.purchase === 'None' && s.canCreateRequest ? (
                      <Link to={'/app/compras/solicitacoes/nova' as never} search={newRequestSearch(f.orderId, f.itemId) as never} className={action}>Gerar solicitação</Link>
                    ) : null}
                  </div>
                </li>
              )
            })}
          </ul>
          {s.total > s.items.length ? <p className="mt-2 text-xs text-muted-foreground tabular-nums">Mostrando {s.items.length} de {s.total}. Abra a lista de faltas para ver todas.</p> : null}
        </>
      )}
    </Block>
  )
}

function PurchaseStatus({ item, canView }: { item: { purchase: string; purchaseLabel: string; purchaseRequestId?: string | null; purchaseOrderId?: string | null }; canView: boolean }) {
  const tone = item.purchase === 'None' ? 'crit' : item.purchase === 'Ordered' ? 'ok' : 'info'
  const chip = <StatusChip tone={tone}>{item.purchaseLabel}</StatusChip>
  if (!canView) return chip
  if (item.purchaseOrderId) return <Link to={'/app/compras/pedidos/$id' as never} params={{ id: item.purchaseOrderId } as never} className="hover:underline">{chip}</Link>
  if (item.purchaseRequestId) return <Link to={'/app/compras/solicitacoes/$id' as never} params={{ id: item.purchaseRequestId } as never} className="hover:underline">{chip}</Link>
  return chip
}

/** Indicadores (CA 6): últimos 30 dias, horas paradas por motivo (com as OS que mais pararam) e a taxa de NC por etapa de qualidade. */
export function IndicatorsBlock({ line, className }: BlockProps) {
  const query = useGetSupervisorIndicators(params(line), { query: { staleTime: 60_000 } })
  return (
    <Block code="indicators" title="Indicadores" subtitle="Últimos 30 dias" how={SUPERVISOR_HOW.indicators} open={{ to: '/app/producao/nao-conformidades', search: { filtro: 'todas' } }}
      query={query} className={className}
      empty={(i) => (i.stoppedHours === 0 && i.qualityRates.length === 0 ? { title: 'Sem paradas nem decisões de qualidade nos últimos 30 dias.', description: 'As pausas vêm do app do operador e as decisões, da inspeção de qualidade.' } : null)}>
      {(i) => (
        <div className="space-y-4">
          <section aria-labelledby="sup-stoppages" className="space-y-1">
            <h3 id="sup-stoppages" className="flex items-center text-sm font-semibold">
              Horas paradas<Dica nome="Horas paradas" texto={SUPERVISOR_DICAS.horasParadas} />
              <span className="ml-auto font-display text-lg tabular-nums">{hoursText(i.stoppedHours)}</span>
            </h3>
            <ul className="divide-y divide-border">
              {i.stoppages.map((s) => (
                <li key={s.reason} className="py-1 text-sm">
                  {s.orders.length > 0 ? (
                    <details>
                      <summary className="flex min-h-[44px] cursor-pointer items-center justify-between gap-2 sm:min-h-8">
                        <span>{s.label} <span className="text-xs text-muted-foreground tabular-nums">({plural(s.pauses, 'pausa', 'pausas')})</span></span>
                        <span className="font-semibold tabular-nums">{hoursText(s.hours)}</span>
                      </summary>
                      <ul className="mb-1 space-y-1 pl-3">
                        {s.orders.map((o) => (
                          <li key={o.orderId} className="flex justify-between gap-2 text-xs">
                            <Link to="/app/producao/os/$id" params={{ id: o.orderId }} search={{ aba: 'apontamentos' } as never} className={link}>{o.orderNumber}</Link>
                            <span className="tabular-nums">{hoursText(o.hours)}</span>
                          </li>
                        ))}
                      </ul>
                    </details>
                  ) : (
                    <p className="flex min-h-8 items-center justify-between gap-2"><span>{s.label}</span><span className="tabular-nums text-muted-foreground">{hoursText(s.hours)}</span></p>
                  )}
                </li>
              ))}
            </ul>
          </section>
          <section aria-labelledby="sup-nc-rate" className="space-y-1">
            <h3 id="sup-nc-rate" className="flex items-center text-sm font-semibold">Taxa de NC<Dica nome="Taxa de NC" texto={SUPERVISOR_DICAS.taxaNc} /></h3>
            {i.qualityRates.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma decisão de qualidade no período.</p> : (
              <ul className="divide-y divide-border">
                {i.qualityRates.map((r) => (
                  <li key={r.stepCode}>
                    <Link to="/app/producao/nao-conformidades" search={{ filtro: 'todas' } as never} className="flex min-h-[44px] items-center justify-between gap-2 text-sm hover:bg-muted sm:min-h-8">
                      <span className="flex items-center gap-1.5"><Letter code={r.stepCode} />{r.stepName}</span>
                      <span className="text-right tabular-nums">
                        <b className={cn((r.ratePercent ?? 0) > 0 && 'text-warn-ink')}>{percentText(r.ratePercent)}</b>
                        <span className="block text-xs text-muted-foreground">{r.rejections} de {plural(r.decisions, 'decisão', 'decisões')}</span>
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      )}
    </Block>
  )
}
