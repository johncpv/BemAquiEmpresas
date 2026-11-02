import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import { useGetMaintenanceCostReport, useListAssetWorkCenterChoices, useListMaintenanceCostOrders } from '@/api/generated/bemaqui'
import type { ListMaintenanceCostOrdersParams, MaintenanceCostCell, MaintenanceOrderType } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ColumnChart } from '@/components/ui/column-chart'
import { DashboardCard } from '@/components/ui/dashboard-card'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatInstantDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { AssetsTabs } from './AssetsTabs'
import { ORDER_TYPE_LABELS } from './maintenance'
import { COST_DICAS, costSummary, defaultPeriod, monthLabel, shortMoney } from './maintenance-cost'

type Filters = { de?: string; ate?: string; centro?: string; bem?: string }

/** Célula clicada (padrão 2): o mês, o bem, o centro e o tipo da lista de OS. */
type Cell = { label: string; month?: string; assetId?: string; workCenterId?: string; type?: MaintenanceOrderType }

const chip = 'inline-flex min-h-11 items-center rounded-md px-1 text-left font-semibold text-primary tabular-nums hover:underline'

/**
 * Patrimônio → Custo de manutenção (`/app/patrimonio/custo`, PAT-03b CA 7): por mês (gráfico de colunas preventiva × corretiva com o resumo
 * acima e a tabela equivalente), por bem e por centro, com o drill-down até a OS (o total da lista é o da célula), "Como calculamos" e
 * "Atualizado às". Só com assets.edit; sem a ação, "Dados protegidos". Filtros na URL; em cartões a 400 px.
 */
