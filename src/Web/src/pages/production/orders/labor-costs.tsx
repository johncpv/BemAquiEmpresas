import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import {
  getGetOrderLaborCostsQueryKey, getMissingLaborCosts, recalculateMissingLaborCosts, useGetLaborEntryCostHistory,
} from '@/api/generated/bemaqui'
import type { LaborCostMissingView, LaborEntryCostView, MissingCostPosition } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { hours } from './fabrication'
import { COST_ORIGIN, LABOR_COST_DICAS, laborCostMarks, recalcImpact, useOrderLaborCosts } from './labor-cost'

/** Célula de custo do apontamento (CA 14): valor, custo-hora do cargo e as marcas, com "Histórico do custo" (CA 17). */
export function LaborCostCell({ orderId, cost }: { orderId: string; cost: LaborEntryCostView | undefined }) {
  if (!cost || cost.pending) return <span className="text-muted-foreground">—</span>
  return (
    <span className="inline-flex flex-col items-end gap-1">
      <Money value={cost.cost} />
      {cost.hourlyCostSnapshot !== null ? <span className="text-xs text-muted-foreground tabular-nums">{formatMoney(cost.hourlyCostSnapshot)}/h</span> : null}
      <CostMarks cost={cost} />
      <CostHistoryButton orderId={orderId} entryId={cost.entryId} />
    </span>
  )
}

export function CostMarks({ cost }: { cost: Pick<LaborEntryCostView, 'costMissing' | 'costBackfilled' | 'pending'> }) {
  return (
    <>
      {laborCostMarks(cost).map((m) => m === 'Sem custo-hora'
        ? <span key={m} className="inline-flex items-center"><StatusChip tone="warn">{m}</StatusChip><Dica nome={m} texto={LABOR_COST_DICAS.semCusto} /></span>
        : <span key={m} className="inline-flex items-center"><Tag>{m}</Tag><Dica nome={m} texto={LABOR_COST_DICAS.implantacao} /></span>)}
    </>
  )
}

/** Histórico do custo do apontamento (CA 17): snapshot, horas, valor, versão, origem e quem fez. */
function CostHistoryButton({ orderId, entryId }: { orderId: string; entryId: string }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" className="text-xs font-semibold text-primary hover:underline" onClick={() => setOpen(true)}>Histórico do custo</button>
      {open ? <CostHistoryDialog orderId={orderId} entryId={entryId} onClose={() => setOpen(false)} /> : null}
    </>
  )
}

function CostHistoryDialog({ orderId, entryId, onClose }: { orderId: string; entryId: string; onClose: () => void }) {
  const query = useGetLaborEntryCostHistory(orderId, entryId)
  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title="Histórico do custo do apontamento" description="Cada versão do custo, com o custo-hora do cargo, as horas, a origem e quem fez.">
      {query.isPending ? <Skeleton className="h-24" /> : query.isError ? <ErrorState title="Não foi possível carregar o histórico do custo." onRetry={() => query.refetch()} /> : (
        <ol className="space-y-2 text-sm" aria-label="Versões do custo">
          {query.data.map((h) => (
            <li key={h.costVersion} className="rounded-md border border-border p-2">
              <p className="font-semibold">Versão {h.costVersion} · {COST_ORIGIN[h.origin] ?? h.origin}</p>
              <p className="tabular-nums">{hours(h.hours)} h × {h.hourlyCostSnapshot === null ? 'sem custo-hora' : `${formatMoney(h.hourlyCostSnapshot)}/h`} = <Money value={h.amount} /> (antes <Money value={h.previousAmount} />)</p>
              <p className="text-xs text-muted-foreground">{formatDateTime(h.recordedAt)} · {h.recordedByName ?? 'Sistema (implantação)'}</p>
            </li>
          ))}
        </ol>
      )}
    </SidePanel>
  )
}

/** "Abrir cargo" do aviso "Sem custo-hora" (CA 5): leva ao painel do cargo em RH → Cargos. */
export function OpenPositionLink({ position }: { position: MissingCostPosition }) {
  if (!position.positionId) return <span className="text-xs text-muted-foreground">Defina o cargo do funcionário em RH → Funcionários.</span>
  return <Link to="/app/rh/cargos" search={{ cargo: position.positionId } as never} className="text-sm font-semibold text-primary hover:underline">Abrir cargo</Link>
}

/**
 * "Recalcular apontamentos sem custo" (CA 6), na OS e no cargo, só com production.approve: o diálogo mostra o impacto
 * ("{n} apontamentos · R$ 0,00 → R$ {total}") e só recalcula os marcados "Sem custo-hora" cujo cargo já tem custo-hora.
 */
