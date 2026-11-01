import { Link, useNavigate } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { useGetAssetMaintenance } from '@/api/generated/bemaqui'
import type { AssetDetail, MaintenanceOrderListItem, MaintenancePlanView } from '@/api/generated/model'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { hasMaintenancePlan } from './assets'
import { frequencyText, formatMeter, MAINTENANCE_DICAS, nextDueText, ORDER_STATUS_LABELS, ORDER_STATUS_TONES, ORDER_TYPE_LABELS, planChip } from './maintenance'
import { MeterDialog, OpenOrderDialog, PlanDialog } from './MaintenanceDialogs'

const button = 'inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

type Dialog = { kind: 'plan'; plan?: MaintenancePlanView } | { kind: 'meter' } | { kind: 'order'; plan?: MaintenancePlanView } | null

/**
 * Aba Manutenção do bem (PAT-03a CA 9): os planos (próximo vencimento, chip "Vencida" crítico quando passou, "A vencer" na antecedência) e as OS
 * do bem da mais recente à mais antiga, em cartões. "Novo plano" e "Registrar horímetro" com `assets.edit`; "Nova OS de manutenção" com
 * `assets.create`. `?plano=` (link do aviso e da pauta) destaca o plano; `?acao=nova-os` (atalho da leitura da etiqueta) abre a corretiva.
 */
export function AssetMaintenance({ asset, highlightPlan, startOrder, fromFloor }: { asset: AssetDetail; highlightPlan?: string; startOrder?: boolean; fromFloor?: boolean }) {
  const query = useGetAssetMaintenance(asset.id)
  const navigate = useNavigate()
  const canEdit = useCan('assets.edit')
  const canCreate = useCan('assets.create')
  const disposed = asset.status === 'Disposed'
  const [dialog, setDialog] = useState<Dialog>(startOrder && canCreate && !disposed ? { kind: 'order' } : null)
  useEffect(() => {
    if (highlightPlan && query.data) document.getElementById(`plano-${highlightPlan}`)?.scrollIntoView({ block: 'center' })
  }, [highlightPlan, query.data])

  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-24" />)}</div>
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar a manutenção do bem." message="Verifique a conexão e tente de novo." onRetry={() => query.refetch()} />
  const { plans, orders, lastReading } = query.data
  const hasHours = plans.some((p) => p.frequencyType === 'Hours')
  const close = (o: boolean) => { if (!o) setDialog(null) }
  const actions = disposed ? null : (
    <div className="flex flex-wrap gap-2">
      {canCreate ? <button type="button" onClick={() => setDialog({ kind: 'order' })} className={primary}>Nova OS de manutenção</button> : null}
      {canEdit ? <button type="button" onClick={() => setDialog({ kind: 'plan' })} className={button}>Novo plano</button> : null}
      {canEdit && (hasHours || lastReading) ? <button type="button" onClick={() => setDialog({ kind: 'meter' })} className={button}>Registrar horímetro</button> : null}
    </div>
  )

  return (
    <div className="space-y-6">
      {disposed ? <p className="text-sm text-muted-foreground">Bem baixado: os planos ficam inativos e não abrem OS. Reative o bem para voltar a cuidar da manutenção.</p> : null}
      {actions}
      <section aria-labelledby="planos-titulo" className="space-y-3">
        <h2 id="planos-titulo" className="text-[15px] font-semibold">Planos preventivos</h2>
        {lastReading ? (
          <p className="inline-flex items-center text-sm text-muted-foreground">
            Horímetro: <b className="ml-1 tabular-nums text-foreground">{formatMeter(lastReading.value)}</b>&nbsp;em {formatDate(lastReading.readOn)}
            <Dica nome="Horímetro" texto={MAINTENANCE_DICAS.horimetro} />
          </p>
        ) : null}
        {plans.length === 0 ? (
          <EmptyState title="Sem plano de manutenção."
            description={hasMaintenancePlan(asset.type) ? 'Crie o plano preventivo: o sistema avisa antes do vencimento e abre a OS no dia.' : 'Os planos preventivos são mais comuns em máquinas e veículos, mas você pode criar um para qualquer bem.'}
            action={canEdit && !disposed ? <button type="button" onClick={() => setDialog({ kind: 'plan' })} className={button}>Novo plano</button> : undefined} />
        ) : (
          <ul className="grid gap-3 sm:grid-cols-2">
            {plans.map((p) => (
              <PlanCard key={p.id} plan={p} highlighted={p.id === highlightPlan} canEdit={canEdit && !disposed} canCreate={canCreate && !disposed}
                onEdit={() => setDialog({ kind: 'plan', plan: p })} onOpen={() => setDialog({ kind: 'order', plan: p })} />
            ))}
          </ul>
        )}
      </section>
      <section aria-labelledby="os-titulo" className="space-y-3">
        <h2 id="os-titulo" className="text-[15px] font-semibold">OS de manutenção</h2>
        {orders.length === 0 ? <EmptyState title="Nenhuma OS de manutenção deste bem." description="A corretiva se abre quando algo quebra; a preventiva, pelo plano." />
          : <ol className="space-y-3">{orders.map((o) => <OrderCard key={o.id} order={o} />)}</ol>}
      </section>
      {dialog?.kind === 'plan' ? <PlanDialog assetId={asset.id} plan={dialog.plan} open onOpenChange={close} /> : null}
      {dialog?.kind === 'meter' ? <MeterDialog assetId={asset.id} last={lastReading?.value} open onOpenChange={close} /> : null}
      {dialog?.kind === 'order' ? (
        <OpenOrderDialog asset={asset} plan={dialog.plan} open onOpenChange={close}
          onOpened={(o) => void (fromFloor ? navigate({ to: '/fab/manutencao/$id', params: { id: o.id } }) : navigate({ to: '/app/patrimonio/manutencao/$id', params: { id: o.id }, search: { nova: '1' } as never }))} />
      ) : null}
    </div>
  )
}