export function MaintenanceCostPage() {
  const canEdit = useCan('assets.edit')
  const search = useSearch({ strict: false }) as Filters
  const navigate = useNavigate()
  const period = defaultPeriod()
  const from = search.de || period.from
  const to = search.ate || period.to
  const [cell, setCell] = useState<Cell | null>(null)
  const centers = useListAssetWorkCenterChoices({ query: { enabled: canEdit } })
  const report = useGetMaintenanceCostReport({ from, to, workCenterId: search.centro || undefined, assetId: search.bem || undefined }, { query: { enabled: canEdit } })
  const setFilter = (patch: Filters) => { setCell(null); void navigate({ to: '/app/patrimonio/custo', search: { ...search, ...patch } as never, replace: true }) }

  const heading = (
    <PageHeading title="Patrimônio: Custo de manutenção">
      <p className="mt-1 text-muted-foreground">Quanto cada máquina custa em manutenção, por mês, por bem e por centro de trabalho.</p>
    </PageHeading>
  )
  if (!canEdit) {
    return (
      <>
        {heading}
        <AssetsTabs />
        <div role="status" className="max-w-xl space-y-2 rounded-md border border-border bg-card p-6">
          <p className="flex items-center gap-2 font-semibold"><ShieldCheck aria-hidden="true" className="size-5 text-muted-foreground" />Dados protegidos</p>
          <p className="text-sm text-muted-foreground">O custo de manutenção é visto por quem edita o patrimônio. Peça ao administrador para incluir você num grupo com essa permissão.</p>
          <Link to="/app/patrimonio/manutencao" className="inline-flex min-h-11 items-center text-sm font-semibold text-primary">Ver a manutenção</Link>
        </div>
      </>
    )
  }

  const data = report.data
  const goDone = <Link to="/app/patrimonio/manutencao" search={{ situacao: 'Done' } as never} className="inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver as OS concluídas</Link>
  const empty = !!data && data.total.total <= 0
  return (
    <>
      {heading}
      <AssetsTabs />
      <div role="group" aria-label="Filtros do custo" className="mb-4 grid gap-2 sm:grid-cols-4">
        <label className="space-y-1 text-sm"><span className="font-medium">De</span>
          <input type="month" value={from} onChange={(e) => setFilter({ de: e.target.value || undefined })} className={inputClass} />
        </label>
        <label className="space-y-1 text-sm"><span className="font-medium">Até</span>
          <input type="month" value={to} onChange={(e) => setFilter({ ate: e.target.value || undefined })} className={inputClass} />
        </label>
        <label className="space-y-1 text-sm"><span className="font-medium">Centro</span>
          <select value={search.centro ?? ''} onChange={(e) => setFilter({ centro: e.target.value || undefined })} className={inputClass}>
            <option value="">Todos</option>
            {(centers.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
        {search.bem ? (
          <div className="flex items-end">
            <button type="button" onClick={() => setFilter({ bem: undefined })} className="inline-flex min-h-11 items-center rounded-md border border-border px-3 text-sm font-semibold">Ver todos os bens</button>
          </div>
        ) : null}
      </div>

      <div className="space-y-4">
        <DashboardCard title="Custo por mês" subtitle={`${monthLabel(from)} a ${monthLabel(to)}`} howWeCalculate={COST_DICAS.comoCalculamos} updatedAt={report.dataUpdatedAt || undefined}
          query={report} empty={{ when: empty, title: 'Nenhum custo de manutenção lançado.', description: 'O custo entra na conclusão da OS (prestador) e na aba Custo de cada OS concluída.', action: goDone }}>
          {data ? (
            <div className="space-y-4">
              <Totals cell={data.total} onCell={(type) => setCell({ label: `Período${type ? ` · ${ORDER_TYPE_LABELS[type]}` : ''}`, type, workCenterId: search.centro, assetId: search.bem })} />
              <ColumnChart title="Custo de manutenção por mês" xKey="month" xLabel="Mês" summary={costSummary(data.months, data.total)} format={(v) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v)} labelFormat={shortMoney}
                data={data.months.map((m) => ({ month: monthLabel(m.month), preventive: m.cost.preventive, corrective: m.cost.corrective }))}
                series={[{ key: 'preventive', label: 'Preventiva', fill: 'outline' }, { key: 'corrective', label: 'Corretiva', fill: 'solid' }]} />
              <ul aria-label="Total de cada mês" className="grid gap-2 sm:grid-cols-3 lg:grid-cols-4">
                {data.months.filter((m) => m.cost.total > 0).map((m) => (
                  <li key={m.month} className="rounded-md border border-border p-3 text-sm">
                    <p className="text-xs text-muted-foreground">{monthLabel(m.month)}</p>
                    <button type="button" onClick={() => setCell({ label: monthLabel(m.month), month: m.month, workCenterId: search.centro, assetId: search.bem })} className={chip}
                      aria-label={`Ver as OS de ${monthLabel(m.month)}`}><Money value={m.cost.total} /></button>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </DashboardCard>

        {cell ? <Drill cell={cell} from={from} to={to} onClose={() => setCell(null)} /> : null}

        {data && !empty ? (
          <div className="grid gap-4 lg:grid-cols-2">
            <section aria-label="Por bem" className="rounded-md border border-border bg-card p-4">
              <h2 className="mb-2 text-base font-semibold">Por bem</h2>
              <ul className="space-y-2">
                {data.assets.map((a) => (
                  <li key={a.assetId} className="rounded-md border border-border p-3 text-sm">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <Link to="/app/patrimonio/bens/$id" params={{ id: a.assetId }} search={{ aba: 'manutencao' } as never} className="font-semibold text-primary hover:underline">{a.tag} · {a.name}</Link>
                      <button type="button" onClick={() => setCell({ label: `${a.tag} · ${a.name}`, assetId: a.assetId })} className={chip} aria-label={`Ver as OS de ${a.tag}`}><Money value={a.cost.total} /></button>
                    </div>
                    <p className="text-xs text-muted-foreground">{a.workCenterName ?? 'Sem centro de trabalho'} · Preventiva <Money value={a.cost.preventive} /> · Corretiva <Money value={a.cost.corrective} /></p>
                  </li>
                ))}
              </ul>
            </section>
            <section aria-label="Por centro de trabalho" className="rounded-md border border-border bg-card p-4">
              <h2 className="mb-2 text-base font-semibold">Por centro de trabalho</h2>
              <ul className="space-y-2">
                {data.centers.map((c) => (
                  <li key={c.workCenterId ?? 'sem-centro'} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border p-3 text-sm">
                    <span>{c.workCenterName ?? 'Sem centro de trabalho'}<span className="block text-xs text-muted-foreground">{c.cost.orders} {c.cost.orders === 1 ? 'OS' : 'OS'}</span></span>
                    {c.workCenterId ? (
                      <button type="button" onClick={() => setCell({ label: c.workCenterName ?? 'Centro', workCenterId: c.workCenterId ?? undefined })} className={chip} aria-label={`Ver as OS de ${c.workCenterName}`}><Money value={c.cost.total} /></button>
                    ) : <Money value={c.cost.total} />}
                  </li>
                ))}
              </ul>
            </section>
          </div>
        ) : null}
      </div>
    </>
  )
}

/** Totais do período (padrão 2: cada total leva à lista de OS). */
function Totals({ cell, onCell }: { cell: MaintenanceCostCell; onCell: (type?: MaintenanceOrderType) => void }) {
  const items: [string, number, MaintenanceOrderType | undefined][] = [['Total', cell.total, undefined], ['Preventiva', cell.preventive, 'Preventive'], ['Corretiva', cell.corrective, 'Corrective']]
  return (
    <dl className="grid gap-2 sm:grid-cols-3">
      {items.map(([label, value, type]) => (
        <div key={label} className="rounded-md border border-border p-3">
          <dt className="text-xs text-muted-foreground">{label}</dt>
          <dd><button type="button" onClick={() => onCell(type)} className={chip + ' font-display text-xl'} aria-label={`Ver as OS: ${label}`}><Money value={value} /></button></dd>
        </div>
      ))}
    </dl>
  )
}

/** Drill-down (CA 7): as OS que formam o valor, com o total igual ao da célula. */
function Drill({ cell, from, to, onClose }: { cell: Cell; from: string; to: string; onClose: () => void }) {
  const params: ListMaintenanceCostOrdersParams = { from, to, month: cell.month, assetId: cell.assetId, workCenterId: cell.workCenterId, type: cell.type }
  const query = useListMaintenanceCostOrders(params)
  return (
    <section aria-label={`OS de ${cell.label}`} className="rounded-md border border-border bg-card p-4">
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold">OS de {cell.label}</h2>
        <button type="button" onClick={onClose} className="inline-flex min-h-11 items-center rounded-md border border-border px-3 text-sm font-semibold">Fechar</button>
      </div>
      {query.isPending ? <Skeleton className="h-24" /> : query.isError || !query.data ? <ErrorState title="Não foi possível carregar as OS." onRetry={() => query.refetch()} /> : (
        <>
          <ul className="divide-y divide-border text-sm">
            {query.data.orders.map((o) => (
              <li key={o.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                <span>
                  <Link to="/app/patrimonio/manutencao/$id" params={{ id: o.id }} search={{ aba: 'custo' } as never} className="font-semibold text-primary hover:underline">{o.number}</Link>
                  <span className="block text-xs text-muted-foreground">{o.assetTag} · {o.assetName} · {ORDER_TYPE_LABELS[o.type]} · concluída em {formatInstantDate(o.doneAt)}</span>
                </span>
                <Money value={o.cost} />
              </li>
            ))}
          </ul>
          <p className="mt-2 flex justify-between border-t border-border pt-2 text-sm font-semibold"><span>Total</span><Money value={query.data.total} /></p>
        </>
      )}
    </section>
  )
}
