import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { createPayrollRun, getListPayrollRunsQueryKey, recalculatePayrollForecast, useGetPayrollSuggestedPeriod, useListPayrollEligibleEmployees, useListPayrollRuns, useGetThirteenthPreviewSensitive, useListPayrollRunTotalsSensitive } from '@/api/generated/bemaqui'
import type { PayrollRunMode, PayrollRunSummary, PayrollRunType } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { choosesEmployees, currentPeriod, isThirteenth, PAYROLL_DICAS, periodLabel, RUN_TYPES, STATUS_LABELS, STATUS_TONES, thirteenthPeriod, typeLabel } from './payroll'
import { ThirteenthPreviewPanel } from './ThirteenthPreview'

const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'

/**
 * RH → Folha (RH-02a CA 4 e 12): lista das folhas com competência, tipo, situação e holerites; com hr.view_sensitive, o líquido
 * total (rota /sensitive). "Nova folha" com hr.create + hr.view_sensitive.
 */
export function PayrollRunsPage() {
  const query = useListPayrollRuns()
  const canSensitive = useCan('hr.view_sensitive')
  const canCreate = useCan('hr.create') && canSensitive
  const canForecast = useCan('hr.edit')
  const totals = useListPayrollRunTotalsSensitive({ query: { enabled: canSensitive } })
  // FLH-04 (Propositivo): a Minha pauta abre "Nova folha → 13º" com a parcela e o ano (?novo13=1 ou 2).
  const [initial] = useState<PayrollRunType | null>(() => { try { const v = new URLSearchParams(window.location.search).get('novo13'); return v === '1' ? 'Thirteenth1' : v === '2' ? 'Thirteenth2' : null } catch { return null } })
  const [creating, setCreating] = useState(initial !== null)
  const net = new Map((totals.data ?? []).map((t) => [t.runId, t.net]))
  const newButton = canCreate ? <button type="button" onClick={() => setCreating(true)} className={primary}>Nova folha</button> : null

  return (
    <>
      <PageHeading title="RH: Folha">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="inline-flex items-center text-muted-foreground">Folha lançada: os valores da contadora, conferidos antes de virar holerite e pagamento.<Dica nome="Folha lançada" texto={PAYROLL_DICAS.lancada} /></p>
          <span className="flex flex-wrap gap-2">
            {canForecast ? (
              <ConfirmDialog verb="Recalcular previsão" done="Previsão recalculada" title="Recalcular a folha prevista?"
                description="Refaz os próximos meses da folha no fluxo de caixa a partir da última folha Mensal fechada, com o 13º e as férias."
                onConfirm={async () => { const r = await recalculatePayrollForecast(); notifySuccess(r.lines === 1 ? '1 linha prevista no fluxo de caixa' : `${r.lines} linhas previstas no fluxo de caixa`) }}
                trigger={<button type="button" className="inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm font-semibold">Recalcular previsão</button>} />
            ) : null}
            {canForecast ? <Dica nome="Folha prevista" texto={PAYROLL_DICAS.prevista} /> : null}
            {newButton}
          </span>
        </div>
      </PageHeading>
      <HrTabs />
      {query.isPending ? <Skeleton className="h-48" /> : query.isError ? <ErrorState title="Não foi possível carregar as folhas." onRetry={() => query.refetch()} /> : query.data.length === 0 ? (
        <EmptyState title="Nenhuma folha ainda." description="Crie a folha do mês para lançar os valores da contadora." action={newButton} />
      ) : (
        <ul aria-label="Folhas" className="divide-y divide-border rounded-md border border-border">
          {query.data.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
              <Link to="/app/rh/folha/$id" params={{ id: r.id }} className="font-semibold text-primary hover:underline">{typeLabel(r.type)} · {periodLabel(r.period)}{r.mode === 'Calculated' ? (r.rectifiesRunId ? ' · retificação' : ' · calculada') : ''}</Link>
              <span className="flex flex-wrap items-center gap-3">
                <span className="tabular-nums text-muted-foreground">{r.payslipCount === 1 ? '1 holerite' : `${r.payslipCount} holerites`}</span>
                {canSensitive && net.has(r.id) ? <span className="text-muted-foreground">Líquido <Money value={net.get(r.id) ?? 0} className="font-semibold text-foreground" /></span> : null}
                {r.closedByName ? <span className="text-muted-foreground">Fechada por {r.closedByName}</span> : null}
                {r.isSimulation ? <StatusChip tone="info">Simulação</StatusChip> : null}
                <StatusChip tone={STATUS_TONES[r.status] ?? 'neutral'}>{STATUS_LABELS[r.status] ?? r.status}</StatusChip>
              </span>
            </li>
          ))}
        </ul>
      )}
      {creating && canCreate ? <NewRunDialog initialType={initial} onClose={() => setCreating(false)} /> : null}
    </>
  )
}