function PlanCard({ plan: p, highlighted, canEdit, canCreate, onEdit, onOpen }: {
  plan: MaintenancePlanView; highlighted: boolean; canEdit: boolean; canCreate: boolean; onEdit: () => void; onOpen: () => void
}) {
  const chip = planChip(p)
  const stripe = p.isActive && p.overdue ? 'border-l-4 border-l-crit' : p.isActive && p.dueSoon ? 'border-l-4 border-l-warn' : ''
  return (
    <li id={`plano-${p.id}`} className={`space-y-1 rounded-md border bg-card p-3 text-sm ${highlighted ? 'border-primary ring-2 ring-primary' : 'border-border'} ${stripe}`}>
      <p className="flex flex-wrap items-center gap-2">
        <b className="font-semibold">{p.description}</b>
        {chip ? <StatusChip tone={chip.tone}>{chip.label}</StatusChip> : null}
      </p>
      <p className="text-muted-foreground">{frequencyText(p.frequencyType, p.frequencyValue)} · aviso {p.leadDays} {p.leadDays === 1 ? 'dia' : 'dias'} antes</p>
      <p>Próxima preventiva: <b className="tabular-nums">{nextDueText(p)}</b></p>
      <p className="text-muted-foreground">
        Última execução: {p.lastDoneAt ? formatDate(p.lastDoneAt) : '—'}{p.lastDoneMeter != null ? ` · ${formatMeter(p.lastDoneMeter)}` : ''}
        {p.defaultSupplierName ? ` · Executor: ${p.defaultSupplierName}` : ' · Equipe interna'}
      </p>
      {p.checklist.length ? <p className="text-muted-foreground">Checklist: {p.checklist.length} {p.checklist.length === 1 ? 'item' : 'itens'}</p> : null}
      <div className="flex flex-wrap gap-2 pt-1">
        {p.openOrderId ? <Link to="/app/patrimonio/manutencao/$id" params={{ id: p.openOrderId }} className={button + ' text-primary'}>Ver {p.openOrderNumber}</Link>
          : canCreate && p.isActive ? <button type="button" onClick={onOpen} className={button}>Abrir OS agora</button> : null}
        {canEdit ? <button type="button" onClick={onEdit} className={button}>Editar plano</button> : null}
      </div>
    </li>
  )
}

/** Cartão da OS (CA 9 e 10): número, tipo, datas, executor e situação; faixa vermelha na OS de máquina parada. */
export function OrderCard({ order: o, showAsset = false }: { order: MaintenanceOrderListItem; showAsset?: boolean }) {
  const open = o.status === 'Open' || o.status === 'InProgress'
  return (
    <li className={`rounded-md border border-border bg-card p-3 text-sm ${open && o.machineStopped ? 'border-l-4 border-l-crit' : ''}`}>
      <p className="flex flex-wrap items-center gap-2">
        <Link to="/app/patrimonio/manutencao/$id" params={{ id: o.id }} className="font-semibold tabular-nums text-primary hover:underline">{o.number}</Link>
        <StatusChip tone={ORDER_STATUS_TONES[o.status]}>{ORDER_STATUS_LABELS[o.status]}</StatusChip>
        <span className="text-muted-foreground">{ORDER_TYPE_LABELS[o.type]}{o.planDescription ? ` · ${o.planDescription}` : ''}</span>
        {open && o.machineStopped ? <StatusChip tone="crit">Máquina parada</StatusChip> : null}
      </p>
      {showAsset ? <p className="mt-1"><Link to="/app/patrimonio/bens/$id" params={{ id: o.assetId }} className="font-semibold hover:underline">{o.assetTag} · {o.assetName}</Link>{o.workCenterName ? <span className="text-muted-foreground"> · {o.workCenterName}</span> : null}</p> : null}
      <p className="mt-1 text-muted-foreground tabular-nums">
        Agendada para {formatDate(o.scheduledOn)}
        {o.startedAt ? ` · iniciada ${formatDateTime(o.startedAt)}` : ''}
        {o.doneAt ? ` · concluída ${formatDateTime(o.doneAt)}` : ''}
        {o.cancelledAt ? ` · cancelada ${formatDateTime(o.cancelledAt)}` : ''}
      </p>
      <p className="mt-1 text-muted-foreground">Executor: {o.supplierName ?? 'Equipe interna'}</p>
    </li>
  )
}
