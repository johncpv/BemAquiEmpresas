import { useQueryClient } from '@tanstack/react-query'
import { ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import {
  addMaintenanceOrderCost, getListMaintenanceOrderCostsQueryKey, listAssetResponsibleChoices, removeMaintenanceOrderCost, scheduleMaintenanceOrder,
  useGetMaintenanceOrderDefaults, useListMaintenanceOrderCosts,
} from '@/api/generated/bemaqui'
import type { MaintenanceCostType, MaintenanceOrderDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput, QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDateTime } from '@/lib/format'
import { COST_DICAS, COST_TYPE_LABELS, formatHours, MANUAL_COST_TYPES } from './maintenance-cost'
import { useRefreshMaintenance } from './maintenance-hooks'
import { SupplierField } from './MaintenanceDialogs'

const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'

/** "Dados protegidos" (CA 7): sem assets.edit, nenhum valor de custo. */
export function ProtectedCost() {
  return (
    <div role="status" className="max-w-xl space-y-1 rounded-md border border-border bg-card p-4 text-sm">
      <p className="flex items-center gap-2 font-semibold"><ShieldCheck aria-hidden="true" className="size-4 text-muted-foreground" />Dados protegidos</p>
      <p className="text-muted-foreground">O custo da manutenção é visto por quem edita o patrimônio.</p>
    </div>
  )
}

/**
 * Aba Custo da OS (PAT-03b CA 6): as linhas por natureza (prestador do título, mão de obra interna, peças e outros), o total por natureza e
 * o total; "Lançar custo" na OS concluída. Mão de obra: o custo-hora e o nome só com hr.view_sensitive.
 */
export function CostTab({ order: o, canEdit }: { order: MaintenanceOrderDetail; canEdit: boolean }) {
  const client = useQueryClient()
  const refresh = useRefreshMaintenance(o.assetId)
  const query = useListMaintenanceOrderCosts(o.id, { query: { enabled: canEdit } })
  const [type, setType] = useState<MaintenanceCostType>('Parts')
  const [description, setDescription] = useState('')
  const [amount, setAmount] = useState<string | null>(null)
  const [hours, setHours] = useState<string | null>(null)
  const [employee, setEmployee] = useState<{ id: string; label: string } | null>(null)
  const [error, setError] = useState<string>()
  const [saving, setSaving] = useState(false)
  if (!canEdit) return <ProtectedCost />
  if (query.isPending) return <Skeleton className="h-48 max-w-3xl" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar o custo da OS." onRetry={() => query.refetch()} />
  const view = query.data
  const done = o.status === 'Done'
  const labor = type === 'Labor'
  const ready = labor ? !!employee && Number(hours) > 0 : description.trim().length > 0 && Number(amount) > 0
  const apply = (next: typeof view, message: string) => {
    client.setQueryData(getListMaintenanceOrderCostsQueryKey(o.id), next)
    refresh()
    notifySuccess(message)
  }
  const add = async () => {
    setSaving(true)
    setError(undefined)
    try {
      apply(await addMaintenanceOrderCost(o.id, labor
        ? { costType: type, employeeId: employee!.id, hours: Number(hours), description: description.trim() || null, version: view.version }
        : { costType: type, description: description.trim(), amount: Number(amount), version: view.version }), 'Custo lançado')
      setDescription(''); setAmount(null); setHours(null); setEmployee(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível lançar o custo.')
    } finally {
      setSaving(false)
    }
  }
  const remove = async (costId: string) => {
    setError(undefined)
    try {
      apply(await removeMaintenanceOrderCost(o.id, costId, { version: view.version }), 'Custo tirado')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível tirar o custo.')
    }
  }
  return (
    <div className="max-w-3xl space-y-4">
      <dl className="grid gap-2 sm:grid-cols-5">
        {view.totals.map((t) => (
          <div key={t.costType} className="rounded-md border border-border bg-card p-3">
            <dt className="text-xs text-muted-foreground">{COST_TYPE_LABELS[t.costType]}</dt>
            <dd className="font-semibold"><Money value={t.amount} /></dd>
          </div>
        ))}
        <div className="rounded-md border border-border bg-card p-3">
          <dt className="text-xs text-muted-foreground">Total da OS</dt>
          <dd className="font-display text-lg font-bold"><Money value={view.total} /></dd>
        </div>
      </dl>
      <FormError message={error} />
      {view.lines.length === 0 ? (
        <EmptyState title="Nenhum custo de manutenção lançado." description={done ? 'Lance a mão de obra interna, as peças e os outros custos abaixo. O prestador entra sozinho na conclusão.' : 'Conclua a OS para lançar o custo. O prestador entra sozinho na conclusão.'} />
      ) : (
        <ul aria-label="Custos da OS" className="divide-y divide-border rounded-md border border-border bg-card text-sm">
          {view.lines.map((l) => (
            <li key={l.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <span>
                <b>{COST_TYPE_LABELS[l.costType]}</b> · {l.description}
                <span className="block text-xs text-muted-foreground tabular-nums">
                  {l.hours != null ? `${formatHours(l.hours)}` : ''}{l.hourlyCost != null ? ` × ${new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(l.hourlyCost)}/h` : ''}{l.employeeName ? ` · ${l.employeeName}` : ''}{l.hours != null ? ' · ' : ''}{formatDateTime(l.createdAt)}
                </span>
              </span>
              <span className="flex items-center gap-2">
                {l.amount != null ? <Money value={l.amount} /> : <span className="text-xs text-muted-foreground">Dados protegidos</span>}
                {l.removable ? <button type="button" onClick={() => void remove(l.id)} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold text-crit-ink">Tirar</button> : null}
              </span>
            </li>
          ))}
        </ul>
      )}
      {!view.laborDetail && view.lines.some((l) => l.costType === 'Labor') ? <p className="text-xs text-muted-foreground">O custo-hora e o nome de quem executou só aparecem para quem vê dados sensíveis do RH.</p> : null}
      {done ? (
        <section aria-label="Lançar custo" className="space-y-3 rounded-md border border-border bg-card p-3">
          <h3 className="text-sm font-semibold">Lançar custo</h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Natureza">
              {(p) => (
                <select {...p} value={type} onChange={(e) => setType(e.target.value as MaintenanceCostType)} className={inputClass}>
                  {MANUAL_COST_TYPES.map((t) => <option key={t} value={t}>{COST_TYPE_LABELS[t]}</option>)}
                </select>
              )}
            </Field>
            {labor ? (
              <Field label="Quem executou" required>
                {(p) => (
                  <Combobox {...p} queryKey={['maintenance-employees']} placeholder="Buscar o funcionário" selected={employee}
                    search={async (q) => (await listAssetResponsibleChoices())
                      .filter((e) => e.name.toLocaleLowerCase('pt-BR').includes(q.trim().toLocaleLowerCase('pt-BR')))
                      .map((e) => ({ id: e.id, label: e.name }))}
                    onChange={(v) => setEmployee(v ? { id: v.id, label: v.label } : null)} />
                )}
              </Field>
            ) : null}
            <Field label={labor ? 'Descrição (opcional)' : 'Descrição'} required={!labor} hint={`${description.length}/150`}>
              {(p) => <input {...p} value={description} maxLength={150} onChange={(e) => setDescription(e.target.value)} className={inputClass} />}
            </Field>
            {labor
              ? <Field label="Horas trabalhadas" required hint="O custo-hora vem do cargo no RH.">{(p) => <QuantityInput {...p} value={hours} onChange={setHours} suffix="h" />}</Field>
              : <Field label="Valor" required>{(p) => <MoneyInput {...p} value={amount} onChange={setAmount} />}</Field>}
          </div>
          <button type="button" disabled={!ready || saving} onClick={() => void add()} className={primary}>{saving ? 'Lançando…' : 'Lançar custo'}</button>
        </section>
      ) : null}
    </div>
  )
}

/**
 * "Planejar" a OS aberta ou em andamento (PAT-03b CA 1, 2 e 8): data, prestador, "Valor combinado" (vira o título previsto "Estimado"),
 * "Horas paradas por dia" (sugeridas pelo centro) e a duração prevista. "Salvar planejamento" / "Planejamento salvo".
 */
export function ScheduleDialog({ order: o, open, onOpenChange, onSaved }: { order: MaintenanceOrderDetail; open: boolean; onOpenChange: (v: boolean) => void; onSaved: (o: MaintenanceOrderDetail) => void }) {
  const defaults = useGetMaintenanceOrderDefaults({ assetId: o.assetId, planId: o.planId ?? undefined }, { query: { enabled: open } })
  const fresh = () => ({
    scheduledOn: o.scheduledOn, supplier: o.supplierPartyId ? { id: o.supplierPartyId, label: o.supplierName ?? 'Prestador' } : null,
    value: o.contractValue != null ? String(o.contractValue) : null as string | null, hours: o.hoursPerDay != null ? String(o.hoursPerDay) : null as string | null,
    expected: o.expectedHours != null ? String(o.expectedHours) : null as string | null,
  })
  const [draft, setDraft] = useState(fresh)
  const [errors, setErrors] = useState<Partial<Record<string, string>>>({})
  const set = (patch: Partial<ReturnType<typeof fresh>>) => { setErrors({}); setDraft((d) => ({ ...d, ...patch })) }
  const center = o.centerHoursPerDay ?? defaults.data?.centerHoursPerDay
  return (
    <ConfirmDialog verb="Salvar planejamento" done="Planejamento salvo" title={`Planejar ${o.number}`} size="lg" open={open} onOpenChange={onOpenChange}
      description="O valor combinado vira um título a pagar previsto, confirmado na conclusão. As horas paradas saem da capacidade do centro."
      onOpen={() => { setDraft(fresh()); setErrors({}) }}
      preconditions={[{ ok: !draft.value || !!draft.supplier, text: 'Escolha o prestador para lançar o valor combinado.' }]}
      onFieldErrors={(e) => { const known = Object.fromEntries(Object.entries(e.fields).filter(([k]) => ['scheduledOn', 'supplierPartyId', 'contractValue', 'hoursPerDay', 'expectedHours'].includes(k)).map(([k, v]) => [k, v[0]])); setErrors(known); return Object.keys(known).length > 0 }}
      onConfirm={async () => {
        onSaved(await scheduleMaintenanceOrder(o.id, {
          scheduledOn: draft.scheduledOn || null, supplierPartyId: draft.supplier?.id ?? null, contractValue: draft.value ? Number(draft.value) : null,
          hoursPerDay: draft.hours ? Number(draft.hours) : null, expectedHours: draft.expected ? Number(draft.expected) : null, version: o.version,
        }))
      }}>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Data agendada" required error={errors.scheduledOn}>{(p) => <DateInput {...p} value={draft.scheduledOn} onChange={(scheduledOn) => set({ scheduledOn })} />}</Field>
        <SupplierField value={draft.supplier} onChange={(supplier) => set({ supplier })} error={errors.supplierPartyId} />
        <Field label="Valor combinado" error={errors.contractValue} dica={COST_DICAS.valorCombinado}
          hint={draft.supplier ? (defaults.data?.contractValue != null && !draft.value ? `Sugestão: o valor da última OS do plano (${new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(defaults.data.contractValue)}).` : 'Vazio: sem título a pagar.') : 'Escolha o prestador primeiro.'}>
          {(p) => <MoneyInput {...p} disabled={!draft.supplier} value={draft.value} onChange={(value) => set({ value })} />}
        </Field>
        {center != null ? (
          <>
            <Field label="Horas paradas por dia" error={errors.hoursPerDay} dica={COST_DICAS.horasParadas} hint={`Até ${formatHours(center)} (as horas do centro).`}>
              {(p) => <QuantityInput {...p} value={draft.hours ?? (defaults.data?.hoursPerDay != null ? String(defaults.data.hoursPerDay) : null)} onChange={(hours) => set({ hours })} suffix="h" />}
            </Field>
            <Field label="Duração prevista" error={errors.expectedHours} dica={COST_DICAS.duracao} hint="Horas paradas no total.">
              {(p) => <QuantityInput {...p} value={draft.expected} onChange={(expected) => set({ expected })} suffix="h" />}
            </Field>
          </>
        ) : <p className="text-sm text-muted-foreground sm:col-span-2">O bem não tem centro de trabalho: a manutenção não muda a capacidade da fábrica.</p>}
      </div>
    </ConfirmDialog>
  )
}
