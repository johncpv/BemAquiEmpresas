import { useQueryClient } from '@tanstack/react-query'
import { useRouterState } from '@tanstack/react-router'
import { useEffect, useRef, useState } from 'react'
import { getGetRevenueTargetsQueryKey, saveRevenueTargets, useGetRevenueTargets } from '@/api/generated/bemaqui'
import type { RevenueTargetsYear } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { monthOf } from '@/pages/dashboard/dashboard'
import { repeatForYear, sumOf, TARGETS_ANCHOR, toInputs } from './revenue-targets'

const capital = (s: string) => s.charAt(0).toUpperCase() + s.slice(1)

/**
 * Configurações → Parâmetros → Financeiro → "Metas de faturamento" (DSH-01a CA 9): meta de cada mês do ano, ≥ 0, com
 * "Repetir para o ano". Aparece para quem vê o Financeiro; grava com settings.edit (com auditoria no servidor).
 */
export function RevenueTargetsSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('finance.view')
  const [current] = useState(() => new Date().getFullYear())
  const [year, setYear] = useState(current)
  const query = useGetRevenueTargets(year, { query: { enabled: canSee } })
  const hash = useRouterState({ select: (s) => s.location.hash })
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (hash === TARGETS_ANCHOR && query.data) ref.current?.scrollIntoView({ block: 'start' })
  }, [hash, query.data])
  if (!canSee) return null
  return (
    <div id={TARGETS_ANCHOR} ref={ref} className="scroll-mt-20">
      <fieldset className="rounded-md border border-border bg-card p-4">
        <legend className="float-left mb-1 w-full text-[15px] font-semibold">Metas de faturamento</legend>
        <p className="clear-left mb-3 text-xs text-muted-foreground">A meta de cada mês aparece no Painel do proprietário, com o medidor e os dias úteis que faltam.</p>
        <label className="clear-left mb-3 flex items-center gap-2 text-sm font-medium">Ano
          <select value={year} onChange={(e) => setYear(Number(e.target.value))} className={inputClass + ' w-28'}>
            {[current - 1, current, current + 1].map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
        </label>
        {query.isPending ? <Skeleton className="h-48" /> : query.isError || !query.data ? <ErrorState title="Não foi possível carregar as metas." onRetry={() => void query.refetch()} /> : (
          <TargetsForm key={`${year}-${JSON.stringify(query.data.months ?? [])}`} targets={query.data} year={year} canEdit={canEdit} />
        )}
      </fieldset>
    </div>
  )
}

function TargetsForm({ targets, year, canEdit }: { targets: RevenueTargetsYear; year: number; canEdit: boolean }) {
  const client = useQueryClient()
  const [values, setValues] = useState<(string | null)[]>(() => toInputs(targets))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const set = (index: number, value: string | null) => setValues((prev) => prev.map((v, i) => (i === index ? value : v)))
  const save = async () => {
    setError(null)
    try {
      const saved = await saveRevenueTargets(year, { months: values.map((v) => (v === null || v === '' ? null : Number(v))) })
      client.setQueryData(getGetRevenueTargetsQueryKey(year), saved)
      void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/dashboard') })
      notifySuccess('Metas salvas')
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar as metas.', fields: {} })
    }
  }

  return (
    <div className="clear-left space-y-3">
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      <fieldset disabled={!canEdit} className="grid gap-3 sm:grid-cols-3">
        {values.map((value, i) => (
          <Field key={i} label={capital(monthOf(i + 1))} error={error?.fields[`months[${i}]`]?.[0]}>
            {(p) => <MoneyInput {...p} value={value} onChange={(v) => set(i, v)} />}
          </Field>
        ))}
      </fieldset>
      <p className="text-sm">Total do ano: <Money value={sumOf(values)} className="font-semibold" /></p>
      {canEdit ? (
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => setValues(repeatForYear(values))} className="min-h-[44px] rounded-md border border-border px-3 py-2 text-sm font-semibold sm:min-h-0">Repetir para o ano</button>
          <button type="button" onClick={() => void save()} className="min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground sm:min-h-0">Salvar metas</button>
        </div>
      ) : <p className="text-xs text-muted-foreground">Só quem edita as Configurações muda as metas.</p>}
    </div>
  )
}
