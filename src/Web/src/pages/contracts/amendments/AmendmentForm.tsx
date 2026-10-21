import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { createAmendment, updateAmendment, useGetAmendmentContext } from '@/api/generated/bemaqui'
import type { AmendmentDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { ToggleGroup } from '@/components/form/choice'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { toNumber } from '../../finance/receivables/receivables'
import { AmendmentMilestones } from './AmendmentMilestones'
import { AMENDMENT_DICAS, emptyMilestone, type MilestoneDraft, milestoneInputs, proposeReductions } from './amendments'

const addDays = (iso: string, days: number) => { const d = new Date(iso + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + days); return d.toISOString().slice(0, 10) }

/** Aditivo em rascunho (COM-07 CA 1 a 4): acréscimo com marcos ou redução dos títulos futuros, com a prévia ao vivo. */
export function AmendmentForm({ contractId, amendment }: { contractId: string; amendment?: AmendmentDetail }) {
  const navigate = useNavigate()
  const context = useGetAmendmentContext(contractId)
  const idempotency = useIdempotencyKey()
  const [description, setDescription] = useState(amendment?.description ?? '')
  const [kind, setKind] = useState<'more' | 'less' | 'days'>(amendment ? (amendment.valueDelta > 0 ? 'more' : amendment.valueDelta < 0 ? 'less' : 'days') : 'more')
  const [amount, setAmount] = useState<string | null>(amendment && amendment.valueDelta !== 0 ? String(Math.abs(amendment.valueDelta)) : null)
  const [days, setDays] = useState(String(amendment?.daysDelta ?? 0))
  const [rows, setRows] = useState<MilestoneDraft[]>(() => amendment?.milestones.length
    ? amendment.milestones.map((m) => ({ description: m.description, percent: String(m.percent), trigger: m.trigger, triggerEvent: m.triggerEvent ?? 'Signature', dueDate: m.dueDate ?? '', days: String(m.daysAfterEvent ?? 0) }))
    : [emptyMilestone()])
  const [overrides, setOverrides] = useState<Record<string, string | null>>(() => Object.fromEntries((amendment?.reductions ?? []).map((r) => [r.receivableId, String(r.newAmount)])))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  if (context.isPending) return <Skeleton className="h-96" />
  if (context.isError) return <ErrorState title="Não foi possível carregar o contrato." onRetry={() => context.refetch()} />
  const c = context.data
  const value = kind === 'days' ? 0 : toNumber(amount) * (kind === 'less' ? -1 : 1)
  const daysDelta = Number(days || 0)
  const proposal = kind === 'less' && toNumber(amount) > 0 ? proposeReductions(c.reducible.map((r) => ({ id: r.id, number: r.number, amount: r.amount })), toNumber(amount)) : []
  const reductions = (proposal ?? []).map((p) => ({ ...p, newAmount: overrides[p.receivableId] !== undefined ? toNumber(overrides[p.receivableId]) : p.newAmount }))

  const save = async () => {
    setError(null)
    const body = {
      description: description.trim(), valueDelta: value, daysDelta, milestones: kind === 'more' ? milestoneInputs(rows) : null,
      reductions: kind === 'less' ? reductions : null, budgetDeltas: null, version: amendment?.version ?? null,
    }
    try {
      const saved = amendment ? await updateAmendment(contractId, amendment.id, body) : await createAmendment(contractId, body, idempotency.options)
      idempotency.renew()
      notifySuccess('Aditivo salvo')
      await navigate({ to: '/app/contratos/$id/aditivos/$aditivoId', params: { id: contractId, aditivoId: saved.id } } as never)
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o aditivo.', fields: {} })
    }
  }

  const field = (k: string) => error?.fields[k]?.[0]
  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-4xl space-y-4">
      <FormError message={error ? (Object.values(error.fields)[0]?.[0] ?? error.message) : undefined} />
      <FormSection title="O que muda">
        <Field label="Descrição" required error={field('description')}>{(p) => <textarea {...p} rows={2} maxLength={500} value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} />}</Field>
        <ToggleGroup label="Tipo" value={kind} onChange={setKind} options={[{ value: 'more', label: 'Acréscimo' }, { value: 'less', label: 'Redução' }, { value: 'days', label: 'Só prazo' }]} />
        <div className="grid gap-3 sm:grid-cols-2">
          {kind !== 'days' ? <Field label={kind === 'more' ? 'Valor do acréscimo' : 'Valor da redução'} required error={field('valueDelta')}>{(p) => <MoneyInput {...p} value={amount} onChange={setAmount} />}</Field> : null}
          <Field label="Variação de prazo (dias)" error={field('daysDelta')}>{(p) => <input {...p} inputMode="numeric" value={days} onChange={(e) => setDays(e.target.value.replace(/[^\d-]/g, ''))} className={inputClass} />}</Field>
        </div>
      </FormSection>
      {kind === 'more' ? <FormSection title="Marcos do acréscimo" description="Os marcos precisam somar 100% do acréscimo."><AmendmentMilestones value={value} rows={rows} onChange={setRows} /></FormSection> : null}
      {kind === 'less' ? (
        <FormSection title="Redução dos títulos futuros" description={AMENDMENT_DICAS.reducao}>
          {proposal === null ? <p className="text-sm text-crit-ink">A redução é maior que o saldo a receber sem baixa.</p> : (
            <ul className="space-y-2 text-sm" aria-label="Títulos reduzidos">
              {reductions.map((r) => { const parcel = c.reducible.find((x) => x.id === r.receivableId)!; return (
                <li key={r.receivableId} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-3">
                  <span><b>{parcel.number}</b> {parcel.description} · hoje <Money value={parcel.amount} /></span>
                  <MoneyInput aria-label={`Valor novo de ${parcel.number}`} value={overrides[r.receivableId] ?? String(r.newAmount)} onChange={(v) => setOverrides({ ...overrides, [r.receivableId]: v })} />
                </li>
              ) })}
            </ul>
          )}
        </FormSection>
      ) : null}
      <section aria-label="Antes e depois" className="grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-2">
        <p><span className="flex items-center text-xs text-muted-foreground">Contratado (valor vigente)<Dica nome="Valor vigente" texto={AMENDMENT_DICAS.vigente} /></span><Money value={c.currentValue} /> → <Money value={c.currentValue + value} className="font-semibold" /></p>
        <p><span className="block text-xs text-muted-foreground">Data prometida</span>{c.promisedDate ? `${formatDate(c.promisedDate)} → ${formatDate(addDays(c.promisedDate, daysDelta))}` : '—'}</p>
      </section>
      <div className="flex justify-end"><button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar aditivo</button></div>
    </form>
  )
}