function NewRunDialog({ onClose, initialType }: { onClose: () => void; initialType?: PayrollRunType | null }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const [type, setType] = useState<PayrollRunType>(initialType ?? 'Monthly')
  const [period, setPeriod] = useState(() => (initialType ? thirteenthPeriod(initialType, Number(currentPeriod().slice(0, 4))) : currentPeriod()))
  const [mode, setMode] = useState<PayrollRunMode>(initialType ? 'Calculated' : 'Entered')
  const [withoutAdvance, setWithoutAdvance] = useState(false)
  // FLH-04: o 13º também é calculado; a prévia dos avos sai antes de criar.
  const calculable = type === 'Monthly' || isThirteenth(type)
  const thirteenth = isThirteenth(type) && mode === 'Calculated'
  const year = Number(period.slice(0, 4))
  const preview = useGetThirteenthPreviewSensitive({ year, type }, { query: { enabled: thirteenth } })
  const needsConfirm = thirteenth && type === 'Thirteenth2' && preview.data !== undefined && !preview.data.firstClosed
  // Propositivo (FLH-02a): na calculada, a competência sugerida é a do último ponto fechado.
  const suggested = useGetPayrollSuggestedPeriod({ query: { enabled: type === 'Monthly' } }).data?.period ?? null
  const [chosen, setChosen] = useState<string[]>([])
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const choose = choosesEmployees(type)
  const eligible = useListPayrollEligibleEmployees({ period }, { query: { enabled: choose && /^\d{4}-\d{2}$/.test(period) } })
  const create = async () => {
    setError(null)
    const run: PayrollRunSummary = await createPayrollRun({ period, type, employeeIds: choose ? chosen : null, mode: calculable ? mode : 'Entered', confirmWithoutAdvance: needsConfirm ? withoutAdvance : null }, idempotency.options)
    idempotency.renew()
    void client.invalidateQueries({ queryKey: getListPayrollRunsQueryKey() })
    await navigate({ to: '/app/rh/folha/$id', params: { id: run.id }, search: { novo: '1' } as never })
  }

  return (
    <ConfirmDialog open onOpenChange={(o) => !o && onClose()} verb="Criar folha" done="Folha criada" title="Nova folha" size="lg"
      description={thirteenth ? 'A folha de 13º calculada nasce com um holerite por funcionário com avos no ano; depois, "Calcular" faz a parcela pelo cadastro, pelos espelhos fechados e pelas tabelas. Até a virada e o primeiro 13º conferido, é só simulação.' : mode === 'Calculated' && type === 'Monthly' ? 'A folha calculada nasce com um holerite por funcionário ativo na competência; depois, "Calcular" faz as linhas pelo cadastro, pelo ponto fechado e pelas tabelas. Até a virada, é só simulação.' : 'A folha nasce em lançamento, com um holerite por funcionário ativo na competência. Na Mensal, o salário já vem preenchido.'}
      preconditions={choose ? [{ ok: chosen.length > 0, text: 'Escolha os funcionários desta folha.' }] : needsConfirm ? [{ ok: withoutAdvance, text: 'Confirme o cálculo da 2ª parcela sem adiantamento.' }] : []}
      onFieldErrors={(e: ApiError) => { setError({ message: e.message, fields: e.fields }); return true }}
      onConfirm={create}>
      <div className="space-y-3">
        <FormError message={error && !error.fields.period && !error.fields.type && !error.fields.employeeIds ? error.message : undefined} />
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Competência" required error={error?.fields.period?.[0]}>
            {(p) => <input {...p} type="month" value={period} onChange={(e) => { setPeriod(e.target.value); setChosen([]) }} className={inputClass} />}
          </Field>
          <Field label="Tipo" required error={error?.fields.type?.[0]}>
            {(p) => (
              <select {...p} value={type} onChange={(e) => { const next = e.target.value as PayrollRunType; setType(next); setChosen([]); setWithoutAdvance(false); if (isThirteenth(next)) setPeriod(thirteenthPeriod(next, year)) }} className={inputClass}>
                {RUN_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
              </select>
            )}
          </Field>
        </div>
        {calculable ? (
          <Field label="Modo" required error={error?.fields.mode?.[0]} dica={PAYROLL_DICAS.simulacao}
            hint={mode === 'Calculated' ? `Simulação: roda em paralelo com a folha lançada da contadora, que continua a oficial.${suggested ? ` Competência sugerida: o último ponto fechado (${periodLabel(suggested)}).` : ''}` : 'Os valores vêm da contadora (digitados ou importados).'}>
            {(p) => (
              <select {...p} value={mode} onChange={(e) => { const next = e.target.value as PayrollRunMode; setMode(next); if (next === 'Calculated' && suggested && type === 'Monthly') setPeriod(suggested) }} className={inputClass}>
                <option value="Entered">Lançada (valores da contadora)</option>
                <option value="Calculated">Calculada pelo sistema</option>
              </select>
            )}
          </Field>
        ) : null}
        {thirteenth ? <ThirteenthPreviewPanel query={preview} confirm={withoutAdvance} onConfirm={setWithoutAdvance} /> : null}
        {choose ? (
          <fieldset className="space-y-1">
            <legend className="text-sm font-medium">Funcionários <span className="text-xs font-normal text-muted-foreground">(obrigatório)</span></legend>
            {eligible.isPending ? <Skeleton className="h-16" /> : eligible.isError ? <p className="text-sm text-crit-ink">Não foi possível carregar os funcionários.</p> : (eligible.data ?? []).length === 0 ? (
              <p className="text-sm text-muted-foreground">Nenhum funcionário ativo em {periodLabel(period)}.</p>
            ) : (
              <ul className="max-h-56 space-y-1 overflow-y-auto rounded-md border border-border p-2">
                {(eligible.data ?? []).map((e) => (
                  <li key={e.id}>
                    <label className="flex min-h-11 items-center gap-2 text-sm sm:min-h-0">
                      <input type="checkbox" checked={chosen.includes(e.id)} onChange={(ev) => setChosen((c) => (ev.target.checked ? [...c, e.id] : c.filter((x) => x !== e.id)))} className="size-4 accent-primary" />
                      {e.name}{e.registration ? <span className="text-muted-foreground"> · {e.registration}</span> : null}
                    </label>
                  </li>
                ))}
              </ul>
            )}
            {error?.fields.employeeIds?.[0] ? <p role="alert" className="text-xs font-medium text-crit-ink">{error.fields.employeeIds[0]}</p> : null}
          </fieldset>
        ) : null}
      </div>
    </ConfirmDialog>
  )
}
