import { useState } from 'react'
import {
  addAssetMeterReading, createMaintenancePlan, listAssets, listAssetSupplierChoices, openMaintenanceOrder,
  updateMaintenancePlan, useGetMaintenanceOrderDefaults,
} from '@/api/generated/bemaqui'
import type { MaintenanceOrderDetail, MaintenanceOrderType, MaintenancePlanView } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import type { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput, QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { todayIso } from './assets'
import { COST_DICAS, formatHours } from './maintenance-cost'
import {
  draftFromPlan, emptyPlanDraft, formatMeter, MAINTENANCE_DICAS, ORDER_TYPE_OPTIONS, type PlanDraft, planErrors, planInput,
} from './maintenance'
import { useRefreshMaintenance } from './maintenance-hooks'

/** Erros do servidor por campo (422) no diálogo. */
function useFieldErrors(fields: string[]) {
  const [errors, setErrors] = useState<Partial<Record<string, string>>>({})
  const onFieldErrors = (e: ApiError) => {
    const known = Object.fromEntries(Object.entries(e.fields).filter(([k]) => fields.includes(k)).map(([k, v]) => [k, v[0]]))
    setErrors(known)
    return Object.keys(known).length > 0
  }
  return { errors, setErrors, onFieldErrors }
}

export function SupplierField({ value, onChange, error, label = 'Executor', hint }: { value: { id: string; label: string } | null; onChange: (v: { id: string; label: string } | null) => void; error?: string; label?: string; hint?: string }) {
  return (
    <Field label={label} error={error} hint={hint ?? 'Vazio: equipe interna. Prestador: um fornecedor do cadastro.'}>
      {(p) => (
        <Combobox {...p} queryKey={['maintenance-suppliers']} placeholder="Equipe interna (ou busque o prestador)" selected={value}
          search={async (q) => (await listAssetSupplierChoices({ q })).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined }))}
          onChange={(o) => onChange(o ? { id: o.id, label: o.label } : null)} />
      )}
    </Field>
  )
}

/**
 * "Novo plano" / "Editar plano" (CA 1): descrição, periodicidade (dias ou horas de uso), antecedência do aviso (padrão 7), executor padrão,
 * checklist e a última execução (data, ou horímetro no plano por horas). "Salvar plano" / "Plano salvo".
 */
