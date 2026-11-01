import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { useListAssetWorkCenterChoices, useListMaintenanceOrders } from '@/api/generated/bemaqui'
import type { MaintenanceDuePlan } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { AssetsTabs } from './AssetsTabs'
import { OrderCard } from './AssetMaintenance'
import { formatMeter, isOrderStatus, isOrderType, ORDER_STATUS_OPTIONS, ORDER_TYPE_OPTIONS } from './maintenance'
import { OpenOrderDialog } from './MaintenanceDialogs'

const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

type Filters = { bem?: string; centro?: string; tipo?: string; situacao?: string }

/**
 * Patrimônio → Manutenção (PAT-03a CA 10): as OS abertas e em andamento (a de máquina parada primeiro, com faixa vermelha) e as preventivas
 * vencidas e a vencer nos próximos 30 dias (faixa âmbar), com filtros por bem, centro, tipo e situação na URL. Em cartões, também a 400 px.
 */
export function MaintenancePage() {
  const search = useSearch({ strict: false }) as Filters
  const navigate = useNavigate()
  const [opening, setOpening] = useState(false)
  const centers = useListAssetWorkCenterChoices()
  const query = useListMaintenanceOrders({
    assetId: search.bem || undefined, workCenterId: search.centro || undefined, type: isOrderType(search.tipo) ? search.tipo : undefined,
    status: isOrderStatus(search.situacao) ? search.situacao : undefined,
  })
  const setFilter = (patch: Filters) => void navigate({ to: '/app/patrimonio/manutencao', search: { ...search, ...patch } as never, replace: true })
  const filtered = !!(search.bem || search.centro || search.tipo || search.situacao)

  return (
    <>
      <PageHeading title="Patrimônio: Manutenção">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">O que está parado, o que está em andamento e as preventivas que vencem.</p>
          <Can perm="assets.create"><button type="button" onClick={() => setOpening(true)} className={primary}>Nova OS de manutenção</button></Can>
        </div>
      </PageHeading>
      <AssetsTabs />
      <div role="group" aria-label="Filtros" className="mb-4 grid gap-2 sm:grid-cols-4">
        <label className="space-y-1 text-sm">
          <span className="font-medium">Centro</span>
          <select value={search.centro ?? ''} onChange={(e) => setFilter({ centro: e.target.value || undefined })} className={inputClass}>
            <option value="">Todos</option>
            {(centers.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
        <label className="space-y-1 text-sm">
          <span className="font-medium">Tipo</span>
          <select value={search.tipo ?? ''} onChange={(e) => setFilter({ tipo: e.target.value || undefined })} className={inputClass}>
            <option value="">Todos</option>
            {ORDER_TYPE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </label>
        <label className="space-y-1 text-sm">
          <span className="font-medium">Situação</span>
          <select value={search.situacao ?? ''} onChange={(e) => setFilter({ situacao: e.target.value || undefined })} className={inputClass}>
            <option value="">Abertas e em andamento</option>
            {ORDER_STATUS_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </label>
        {search.bem ? (
          <div className="flex items-end">
            <button type="button" onClick={() => setFilter({ bem: undefined })} className="min-h-11 text-sm font-semibold text-primary">Tirar o filtro do bem</button>
          </div>
        ) : null}
      </div>
      {query.isPending ? <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-24" />)}</div>
        : query.isError || !query.data ? <ErrorState title="Não foi possível carregar a manutenção." message="Verifique a conexão e tente de novo." onRetry={() => query.refetch()} />
          : query.data.orders.length === 0 && query.data.duePlans.length === 0 ? (
            filtered ? <EmptyState title="Nada com estes filtros." description="Mude ou tire os filtros para ver as outras OS." />
              : <EmptyState title="Nenhuma manutenção aberta. Crie planos preventivos para as máquinas." description="O plano fica na ficha de cada bem: escolha uma máquina e use a aba Manutenção."
                action={<Can perm="assets.edit"><Link to="/app/patrimonio/bens" search={{ tipo: 'Machine' } as never} className="inline-flex min-h-11 items-center text-sm font-semibold text-primary">Novo plano</Link></Can>} />
          ) : (
            <div className="space-y-6">
              <section aria-labelledby="os-abertas" className="space-y-3">
                <h2 id="os-abertas" className="text-[15px] font-semibold">OS de manutenção</h2>
                {query.data.orders.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma OS com estes filtros.</p>
                  : <ol className="grid gap-3 lg:grid-cols-2">{query.data.orders.map((o) => <OrderCard key={o.id} order={o} showAsset />)}</ol>}
              </section>
              {query.data.duePlans.length ? (
                <section aria-labelledby="preventivas" className="space-y-3">
                  <h2 id="preventivas" className="text-[15px] font-semibold">Preventivas vencidas e a vencer em 30 dias</h2>
                  <ol className="grid gap-3 lg:grid-cols-2">{query.data.duePlans.map((p) => <DuePlanCard key={p.id} plan={p} />)}</ol>
                </section>
              ) : null}
            </div>
          )}
      {opening ? <OpenOrderDialog open onOpenChange={setOpening} onOpened={(o) => void navigate({ to: '/app/patrimonio/manutencao/$id', params: { id: o.id }, search: { nova: '1' } as never })} /> : null}
    </>
  )
}

function DuePlanCard({ plan: p }: { plan: MaintenanceDuePlan }) {
  const due = p.frequencyType === 'Days'
    ? (p.nextDueAt ? formatDate(p.nextDueAt) : '—')
    : `${p.nextDueMeter != null ? formatMeter(p.nextDueMeter) : '—'}${p.currentMeter != null ? ` (agora ${formatMeter(p.currentMeter)})` : ''}${p.nextDueAt ? ` · previsto ${formatDate(p.nextDueAt)}` : ''}`
  return (
    <li className={`rounded-md border border-border bg-card p-3 text-sm border-l-4 ${p.overdue ? 'border-l-crit' : 'border-l-warn'}`}>
      <p className="flex flex-wrap items-center gap-2">
        <b className="font-semibold">{p.description}</b>
        <StatusChip tone={p.overdue ? 'crit' : 'warn'}>{p.overdue ? 'Vencida' : 'A vencer'}</StatusChip>
      </p>
      <p className="mt-1"><Link to="/app/patrimonio/bens/$id" params={{ id: p.assetId }} search={{ aba: 'manutencao', plano: p.id } as never} className="font-semibold hover:underline">{p.assetTag} · {p.assetName}</Link>
        {p.workCenterName ? <span className="text-muted-foreground"> · {p.workCenterName}</span> : null}</p>
      <p className="mt-1 tabular-nums">Vencimento: {due}</p>
      {p.openOrderId ? <p className="mt-1"><Link to="/app/patrimonio/manutencao/$id" params={{ id: p.openOrderId }} className="font-semibold text-primary hover:underline">Ver {p.openOrderNumber}</Link></p> : null}
    </li>
  )
}
