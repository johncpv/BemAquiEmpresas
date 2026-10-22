import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetProductionParametersQueryKey, updateProductionParameters, useGetProductionParameters } from '@/api/generated/bemaqui'
import type { ProductionParametersView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'

const SHOW_DICA = 'Ligado, quem vê custo do projeto vê o custo-hora do cargo e o custo de cada apontamento. Desligado, só o total por etapa e por OS.'

/**
 * Produção (decisões do PO de 22/10/2026 na PRD-04): "Exibir custo-hora em cada apontamento" (padrão desligado) e o limite do
 * apontamento aberto (padrão 12 h, de 1 a 72). Salvar com settings.edit; cada parâmetro tem o histórico de alterações.
 */
export function ProductionParametersSection({ canEdit }: { canEdit: boolean }) {
  const query = useGetProductionParameters()
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return null
  return <ProductionForm key={`${query.data.showLaborEntryCost}-${query.data.openLaborAlertHours}`} params={query.data} canEdit={canEdit} />
}

function ProductionForm({ params, canEdit }: { params: ProductionParametersView; canEdit: boolean }) {
  const client = useQueryClient()
  const [show, setShow] = useState(params.showLaborEntryCost)
  const [hours, setHours] = useState(String(params.openLaborAlertHours))
  const [error, setError] = useState<string | undefined>()
  const value = Number(hours)
  const invalid = !hours || value < 1 || value > 72 ? 'Informe de 1 a 72 horas.' : undefined
  const dirty = show !== params.showLaborEntryCost || value !== params.openLaborAlertHours
  const save = async () => {
    try {
      const saved = await updateProductionParameters({ showLaborEntryCost: show, openLaborAlertHours: value })
      client.setQueryData(getGetProductionParametersQueryKey(), saved)
      void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/ProductionParameter/') })
      notifySuccess('Parâmetros da produção salvos')
      setError(undefined)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar os parâmetros da produção.')
    }
  }
  return (
    <FormSection title="Produção" description="Como o custo das horas aparece nas OS e quando um apontamento aberto vira alerta.">
      <fieldset disabled={!canEdit} className="space-y-3">
        <Field label="Exibir custo-hora em cada apontamento" dica={SHOW_DICA} hint="O total de mão de obra da OS e do projeto aparece sempre para quem vê custo.">
          {(p) => <span className="flex items-center gap-2 text-sm"><input {...p} type="checkbox" checked={show} onChange={(e) => setShow(e.target.checked)} className="size-4 accent-primary" /><span aria-hidden="true">{show ? 'Ligado' : 'Desligado'}</span></span>}
        </Field>
        <Field label="Apontamento aberto vira alerta depois de (horas)" error={invalid ?? error} hint="Usado na tela Apontamentos abertos. Padrão 12 h.">
          {(p) => <input {...p} inputMode="numeric" maxLength={2} value={hours} onChange={(e) => setHours(e.target.value.replace(/[^0-9]/g, ''))} className={inputClass + ' tabular-nums'} />}
        </Field>
        {canEdit ? <button type="button" disabled={!dirty || !!invalid} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar parâmetros da produção</button> : null}
      </fieldset>
      {params.showLaborEntryCostId || params.openLaborAlertHoursId ? (
        <details className="rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3 space-y-3">
            {params.showLaborEntryCostId ? <RecordHistory entity="ProductionParameter" id={params.showLaborEntryCostId} /> : null}
            {params.openLaborAlertHoursId ? <RecordHistory entity="ProductionParameter" id={params.openLaborAlertHoursId} /> : null}
          </div>
        </details>
      ) : null}
    </FormSection>
  )
}