export function PlanDialog({ assetId, plan, open, onOpenChange, onSaved }: {
  assetId: string; plan?: MaintenancePlanView; open: boolean; onOpenChange: (o: boolean) => void; onSaved?: (p: MaintenancePlanView) => void
}) {
  const refresh = useRefreshMaintenance(assetId)
  const fresh = () => (plan ? draftFromPlan(plan) : emptyPlanDraft(todayIso()))
  const [draft, setDraft] = useState<PlanDraft>(fresh)
  const [submitted, setSubmitted] = useState(false)
  const { errors: server, setErrors, onFieldErrors } = useFieldErrors(['description', 'frequencyType', 'frequencyValue', 'leadDays', 'defaultSupplierPartyId', 'checklist', 'lastDoneAt', 'lastDoneMeter'])
  const local = planErrors(draft)
  const errors = { ...(submitted ? local : {}), ...server }
  const set = (patch: Partial<PlanDraft>) => { setErrors({}); setDraft((d) => ({ ...d, ...patch })) }
  const days = draft.frequencyType === 'Days'
  return (
    <ConfirmDialog verb="Salvar plano" done="Plano salvo" title={plan ? `Editar plano ${plan.description}` : 'Novo plano de manutenção'} size="lg" open={open} onOpenChange={onOpenChange}
      description="O sistema avisa antes do vencimento e abre a OS preventiva sozinho no dia. O próximo vencimento conta da última execução."
      onOpen={() => { setDraft(fresh()); setSubmitted(false); setErrors({}) }}
      preconditions={[{ ok: Object.keys(local).length === 0, text: Object.values(local)[0] ?? '' }]}
      onFieldErrors={onFieldErrors}
      onConfirm={async () => {
        setSubmitted(true)
        const body = planInput(draft, plan?.version)
        const saved = plan ? await updateMaintenancePlan(assetId, plan.id, body as never) : await createMaintenancePlan(assetId, body as never)
        refresh()
        onSaved?.(saved)
      }}>
      <Field label="Descrição" required error={errors.description}>
        {(p) => <input {...p} value={draft.description} maxLength={150} onChange={(e) => set({ description: e.target.value })} className={inputClass} placeholder="Ex.: Lubrificação geral" />}
      </Field>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Periodicidade" required error={errors.frequencyType}>
          {(p) => (
            <select {...p} value={draft.frequencyType} onChange={(e) => set({ frequencyType: e.target.value as PlanDraft['frequencyType'] })} className={inputClass}>
              <option value="Days">A cada N dias</option>
              <option value="Hours">A cada N horas de uso</option>
            </select>
          )}
        </Field>
        <Field label={days ? 'A cada quantos dias' : 'A cada quantas horas'} required error={errors.frequencyValue}>
          {(p) => <input {...p} inputMode="numeric" value={draft.frequencyValue} onChange={(e) => set({ frequencyValue: e.target.value })} className={inputClass + ' tabular-nums'} />}
        </Field>
        <Field label="Antecedência do aviso (dias)" required error={errors.leadDays} dica={MAINTENANCE_DICAS.antecedencia}>
          {(p) => <input {...p} inputMode="numeric" value={draft.leadDays} onChange={(e) => set({ leadDays: e.target.value })} className={inputClass + ' tabular-nums'} />}
        </Field>
        {days ? (
          <Field label="Última execução" required error={errors.lastDoneAt} hint="Até hoje. A próxima conta desta data.">
            {(p) => <DateInput {...p} value={draft.lastDoneAt} onChange={(lastDoneAt) => set({ lastDoneAt })} />}
          </Field>
        ) : (
          <Field label="Horímetro da última execução" required error={errors.lastDoneMeter} dica={MAINTENANCE_DICAS.horimetro}>
            {(p) => <QuantityInput {...p} value={draft.lastDoneMeter || null} onChange={(v) => set({ lastDoneMeter: v ?? '' })} suffix="h" />}
          </Field>
        )}
      </div>
      <SupplierField value={draft.supplier} onChange={(supplier) => set({ supplier })} error={errors.defaultSupplierPartyId} label="Executor padrão" />
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">Checklist</legend>
        {errors.checklist ? <p role="alert" className="text-sm text-crit-ink">{errors.checklist}</p> : null}
        {draft.checklist.map((item, i) => (
          <div key={i} className="flex gap-2">
            <input aria-label={`Item ${i + 1} do checklist`} value={item} maxLength={200} onChange={(e) => set({ checklist: draft.checklist.map((c, j) => (j === i ? e.target.value : c)) })} className={inputClass} />
            <button type="button" onClick={() => set({ checklist: draft.checklist.filter((_, j) => j !== i) })} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Remover</button>
          </div>
        ))}
        <button type="button" onClick={() => set({ checklist: [...draft.checklist, ''] })} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Adicionar item</button>
      </fieldset>
      {plan ? (
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" checked={draft.isActive} onChange={(e) => set({ isActive: e.target.checked })} className="size-5" />
          Plano ativo (inativo não avisa nem abre OS)
        </label>
      ) : null}
    </ConfirmDialog>
  )
}

/** "Registrar horímetro" (CA 2): valor e data; a leitura precisa ser maior que a anterior. "Horímetro registrado". */
export function MeterDialog({ assetId, last, open, onOpenChange }: { assetId: string; last?: number | null; open: boolean; onOpenChange: (o: boolean) => void }) {
  const refresh = useRefreshMaintenance(assetId)
  const [value, setValue] = useState<string | null>(null)
  const [readOn, setReadOn] = useState(todayIso())
  const { errors, setErrors, onFieldErrors } = useFieldErrors(['value', 'readOn'])
  const number = value == null ? null : Number(value)
  return (
    <ConfirmDialog verb="Registrar horímetro" done="Horímetro registrado" title="Registrar horímetro" open={open} onOpenChange={onOpenChange}
      description={last != null ? `Última leitura: ${formatMeter(last)}. A nova precisa ser maior.` : 'Digite o valor que aparece no painel da máquina.'}
      onOpen={() => { setValue(null); setReadOn(todayIso()); setErrors({}) }}
      preconditions={[{ ok: number != null && (last == null || number > last), text: last != null ? `Informe uma leitura maior que ${formatMeter(last)}.` : 'Informe a leitura.' }]}
      onFieldErrors={onFieldErrors}
      onConfirm={async () => { await addAssetMeterReading(assetId, { value: number, readOn: readOn || null }); refresh() }}>
      <Field label="Horímetro" required error={errors.value} dica={MAINTENANCE_DICAS.horimetro}>
        {(p) => <QuantityInput {...p} value={value} onChange={(v) => { setErrors({}); setValue(v) }} suffix="h" />}
      </Field>
      <Field label="Data da leitura" required error={errors.readOn} hint="Até hoje.">{(p) => <DateInput {...p} value={readOn} onChange={setReadOn} />}</Field>
    </ConfirmDialog>
  )
}

