import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetMissingLaborCostsQueryKey, resetPositionHourlyCost, setPositionHourlyCost, useGetMissingLaborCosts } from '@/api/generated/bemaqui'
import type { PositionDetail } from '@/api/generated/model'
import { Field, FormSection } from '@/components/form/form'
import { MoneyInput } from '@/components/form/inputs'
import { StatusChip } from '@/components/ui/status-chip'
import { notifySuccess } from '@/components/ui/messages'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { RecalculateLaborButton } from '@/pages/production/orders/labor-costs'
import { useCan } from '@/pages/use-can'
import { DICAS } from './hr'

/**
 * Apontamentos "Sem custo-hora" do cargo (PRD-04 CA 5 e 6): só com production.approve; com o custo-hora já definido, o
 * "Recalcular apontamentos sem custo" aparece aqui (propositivo: quem acabou de ajustar o custo-hora já resolve).
 */
export function PositionMissingCosts({ positionId }: { positionId: string }) {
  const canApprove = useCan('production.approve')
  const client = useQueryClient()
  const query = useGetMissingLaborCosts({ positionId }, { query: { enabled: canApprove, refetchOnMount: 'always' } })
  if (!canApprove || !query.data?.count) return null
  const m = query.data
  return (
    <div role="note" className="space-y-2 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
      <p>{m.count === 1 ? '1 apontamento deste cargo está sem custo.' : `${m.count} apontamentos deste cargo estão sem custo.`}{m.recalculableCount === 0 ? ' Defina o custo-hora e recalcule.' : ''}</p>
      {m.recalculableCount > 0 ? <RecalculateLaborButton positionId={positionId} onDone={() => client.invalidateQueries({ queryKey: getGetMissingLaborCostsQueryKey({ positionId }) })} /> : null}
    </div>
  )
}

/** Custo-hora do cargo (CA 3 e 5): o calculado, ou o ajustado à mão com "Voltar ao calculado". */
export function PositionCost({ position, onSaved }: { position: PositionDetail; onSaved: (p: PositionDetail) => void }) {
  const canEdit = useCan('hr.edit')
  const [manual, setManual] = useState<string | null>(null)
  return (
    <FormSection title="Custo-hora">
      <p className="flex flex-wrap items-center gap-2 text-sm">
        <span className="inline-flex items-center gap-1">Custo-hora <Dica nome="Custo-hora" texto={DICAS.custoHora} /></span>
        <b className="tabular-nums">{formatMoney(position.hourlyCost)}</b>
        {position.hourlyCostManual ? <span className="inline-flex items-center gap-1"><StatusChip tone="warn">Ajustado manualmente</StatusChip><Dica nome="Ajustado manualmente" texto={DICAS.ajustado} /></span> : null}
      </p>
      {canEdit ? (
        <div className="flex flex-wrap items-end gap-2">
          <Field label="Novo custo-hora">{(p) => <MoneyInput {...p} value={manual} onChange={setManual} />}</Field>
          <button type="button" disabled={manual === null} onClick={async () => { onSaved(await setPositionHourlyCost(position.id, { value: Number(manual) })); setManual(null); notifySuccess('Custo-hora ajustado') }}
            className="rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60">Ajustar custo-hora</button>
          {position.hourlyCostManual ? (
            <button type="button" onClick={async () => { onSaved(await resetPositionHourlyCost(position.id)); notifySuccess('Voltou ao calculado') }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar ao calculado</button>
          ) : null}
        </div>
      ) : null}
    </FormSection>
  )
}
