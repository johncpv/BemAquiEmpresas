import { useState } from 'react'
import { updatePositionSalary, useGetPositionSalary } from '@/api/generated/bemaqui'
import type { PositionDetail, PositionSalaryDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { MoneyInput } from '@/components/form/inputs'
import { Skeleton } from '@/components/ui/states'
import { toastDone } from '@/components/ui/toast'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { average, DICAS, hourlyCost } from './hr'

/**
 * Faixa salarial do cargo (CAD-04a CA 2 e 10): só com hr.view_sensitive, lida pela rota /sensitive. O salário de referência
 * vem como a média da faixa enquanto não for editado; a prévia mostra o custo-hora antes de salvar.
 */
export function PositionSalary({ position, chargesPct, onSaved }: { position: PositionDetail | null; chargesPct: number; onSaved: (p: PositionDetail) => void }) {
  const canSee = useCan('hr.view_sensitive')
  const salary = useGetPositionSalary(position?.id ?? '', { query: { enabled: canSee && !!position } })
  if (!canSee) {
    return (
      <FormSection title="Faixa salarial">
        <p className="inline-flex items-center gap-1 text-sm text-muted-foreground">Dados protegidos <Dica nome="Dados protegidos" texto={DICAS.protegidos} /></p>
      </FormSection>
    )
  }

  if (!position) return <FormSection title="Faixa salarial"><p className="text-sm text-muted-foreground">Salve o cargo para informar a faixa salarial.</p></FormSection>
  if (salary.isPending) return <Skeleton className="h-32" />
  return <SalaryForm key={salary.data?.id ?? 'x'} position={position} current={salary.data ?? null} chargesPct={chargesPct} onSaved={onSaved} />
}

function SalaryForm({ position, current, chargesPct, onSaved }: { position: PositionDetail; current: PositionSalaryDetail | null; chargesPct: number; onSaved: (p: PositionDetail) => void }) {
  const canEdit = useCan('hr.edit')
  const str = (n: number | null | undefined) => (n === null || n === undefined ? null : String(n))
  const [min, setMin] = useState(str(current?.salaryMin))
  const [max, setMax] = useState(str(current?.salaryMax))
  const [reference, setReference] = useState(str(current?.referenceSalary))
  const [touched, setTouched] = useState(current?.referenceSalary !== null && current?.referenceSalary !== undefined && current.referenceSalary !== average(current.salaryMin, current.salaryMax))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const num = (s: string | null) => (s === null || s === '' ? null : Number(s))
  const suggested = average(num(min), num(max))
  const effective = touched ? num(reference) : suggested
  const preview = hourlyCost(effective, chargesPct, position.monthlyHours)

  const save = async () => {
    try {
      const saved = await updatePositionSalary(position.id, { salaryMin: num(min), salaryMax: num(max), referenceSalary: touched ? num(reference) : null })
      toastDone('Faixa salarial salva')
      onSaved(saved)
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a faixa salarial.', fields: {} })
    }
  }

  return (
    <FormSection title="Faixa salarial" description="Dado protegido: só quem tem acesso aos dados sensíveis do RH vê.">
      <fieldset disabled={!canEdit} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="Salário mínimo" error={error?.fields.salaryMin?.[0]}>{(p) => <MoneyInput {...p} value={min} onChange={setMin} />}</Field>
          <Field label="Salário máximo" error={error?.fields.salaryMax?.[0]}>{(p) => <MoneyInput {...p} value={max} onChange={setMax} />}</Field>
        </div>
        <Field label="Salário de referência" hint={touched ? 'Informado à mão.' : 'Média da faixa, enquanto você não trocar.'} error={error?.fields.referenceSalary?.[0]}>
          {(p) => <MoneyInput {...p} value={touched ? reference : str(suggested)} onChange={(v) => { setTouched(true); setReference(v) }} />}
        </Field>
        <p aria-live="polite" className="rounded-md border border-border bg-muted px-3 py-2 text-sm tabular-nums">
          Custo-hora previsto: <b>{formatMoney(preview)}</b> com {chargesPct.toLocaleString('pt-BR')}% de encargos e {position.monthlyHours.toLocaleString('pt-BR')} h por mês
        </p>
        {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar faixa salarial</button> : null}
      </fieldset>
    </FormSection>
  )
}
