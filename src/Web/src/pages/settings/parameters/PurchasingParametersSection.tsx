import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetPurchasingParametersQueryKey, updatePurchasingParameters, useGetPurchasingParameters } from '@/api/generated/bemaqui'
import type { PurchasingParametersView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'

const LEAD_DICA = 'Quantos dias antes da necessidade a compra precisa ser feita. A falta da BOM pede o material para a data prometida do projeto menos este prazo; a solicitação manual e a do estoque mínimo sugerem hoje mais este prazo.'

/** Compras (CMP-01, ADR 0016 adendo M6): o prazo padrão de compra (15 dias, de 0 a 365). Salvar com settings.edit; com o histórico. */
export function PurchasingParametersSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('purchasing.view')
  const query = useGetPurchasingParameters({ query: { enabled: canSee } })
  if (!canSee) return null
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return null
  return <PurchasingForm key={query.data.defaultLeadDays} params={query.data} canEdit={canEdit} />
}

function PurchasingForm({ params, canEdit }: { params: PurchasingParametersView; canEdit: boolean }) {
  const client = useQueryClient()
  const [days, setDays] = useState(String(params.defaultLeadDays))
  const [error, setError] = useState<string | undefined>()
  const value = Number(days)
  const invalid = days === '' || value < 0 || value > 365 ? 'Informe de 0 a 365 dias.' : undefined
  const save = async () => {
    try {
      const saved = await updatePurchasingParameters({ defaultLeadDays: value })
      client.setQueryData(getGetPurchasingParametersQueryKey(), saved)
      void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/PurchasingParameter/') })
      notifySuccess('Parâmetros de compras salvos')
      setError(undefined)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar os parâmetros de compras.')
    }
  }
  return (
    <FormSection title="Compras" description="Prazo usado para calcular quando o material precisa estar comprado.">
      <fieldset disabled={!canEdit} className="space-y-3">
        <Field label="Prazo padrão de compra (dias)" dica={LEAD_DICA} error={invalid ?? error} hint="Padrão 15 dias.">
          {(p) => <input {...p} inputMode="numeric" maxLength={3} value={days} onChange={(e) => setDays(e.target.value.replace(/[^0-9]/g, ''))} className={inputClass + ' tabular-nums'} />}
        </Field>
        {canEdit ? <button type="button" disabled={value === params.defaultLeadDays || !!invalid} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar parâmetros de compras</button> : null}
      </fieldset>
      {params.defaultLeadDaysId ? (
        <details className="rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="PurchasingParameter" id={params.defaultLeadDaysId} /></div>
        </details>
      ) : null}
    </FormSection>
  )
}
