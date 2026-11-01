import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { confirmHrParameter, getGetHrParametersQueryKey, searchPayrollPayees, updateHrParameters, useGetHrParameters } from '@/api/generated/bemaqui'
import type { HrParameterItem, HrParametersView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'

const PENDING = 'A confirmar com a contadora'
const PENDING_DICA = 'Valor sugerido pelo sistema. Confira com a contadora e clique em "Marcar como conferido". Se o valor mudar depois, volta para "A confirmar".'

const searchPayees = async (term: string): Promise<ComboOption[]> =>
  (await searchPayrollPayees({ q: term })).map((p) => ({ id: p.id, label: p.name, hint: p.document ?? undefined }))

type Value = { value: string; party: ComboOption | null; code: string }

const CODE_KINDS = new Set(['Account', 'CostCenter', 'ExportLayout', 'Geolocation'])

/** Opções do parâmetro escolhido por código: contas, centros de custo ou os layouts do pacote para a contadora (RH-03). */
const codeOptions = (view: HrParametersView, kind: string) => (kind === 'Account' ? view.accounts : kind === 'CostCenter' ? view.costCenters : kind === 'Geolocation' ? view.geolocationModes : view.exportLayouts) ?? []

/** 1130 → "30/11" e de volta (dia e mês do ano, RH-02c). */
const monthDayText = (v: number) => `${String(v % 100).padStart(2, '0')}/${String(Math.floor(v / 100)).padStart(2, '0')}`
const monthDayValue = (t: string) => { const [d, m] = t.split('/').map(Number); return d && m ? m * 100 + d : null }

const initialOf = (items: HrParameterItem[]) =>
  Object.fromEntries(items.map((i) => [i.key, { value: i.value == null ? '' : i.kind === 'MonthDay' ? monthDayText(i.value) : String(i.value), code: i.code ?? '', party: i.partyId ? { id: i.partyId, label: i.partyName ?? '' } : null } satisfies Value]))

const suffix = (i: HrParameterItem) => (i.kind === 'Percent' ? '%' : i.key === 'hr.timesheet_period_start_day' ? 'dia de início do período (1 = mês civil)' : i.kind === 'Minutes' ? 'minutos' : i.kind === 'DayOfMonth' ? 'dia do mês seguinte' : i.kind === 'Days' ? 'dias' : i.kind === 'BusinessDay' ? 'º dia útil do mês seguinte' : i.kind === 'DayOfPeriod' ? 'dia da competência' : i.kind === 'Months' ? 'meses' : i.kind === 'MonthDay' ? 'dia/mês' : i.kind === 'Hours' ? 'horas (0 = horas mensais do cargo)' : i.kind === 'Years' ? 'anos' : i.kind === 'Count' ? (i.key.startsWith('hr.vacation_absence_brackets') ? 'faltas' : 'períodos') : '')

/**
 * Configurações → Parâmetros → RH (RH-02a CA 3, ADR 0042, dono HR): alerta de variação do líquido, vencimento e órgão padrão de cada
 * guia e a quarentena da conta trocada. Os vencimentos legais têm o chip "A confirmar com a contadora" e "Marcar como conferido";
 * aparece com hr.view, salva com settings.edit, com o histórico de cada valor.
 */
export function HrParametersSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('hr.view')
  const query = useGetHrParameters({ query: { enabled: canSee } })
  if (!canSee) return null
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError || !query.data?.items) return null
  return <HrForm key={JSON.stringify(query.data.items.map((i) => [i.value, i.partyId, i.code, i.confirmed]))} view={query.data} canEdit={canEdit} />
}

