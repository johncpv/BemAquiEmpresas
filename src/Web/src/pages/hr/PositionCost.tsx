import { useState } from 'react'
import { resetPositionHourlyCost, setPositionHourlyCost } from '@/api/generated/bemaqui'
import type { PositionDetail } from '@/api/generated/model'
import { Field, FormSection } from '@/components/form/form'
import { MoneyInput } from '@/components/form/inputs'
import { StatusChip } from '@/components/ui/status-chip'
import { toastDone } from '@/components/ui/toast'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { DICAS } from './hr'

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
          <button type="button" disabled={manual === null} onClick={async () => { onSaved(await setPositionHourlyCost(position.id, { value: Number(manual) })); setManual(null); toastDone('Custo-hora ajustado') }}
            className="rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60">Ajustar custo-hora</button>
          {position.hourlyCostManual ? (
            <button type="button" onClick={async () => { onSaved(await resetPositionHourlyCost(position.id)); toastDone('Voltou ao calculado') }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar ao calculado</button>
          ) : null}
        </div>
      ) : null}
    </FormSection>
  )
}