interface OrderDraft {
  asset: { id: string; label: string } | null
  type: MaintenanceOrderType
  problem: string
  machineStopped: boolean
  scheduledOn: string
  supplier: { id: string; label: string } | null
  value: string | null
  hours: string | null
  expected: string | null
}

/**
 * "Nova OS de manutenção" (CA 4): o bem (fixo quando vem da ficha ou da leitura da etiqueta), corretiva ou preventiva avulsa (ou a preventiva do
 * plano, com o checklist dele), o problema, máquina parada, a data e o executor (o padrão do plano vem preenchido). Envia com Idempotency-Key.
 * "Abrir OS de manutenção" / "OS de manutenção aberta".
 */
export function OpenOrderDialog({ asset, plan, open, onOpenChange, onOpened, defaultType = 'Corrective' }: {
  asset?: { id: string; tag: string; name: string }
  plan?: MaintenancePlanView
  open: boolean
  onOpenChange: (o: boolean) => void
  onOpened: (order: MaintenanceOrderDetail) => void
  defaultType?: MaintenanceOrderType
}) {
  const refresh = useRefreshMaintenance(asset?.id)
  const { options, renew } = useIdempotencyKey()
  const fresh = (): OrderDraft => ({
    asset: asset ? { id: asset.id, label: `${asset.tag} · ${asset.name}` } : null, type: plan ? 'Preventive' : defaultType, problem: plan?.description ?? '', machineStopped: false,
    scheduledOn: todayIso(), supplier: plan?.defaultSupplierPartyId ? { id: plan.defaultSupplierPartyId, label: plan.defaultSupplierName ?? 'Prestador' } : null,
    value: null, hours: null, expected: null,
  })
  const [draft, setDraft] = useState<OrderDraft>(fresh)
  const canEdit = useCan('assets.edit')
  // PAT-03b (propositivo): o valor da última OS do plano e as horas paradas pelo centro vêm sugeridos.
  const defaults = useGetMaintenanceOrderDefaults({ assetId: draft.asset?.id ?? '', planId: plan?.id }, { query: { enabled: open && !!draft.asset } })
  const suggested = defaults.data
  const { errors, setErrors, onFieldErrors } = useFieldErrors(['assetId', 'type', 'problem', 'scheduledOn', 'supplierPartyId', 'planId', 'contractValue', 'hoursPerDay', 'expectedHours'])
  const set = (patch: Partial<OrderDraft>) => { setErrors({}); setDraft((d) => ({ ...d, ...patch })) }
  return (
    <ConfirmDialog verb="Abrir OS de manutenção" done="OS de manutenção aberta" title={plan ? `Abrir a preventiva ${plan.description}` : 'Nova OS de manutenção'} size="lg"
      open={open} onOpenChange={onOpenChange}
      description={plan ? `O checklist do plano (${plan.checklist.length} ${plan.checklist.length === 1 ? 'item' : 'itens'}) vai para a OS.` : 'A OS nasce aberta. Com a máquina parada, o bem fica "Em manutenção" na hora.'}
      onOpen={() => { setDraft(fresh()); setErrors({}) }}
      preconditions={[
        { ok: !!draft.asset, text: 'Escolha o bem.' },
        { ok: draft.problem.trim().length > 0, text: draft.type === 'Corrective' ? 'Descreva o problema.' : 'Descreva o que será feito.' },
      ]}
      onFieldErrors={onFieldErrors}
      onConfirm={async () => {
        const order = await openMaintenanceOrder({
          assetId: draft.asset!.id, type: draft.type, problem: draft.problem.trim(), machineStopped: draft.machineStopped, scheduledOn: draft.scheduledOn || null,
          supplierPartyId: draft.supplier?.id ?? null, planId: plan?.id ?? null,
          contractValue: canEdit && draft.supplier && draft.value ? Number(draft.value) : null,
          hoursPerDay: draft.hours ? Number(draft.hours) : null, expectedHours: draft.expected ? Number(draft.expected) : null,
        }, options)
        renew()
        refresh()
        onOpened(order)
      }}>
      {asset ? null : (
        <Field label="Bem" required error={errors.assetId}>
          {(p) => (
            <Combobox {...p} queryKey={['maintenance-assets']} placeholder="Buscar pela etiqueta ou nome" selected={draft.asset}
              search={async (q) => (await listAssets({ q, pageSize: 20 })).items.map((a) => ({ id: a.id, label: `${a.tag} · ${a.name}` }))}
              onChange={(o) => set({ asset: o ? { id: o.id, label: o.label } : null })} />
          )}
        </Field>
      )}
      {plan ? null : (
        <Field label="Tipo" required error={errors.type}>
          {(p) => (
            <select {...p} value={draft.type} onChange={(e) => set({ type: e.target.value as MaintenanceOrderType })} className={inputClass}>
              {ORDER_TYPE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.value === 'Preventive' ? 'Preventiva avulsa' : o.label}</option>)}
            </select>
          )}
        </Field>
      )}
      <Field label={draft.type === 'Corrective' ? 'Problema' : 'O que será feito'} required error={errors.problem} hint={`${draft.problem.length}/1000`}>
        {(p) => <textarea {...p} rows={3} value={draft.problem} maxLength={1000} onChange={(e) => set({ problem: e.target.value })} className={inputClass} />}
      </Field>
      <div className="flex items-center">
        <label className="flex min-h-11 items-center gap-2 text-sm font-medium">
          <input type="checkbox" checked={draft.machineStopped} onChange={(e) => set({ machineStopped: e.target.checked })} className="size-5" />
          Máquina parada
        </label>
        <Dica nome="Máquina parada" texto={MAINTENANCE_DICAS.maquinaParada} />
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Data agendada" required error={errors.scheduledOn}>{(p) => <DateInput {...p} value={draft.scheduledOn} onChange={(scheduledOn) => set({ scheduledOn })} />}</Field>
        <SupplierField value={draft.supplier} onChange={(supplier) => set({ supplier })} error={errors.supplierPartyId} />
        {canEdit && draft.supplier ? (
          <Field label="Valor combinado" error={errors.contractValue} dica={COST_DICAS.valorCombinado}
            hint={suggested?.contractValue != null ? `Sugestão: o valor da última OS do plano (${new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(suggested.contractValue)}).` : 'Vazio: sem título a pagar por enquanto.'}>
            {(p) => <MoneyInput {...p} value={draft.value} onChange={(value) => set({ value })} />}
          </Field>
        ) : null}
        {suggested?.centerHoursPerDay != null ? (
          <>
            <Field label="Horas paradas por dia" error={errors.hoursPerDay} dica={COST_DICAS.horasParadas}
              hint={`Sugestão: ${formatHours(suggested.hoursPerDay ?? suggested.centerHoursPerDay)} (${formatHours(suggested.centerHoursPerDay)} do centro ÷ ${Math.max(1, suggested.activeMachines)} ${suggested.activeMachines === 1 ? 'máquina' : 'máquinas'}).`}>
              {(p) => <QuantityInput {...p} value={draft.hours ?? (suggested.hoursPerDay != null ? String(suggested.hoursPerDay) : null)} onChange={(hours) => set({ hours })} suffix="h" />}
            </Field>
            <Field label="Duração prevista" error={errors.expectedHours} dica={COST_DICAS.duracao} hint="Horas paradas no total. Vazio: um dia de parada.">
              {(p) => <QuantityInput {...p} value={draft.expected} onChange={(expected) => set({ expected })} suffix="h" />}
            </Field>
          </>
        ) : null}
      </div>
    </ConfirmDialog>
  )
}

