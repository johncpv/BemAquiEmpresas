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
import { StatusChip } from '@/components/ui/status-chip'
import { useCan } from '@/pages/use-can'
import { INSTALLATION_DICA, INSTALLATION_PENDING_LABEL, installationDaysError } from './production-parameters'

const SHOW_DICA = 'Ligado, quem vê custo do projeto vê o custo-hora do cargo e o custo de cada apontamento. Desligado, só o total por etapa e por OS.'

/**
 * Produção (decisões do PO de 22/10/2026 na PRD-04): "Exibir custo-hora em cada apontamento" (padrão desligado), o limite do
 * apontamento aberto (padrão 12 h, de 1 a 72) e, na PLN-01b, o prazo padrão de instalação (5 dias úteis, de 0 a 120, "A confirmar" até a
 * empresa conferir; salvar refaz a entrega prevista dos projetos). Salvar com settings.edit; cada parâmetro tem o histórico de alterações.
 */
export function ProductionParametersSection({ canEdit }: { canEdit: boolean }) {
  // Os parâmetros são do módulo production (como os limites do Finance): a seção só aparece para quem vê a produção.
  const canSee = useCan('production.view')
  const query = useGetProductionParameters({ query: { enabled: canSee } })
  if (!canSee) return null
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return null
  const p = query.data
  return <ProductionForm key={[p.showLaborEntryCost, p.openLaborAlertHours, p.installationDays, p.installationDaysConfirmed].join('|')} params={p} canEdit={canEdit} />
}

function ProductionForm({ params, canEdit }: { params: ProductionParametersView; canEdit: boolean }) {
  const client = useQueryClient()
  const savedDays = params.installationDays ?? 5
  const savedConfirmed = params.installationDaysConfirmed ?? false
  const [show, setShow] = useState(params.showLaborEntryCost)
  const [hours, setHours] = useState(String(params.openLaborAlertHours))
  const [days, setDays] = useState(String(savedDays))
  const [confirmed, setConfirmed] = useState(savedConfirmed)
  const [error, setError] = useState<string | undefined>()
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const value = Number(hours)
  const invalid = !hours || value < 1 || value > 72 ? 'Informe de 1 a 72 horas.' : undefined
  const daysInvalid = installationDaysError(days)
  const dirty = show !== params.showLaborEntryCost || value !== params.openLaborAlertHours || Number(days) !== savedDays || confirmed !== savedConfirmed
  const save = async () => {
    try {
      const saved = await updateProductionParameters({ showLaborEntryCost: show, openLaborAlertHours: value, installationDays: Number(days), installationDaysConfirmed: confirmed })
      client.setQueryData(getGetProductionParametersQueryKey(), saved)
      void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/ProductionParameter/') })
      notifySuccess(Number(days) !== savedDays ? 'Parâmetros da produção salvos. A entrega prevista dos projetos está sendo recalculada.' : 'Parâmetros da produção salvos')
      setError(undefined)
      setFieldErrors({})
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fields).length) setFieldErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]])))
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar os parâmetros da produção.')
    }
  }
  const ids = [params.showLaborEntryCostId, params.openLaborAlertHoursId, params.installationDaysId].filter((id): id is string => !!id)
  return (
    <FormSection title="Produção" description="Como o custo das horas aparece nas OS, quando um apontamento aberto vira alerta e o prazo de instalação da entrega prevista.">
      <fieldset disabled={!canEdit} className="space-y-3">
        <Field label="Exibir custo-hora em cada apontamento" dica={SHOW_DICA} hint="O total de mão de obra da OS e do projeto aparece sempre para quem vê custo.">
          {(p) => <span className="flex items-center gap-2 text-sm"><input {...p} type="checkbox" checked={show} onChange={(e) => setShow(e.target.checked)} className="size-4 accent-primary" /><span aria-hidden="true">{show ? 'Ligado' : 'Desligado'}</span></span>}
        </Field>
        <Field label="Apontamento aberto vira alerta depois de (horas)" error={invalid ?? fieldErrors.openLaborAlertHours} hint="Usado na tela Apontamentos abertos. Padrão 12 h.">
          {(p) => <input {...p} inputMode="numeric" maxLength={2} value={hours} onChange={(e) => setHours(e.target.value.replace(/[^0-9]/g, ''))} className={inputClass + ' tabular-nums'} />}
        </Field>
        <div role="group" aria-label="Prazo de instalação" className="space-y-3 rounded-md border border-border p-3">
          <p className="flex flex-wrap items-center gap-2 text-sm font-semibold">
            Entrega prevista dos projetos
            {!savedConfirmed ? <StatusChip tone="warn">{INSTALLATION_PENDING_LABEL}</StatusChip> : null}
          </p>
          <Field label="Prazo padrão de instalação (dias úteis)" dica={INSTALLATION_DICA} error={daysInvalid ?? fieldErrors.installationDays} hint="Valor sugerido: 5 dias úteis. Confira com o prazo real dos instaladores.">
            {(p) => <input {...p} inputMode="numeric" maxLength={3} value={days} onChange={(e) => setDays(e.target.value.replace(/[^0-9]/g, ''))} className={inputClass + ' tabular-nums'} />}
          </Field>
          <label className="flex min-h-[44px] items-center gap-2 text-sm">
            <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} className="size-4 accent-primary" />
            Prazo conferido com a empresa
          </label>
        </div>
        {error && Object.keys(fieldErrors).length === 0 ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
        {canEdit ? <button type="button" disabled={!dirty || !!invalid || !!daysInvalid} onClick={() => void save()} className="min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar parâmetros da produção</button> : null}
      </fieldset>
      {ids.length > 0 ? (
        <details className="rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3 space-y-3">
            {ids.map((id) => <RecordHistory key={id} entity="ProductionParameter" id={id} />)}
          </div>
        </details>
      ) : null}
    </FormSection>
  )
}
