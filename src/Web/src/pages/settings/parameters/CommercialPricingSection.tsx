import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetCommercialParametersQueryKey, updateCommercialParameters, useGetCommercialParameters } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Field, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { PercentInput } from '@/components/form/inputs'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'

/** % padrão de markup ou margem dos orçamentos (COM-01a CA 12): vazio no seed. */
export function CommercialPricingSection({ canEdit }: { canEdit: boolean }) {
  const query = useGetCommercialParameters()
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return null
  return <PricingForm key={`${query.data.defaultPct}-${query.data.validityDays}`} defaultPct={query.data.defaultPct ?? null} validity={query.data.validityDays ?? 15} mode={query.data.pricingMode} canEdit={canEdit} />
}

function PricingForm({ defaultPct, validity, mode, canEdit }: { defaultPct: number | null; validity: number; mode: string; canEdit: boolean }) {
  const [days, setDays] = useState(String(validity))
  const client = useQueryClient()
  const [pct, setPct] = useState<string | null>(defaultPct === null ? null : String(defaultPct))
  const [error, setError] = useState<string | undefined>()
  const save = async () => {
    try {
      const saved = await updateCommercialParameters({ defaultPct: pct === null || pct === '' ? null : Number(pct), validityDays: Number(days || 15) })
      client.setQueryData(getGetCommercialParametersQueryKey(), saved)
      notifySuccess('% padrão salvo')
      setError(undefined)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o % padrão.')
    }
  }

  const label = mode === 'Margin' ? '% padrão de margem' : '% padrão de markup'
  return (
    <FormSection title="Orçamentos" description="O % que a empresa costuma aplicar. O orçamento sugere o preço com ele, e cada item pode ter o seu.">
      <fieldset disabled={!canEdit} className="space-y-3">
        <Field label={label} error={error} hint={mode === 'Margin' ? 'Margem sobre o preço, menor que 100%.' : 'Markup sobre o custo.'}>{(p) => <PercentInput {...p} scale={4} value={pct} onChange={setPct} />}</Field>
        <Field label="Validade padrão do orçamento (dias)" hint="Todo orçamento novo vem com hoje mais estes dias.">{(p) => <input {...p} inputMode="numeric" maxLength={3} value={days} onChange={(e) => setDays(e.target.value.replace(/[^0-9]/g, ''))} className={inputClass + ' tabular-nums'} />}</Field>
        {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar % padrão</button> : null}
      </fieldset>
    </FormSection>
  )
}