function HrForm({ view, canEdit }: { view: HrParametersView; canEdit: boolean }) {
  const client = useQueryClient()
  const initial = initialOf(view.items)
  const [draft, setDraft] = useState<Record<string, Value>>(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const changed = view.items.filter((i) => draft[i.key].value !== initial[i.key].value || draft[i.key].party?.id !== initial[i.key].party?.id || draft[i.key].code !== initial[i.key].code)
  const saved = (next: HrParametersView, message: string) => {
    client.setQueryData(getGetHrParametersQueryKey(), next)
    void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/HrParameter/') })
    notifySuccess(message)
    setError(null)
  }
  const fail = (e: unknown, fallback: string) => setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: fallback, fields: {} })
  const save = async () => {
    try {
      saved(await updateHrParameters({ changes: changed.map((i) => (CODE_KINDS.has(i.kind) ? { key: i.key, value: null, partyId: null, code: draft[i.key].code } : i.kind === 'MonthDay' ? { key: i.key, value: monthDayValue(draft[i.key].value), partyId: null } : i.kind === 'Party' ? { key: i.key, value: null, partyId: draft[i.key].party?.id ?? null } : { key: i.key, value: draft[i.key].value === '' ? null : Number(draft[i.key].value.replace(',', '.')), partyId: null })) }), 'Parâmetros do RH salvos')
    } catch (e) {
      fail(e, 'Não foi possível salvar os parâmetros do RH.')
    }
  }
  const confirm = async (key: string) => {
    try {
      saved(await confirmHrParameter({ key }), 'Conferido')
    } catch (e) {
      fail(e, 'Não foi possível marcar como conferido.')
    }
  }

  return (
    <section id="secao-rh" aria-label="RH">
      <FormSection title="RH: folha e ponto" description={`Alerta da conferência, vencimento e órgão de cada guia, a quarentena da conta trocada, o pagamento dos salários, a folha prevista, a conta e o centro de custo dos títulos da folha, o layout do pacote para a contadora, no ponto a tolerância, o banco de horas e o início do período de apuração e, na folha calculada, a proporcionalidade, o afastamento, os adicionais, o valor da hora, o salário-família e a redução do IRRF. Contribuição sindical e "Outros" têm o vencimento informado na folha; o dia não útil antecipa pelo calendário padrão. ${view.pendingCount > 0 ? `${view.pendingCount} ${view.pendingCount === 1 ? 'valor a confirmar' : 'valores a confirmar'} com a contadora.` : 'Tudo conferido com a contadora.'}`}>
        <div className="space-y-3 sm:col-span-2">
          <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
          {view.items.map((item) => {
            const id = `hr-${item.key}`
            const fieldError = error?.fields[item.key]?.[0]
            return (
              <div key={item.key} role="group" aria-label={item.label} className="space-y-2 rounded-md border border-border p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <label htmlFor={id} className="inline-flex items-center text-sm font-semibold">{item.label}<Dica nome={item.label} texto={item.description} /></label>
                  {item.legal ? (item.confirmed ? (
                    <StatusChip tone="ok">Conferido{item.confirmedByName ? ` por ${item.confirmedByName}` : ''}{item.confirmedAt ? ` em ${formatDateTime(item.confirmedAt)}` : ''}</StatusChip>
                  ) : (
                    <span className="inline-flex flex-wrap items-center gap-2">
                      <StatusChip tone="warn">{PENDING}</StatusChip>
                      <Dica nome={PENDING} texto={PENDING_DICA} />
                      {canEdit ? <button type="button" onClick={() => void confirm(item.key)} className="min-h-11 rounded-md border border-border px-2 py-1 text-xs font-semibold sm:min-h-0">Marcar como conferido</button> : null}
                    </span>
                  )) : null}
                </div>
                {item.kind === 'Party' ? (
                  <Combobox id={id} queryKey={['payroll-payees']} search={searchPayees} value={draft[item.key].party?.id ?? null} selected={draft[item.key].party} disabled={!canEdit}
                    placeholder="Sem órgão padrão: busque pelo nome" aria-invalid={fieldError ? true : undefined} onChange={(party) => setDraft((d) => ({ ...d, [item.key]: { ...d[item.key], party } }))} />
                ) : CODE_KINDS.has(item.kind) ? (
                  <select id={id} disabled={!canEdit} value={draft[item.key].code} onChange={(e) => setDraft((d) => ({ ...d, [item.key]: { ...d[item.key], code: e.target.value } }))}
                    aria-invalid={fieldError ? true : undefined} className={inputClass + ' max-w-md'}>
                    {codeOptions(view, item.kind).map((o) => <option key={o.code} value={o.code}>{item.kind === 'ExportLayout' || item.kind === 'Geolocation' ? o.name : `${o.code} ${o.name}`}</option>)}
                    {draft[item.key].code && !codeOptions(view, item.kind).some((o) => o.code === draft[item.key].code) ? <option value={draft[item.key].code}>{draft[item.key].code} (não encontrado)</option> : null}
                  </select>
                ) : item.kind === 'Choice' ? (
                  <select id={id} disabled={!canEdit} value={draft[item.key].value} onChange={(e) => setDraft((d) => ({ ...d, [item.key]: { ...d[item.key], value: e.target.value } }))}
                    aria-invalid={fieldError ? true : undefined} className={inputClass + ' max-w-md'}>
                    {(item.options ?? []).map((o) => <option key={o.value} value={String(o.value)}>{o.label}</option>)}
                  </select>
                ) : item.kind === 'Toggle' ? (
                  <select id={id} disabled={!canEdit} value={draft[item.key].value} onChange={(e) => setDraft((d) => ({ ...d, [item.key]: { ...d[item.key], value: e.target.value } }))}
                    aria-invalid={fieldError ? true : undefined} className={inputClass + ' max-w-xs'}>
                    <option value="1">Ligado</option>
                    <option value="0">Desligado</option>
                  </select>
                ) : item.kind === 'MonthDay' ? (
                  <span className="flex items-center gap-2">
                    <input id={id} inputMode="numeric" placeholder="dd/mm" maxLength={5} disabled={!canEdit} value={draft[item.key].value} onChange={(e) => setDraft((d) => ({ ...d, [item.key]: { ...d[item.key], value: e.target.value.replace(/[^0-9/]/g, '') } }))}
                      aria-invalid={fieldError ? true : undefined} aria-describedby={fieldError ? `${id}-error` : undefined} className={inputClass + ' max-w-28 tabular-nums'} />
                    <span className="text-sm text-muted-foreground">{suffix(item)}</span>
                  </span>
                ) : (
                  <span className="flex items-center gap-2">
                    <input id={id} inputMode="decimal" disabled={!canEdit} value={draft[item.key].value} onChange={(e) => setDraft((d) => ({ ...d, [item.key]: { ...d[item.key], value: e.target.value.replace(/[^0-9,.]/g, '') } }))}
                      aria-invalid={fieldError ? true : undefined} aria-describedby={fieldError ? `${id}-error` : undefined} className={inputClass + ' max-w-28 tabular-nums'} />
                    <span className="text-sm text-muted-foreground">{suffix(item)}</span>
                  </span>
                )}
                {fieldError ? <p id={`${id}-error`} role="alert" className="text-xs font-medium text-crit-ink">{fieldError}</p> : null}
                {item.parameterId ? (
                  <details className="text-sm">
                    <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Histórico de alterações</summary>
                    <div className="mt-2"><RecordHistory entity="HrParameter" id={item.parameterId} /></div>
                  </details>
                ) : null}
              </div>
            )
          })}
          {canEdit ? (
            <button type="button" disabled={changed.length === 0} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar parâmetros do RH</button>
          ) : null}
        </div>
      </FormSection>
    </section>
  )
}