export function RecalculateLaborButton({ positionId, orderId, onDone }: { positionId?: string; orderId?: string; onDone: () => Promise<void> | void }) {
  const canApprove = useCan('production.approve')
  const [impact, setImpact] = useState<LaborCostMissingView | null>(null)
  const [error, setError] = useState(false)
  if (!canApprove) return null
  const load = async () => {
    setError(false)
    try {
      setImpact(await getMissingLaborCosts({ positionId, orderId }))
    } catch {
      setError(true)
    }
  }
  return (
    <ConfirmDialog verb="Recalcular apontamentos sem custo" done="Apontamentos recalculados" title="Recalcular apontamentos sem custo?" size="lg"
      description="Os apontamentos marcados Sem custo-hora recebem o custo-hora atual do cargo. Os que já têm custo não mudam."
      onOpen={() => { setImpact(null); void load() }}
      impact={impact ? <p className="tabular-nums">{recalcImpact(impact)}</p> : error ? <p>Não foi possível calcular o impacto. Feche e tente de novo.</p> : <p>Calculando o impacto…</p>}
      preconditions={[{ ok: !!impact && impact.recalculableCount > 0, text: !impact ? 'Aguarde o cálculo do impacto.' : 'Defina o custo-hora do cargo antes de recalcular.' }]}
      onConfirm={async () => { await recalculateMissingLaborCosts({ positionId: positionId ?? null, orderId: orderId ?? null }); await onDone() }}
      trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Recalcular apontamentos sem custo</button>} />
  )
}

/** Aviso acionável dos apontamentos sem custo-hora (CA 5), com "Abrir cargo" e o recálculo. */
export function MissingCostNotice({ orderId, positions, onDone }: { orderId: string; positions: MissingCostPosition[]; onDone: () => Promise<void> }) {
  if (positions.length === 0) return null
  const total = positions.reduce((s, p) => s + p.count, 0)
  return (
    <div role="note" className="space-y-2 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
      <p>{total === 1 ? '1 apontamento está sem custo-hora.' : `${total} apontamentos estão sem custo-hora.`} As horas contam, mas o custo fica zero até recalcular.</p>
      <ul className="space-y-1">
        {positions.map((p) => (
          <li key={p.positionId ?? 'sem-cargo'} className="flex flex-wrap items-center gap-2">
            <span>{p.positionName}: {p.count} {p.count === 1 ? 'apontamento' : 'apontamentos'}{p.hasHourlyCost ? ' (o cargo já tem custo-hora)' : ''}</span>
            <OpenPositionLink position={p} />
          </li>
        ))}
      </ul>
      {positions.some((p) => p.hasHourlyCost) ? <RecalculateLaborButton orderId={orderId} onDone={onDone} /> : null}
    </div>
  )
}

/**
 * Aba Custos da OS (PRD-04 CA 14): linha Mão de obra (a EST-03 acrescenta Material), horas e custo por etapa (cartões a 400 px) e
 * o aviso dos apontamentos sem custo-hora. Só com projects.view_cost (a aba nem aparece sem a permissão).
 */
export function FabricationCostsTab({ orderId }: { orderId: string }) {
  const client = useQueryClient()
  const { query } = useOrderLaborCosts(orderId)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os custos da OS." message="Tente de novo. Se continuar, fale com o administrador." onRetry={() => query.refetch()} />
  const c = query.data
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetOrderLaborCostsQueryKey(orderId) }) }
  return (
    <div className="space-y-3 text-sm">
      <MissingCostNotice orderId={orderId} positions={c.missingPositions} onDone={refresh} />
      <div className="rounded-md border border-border bg-card p-3">
        <p className="flex flex-wrap items-center justify-between gap-2">
          <b className="inline-flex items-center">Mão de obra{c.includesBackfill ? <span className="ml-1 inline-flex items-center"><Tag>Calculado na implantação</Tag><Dica nome="Calculado na implantação" texto={LABOR_COST_DICAS.implantacao} /></span> : null}</b>
          <span className="tabular-nums"><span className="text-muted-foreground">{hours(c.totalHours)} h · </span><Money value={c.laborCost} className="font-semibold" /></span>
        </p>
        <p className="mt-1 inline-flex items-center text-xs text-muted-foreground">Horas apontadas × custo-hora do cargo<Dica nome="Custo-hora" texto={LABOR_COST_DICAS.custoHora} /></p>
      </div>
      {c.steps.length === 0 ? <p className="text-muted-foreground">Nenhuma hora apontada ainda. As horas entram quando o operador finaliza a etapa no app do chão de fábrica.</p> : (
        <>
          <table className="hidden w-full sm:table" aria-label="Mão de obra por etapa">
            <thead className="text-xs text-muted-foreground">
              <tr><th className="py-1 text-left">Etapa</th><th className="py-1 text-right">Horas</th><th className="py-1 text-right">Custo</th></tr>
            </thead>
            <tbody>
              {c.steps.map((s) => (
                <tr key={s.stepId} className="border-t border-border">
                  <td className="py-1.5">{s.stepCode} · {s.stepName}{s.missingCount > 0 ? <span className="ml-2"><StatusChip tone="warn">Sem custo-hora</StatusChip></span> : null}</td>
                  <td className="text-right tabular-nums">{hours(s.hours)}</td>
                  <td className="text-right"><Money value={s.cost} /></td>
                </tr>
              ))}
            </tbody>
          </table>
          <ul className="space-y-2 sm:hidden" aria-label="Mão de obra por etapa em cartões">
            {c.steps.map((s) => (
              <li key={s.stepId} className="rounded-md border border-border bg-card p-3">
                <b>{s.stepCode} · {s.stepName}</b>
                <p className="tabular-nums">{hours(s.hours)} h · <Money value={s.cost} /></p>
                {s.missingCount > 0 ? <StatusChip tone="warn">Sem custo-hora</StatusChip> : null}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}