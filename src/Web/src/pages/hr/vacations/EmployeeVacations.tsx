import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import {
  calculateVacation, cancelVacationSchedule, getDownloadPayslipPdfUrl, getDownloadVacationNoticeUrl, getGetEmployeeVacationsQueryKey, scheduleVacation, useGetEmployeeVacations,
} from '@/api/generated/bemaqui'
import type { EmployeeDetail, EmployeeVacations as Vacations, VacationPeriodItem, VacationScheduleItem } from '@/api/generated/model'
import { apiDownload, ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { type Step, NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { addDays, periodTone, SCHEDULE_TONES, scheduleDraftError, VACATION_DICAS, vacationReturn } from './vacations'

const button = 'min-h-11 rounded-md border border-border px-3 text-sm font-semibold'

const download = (url: string, name: string) => apiDownload(url, name).catch((e: unknown) => notifyError(e instanceof Error ? e.message : 'Não foi possível baixar o arquivo.'))

const downloadNotice = (s: { id: string; startsOn: string }) => download(getDownloadVacationNoticeUrl(s.id), `aviso-ferias-${s.startsOn}.pdf`)

/** Período aquisitivo em cartão (CA 1): datas, dias de direito, gozados, vendidos, saldo, o concessivo e o chip. */
function PeriodCard({ p }: { p: VacationPeriodItem }) {
  return (
    <li className="rounded-md border border-border bg-card p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-semibold tabular-nums">{formatDate(p.startsOn)} a {formatDate(p.endsOn)}</p>
        <StatusChip tone={periodTone(p.tone)}>{p.statusLabel}</StatusChip>
      </div>
      <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-sm sm:grid-cols-4">
        <div><dt className="text-muted-foreground">Dias de direito</dt><dd className="tabular-nums">{p.entitledDays}</dd></div>
        <div><dt className="text-muted-foreground">Gozados</dt><dd className="tabular-nums">{p.takenDays}</dd></div>
        <div><dt className="text-muted-foreground">Vendidos</dt><dd className="tabular-nums">{p.soldDays}</dd></div>
        <div><dt className="text-muted-foreground">Saldo</dt><dd className="font-semibold tabular-nums">{p.balance}</dd></div>
      </dl>
      <p className="mt-2 inline-flex items-center text-sm text-muted-foreground">
        Fim do período concessivo: <span className="ml-1 tabular-nums">{formatDate(p.concessionEndsOn)}</span>
        <Dica nome="Período concessivo" texto={VACATION_DICAS.concessivo} />
      </p>
      {p.memory.map((m) => <p key={m} className="mt-1 text-xs text-muted-foreground">{m}</p>)}
    </li>
  )
}

/**
 * "Programar férias" (CA 4 e 5): o período, o início (o sugerido depois do aviso de 30 dias), os dias (o saldo), o abono e o adiantamento do
 * 13º. A validação com os parâmetros é do servidor (422 com a mensagem do parâmetro); o diálogo mostra o fim, o retorno e o pagamento.
 */
function ScheduleDialog({ data, open, onOpenChange, onDone }: { data: Vacations; open: boolean; onOpenChange: (o: boolean) => void; onDone: (steps: Step[], warnings: string[]) => void }) {
  const client = useQueryClient()
  const idempotency = useIdempotencyKey()
  const available = data.periods.filter((p) => (p.status === 'Available' || p.status === 'Expired') && p.balance > 0)
  const s = data.suggestion
  const initial = () => ({ periodId: s?.periodId ?? available[0]?.id ?? '', startsOn: s?.startsOn ?? '', days: String(s?.days ?? ''), soldDays: '0', advance13th: false })
  const [draft, setDraft] = useState(initial)
  const period = available.find((p) => p.id === draft.periodId)
  const maxSold = period ? Math.max(Math.floor(period.entitledDays / 3) - period.soldDays, 0) : 0
  const days = Number(draft.days) || 0
  return (
    <ConfirmDialog verb="Programar férias" done="Férias programadas" title={`Programar as férias de ${data.name}`} size="lg" open={open} onOpenChange={onOpenChange}
      description={`Até ${data.rules.maxParts} períodos: um com pelo menos ${data.rules.firstPartMinDays} dias e os demais com ${data.rules.otherPartMinDays}. O aviso sai ${data.rules.noticeDays} dias antes e o pagamento vence ${data.rules.paymentDaysBefore} dias antes do início.`}
      onOpen={() => { setDraft(initial()); idempotency.renew() }}
      impact={draft.startsOn && days > 0 ? (
        <ul className="space-y-1 text-sm">
          <li>Gozo: {formatDate(draft.startsOn)} a {formatDate(addDays(draft.startsOn, days - 1))} · retorno em {formatDate(vacationReturn(draft.startsOn, days))}</li>
          <li>Pagamento até {formatDate(addDays(draft.startsOn, -data.rules.paymentDaysBefore))} (o dia não útil antecipa)</li>
        </ul>
      ) : undefined}
      onConfirm={async () => {
        const error = scheduleDraftError(draft, maxSold, period?.balance ?? 0)
        if (error) throw new DialogMessage(error)
        const result = await scheduleVacation(data.employeeId, { periodId: draft.periodId, startsOn: draft.startsOn, days, soldDays: Number(draft.soldDays || '0'), advance13th: draft.advance13th }, idempotency.options)
        idempotency.renew()
        await client.invalidateQueries({ queryKey: getGetEmployeeVacationsQueryKey(data.employeeId) })
        onDone([
          { label: 'Gerar aviso de férias', onSelect: () => void downloadNotice(result.schedule) },
        ], result.warnings)
      }}>
      <Field label="Período aquisitivo" required dica={VACATION_DICAS.aquisitivo}>
        {(f) => (
          <select {...f} value={draft.periodId} onChange={(e) => setDraft({ ...draft, periodId: e.target.value })} className={inputClass}>
            {available.map((p) => <option key={p.id} value={p.id}>{formatDate(p.startsOn)} a {formatDate(p.endsOn)} · saldo {p.balance} dias</option>)}
          </select>
        )}
      </Field>
      <Field label="Início" required hint="Sugerido: o primeiro dia depois do aviso que não fica nos 2 dias antes de feriado ou domingo.">
        {(f) => <DateInput {...f} value={draft.startsOn} onChange={(v) => setDraft({ ...draft, startsOn: v })} />}
      </Field>
      <Field label="Dias de férias" required dica={VACATION_DICAS.fracionamento} hint={period ? `Saldo do período: ${period.balance} dias.` : undefined}>
        {(f) => <input {...f} type="number" min={1} max={30} inputMode="numeric" value={draft.days} onChange={(e) => setDraft({ ...draft, days: e.target.value })} className={inputClass + ' tabular-nums'} />}
      </Field>
      <Field label="Abono pecuniário (dias vendidos)" dica={VACATION_DICAS.abono} hint={`Até ${maxSold} dias.`}>
        {(f) => <input {...f} type="number" min={0} max={maxSold} inputMode="numeric" value={draft.soldDays} onChange={(e) => setDraft({ ...draft, soldDays: e.target.value })} className={inputClass + ' tabular-nums'} />}
      </Field>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" className="size-4" checked={draft.advance13th} onChange={(e) => setDraft({ ...draft, advance13th: e.target.checked })} />
        O funcionário pediu o adiantamento do 13º
      </label>
    </ConfirmDialog>
  )
}

/** Uma programação (CA 5, 6, 8 e 11): datas, situação e as ações que cabem. */
function ScheduleRow({ s, employeeId, onChanged }: { s: VacationScheduleItem; employeeId: string; onChanged: (steps: Step[]) => void }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const canEdit = useCan('hr.edit')
  const canSensitive = useCan('hr.view_sensitive')
  const [reason, setReason] = useState('')
  const [calculating, setCalculating] = useState(false)
  const active = s.status === 'Scheduled' || s.status === 'Paid'
  const reload = () => client.invalidateQueries({ queryKey: getGetEmployeeVacationsQueryKey(employeeId) })
  const runId = s.payrollRunId
  const slipId = s.payslipId
  const receipt = runId && slipId ? () => void download(getDownloadPayslipPdfUrl(runId, slipId), `recibo-ferias-${s.startsOn}.pdf`) : null
  const calculate = async () => {
    setCalculating(true)
    try {
      const result = await calculateVacation(s.id, idempotency.options)
      idempotency.renew()
      notifySuccess(result.message)
      await reload()
      onChanged([
        { label: result.run.isSimulation ? 'Baixar prévia do recibo' : 'Baixar recibo de férias', onSelect: () => void download(getDownloadPayslipPdfUrl(result.run.id, result.payslipId), `recibo-ferias-${s.startsOn}.pdf`) },
        { label: result.run.isSimulation ? 'Ver folha de férias' : 'Fechar folha de férias', onSelect: () => void navigate({ to: '/app/rh/folha/$id', params: { id: result.run.id }, search: { aba: result.run.isSimulation ? 'holerites' : 'fechamento' } as never }) },
      ])
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível calcular as férias.')
    } finally {
      setCalculating(false)
    }
  }
  return (
    <li className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-3">
      <div>
        <p className="font-semibold tabular-nums">{formatDate(s.startsOn)} a {formatDate(s.endsOn)} · {s.days} dias{s.soldDays > 0 ? ` + ${s.soldDays} vendidos` : ''}</p>
        <p className="text-sm text-muted-foreground">
          Pagamento até <span className="tabular-nums">{formatDate(s.paymentDueOn)}</span>{s.advance13th ? ' · com adiantamento do 13º' : ''}
          {s.cancelReason ? ` · cancelada: ${s.cancelReason}` : ''}
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <StatusChip tone={SCHEDULE_TONES[s.status] ?? 'neutral'}>{s.statusLabel}</StatusChip>
        {s.calculated && s.isSimulation ? <StatusChip tone="warn">Simulação</StatusChip> : null}
        {active ? <button type="button" className={button} onClick={() => void downloadNotice(s)}>Gerar aviso de férias</button> : null}
        {active && canEdit && canSensitive && !s.runClosed ? (
          <button type="button" className={button} disabled={calculating} onClick={() => void calculate()}>{calculating ? 'Calculando…' : s.calculated ? 'Recalcular férias' : 'Calcular férias'}</button>
        ) : null}
        {receipt && canSensitive ? <button type="button" className={button} onClick={receipt}>{s.isSimulation ? 'Baixar prévia do recibo' : 'Baixar recibo'}</button> : null}
        {s.payrollRunId ? <Link to="/app/rh/folha/$id" params={{ id: s.payrollRunId }} className="text-sm font-semibold text-primary">Ver folha de férias</Link> : null}
        {active && canEdit ? (
          <ConfirmDialog verb="Cancelar programação" done="Programação cancelada" destructive title="Cancelar estas férias?" onOpen={() => setReason('')}
            description="O saldo volta ao período e os dias saem do espelho. Com a folha de férias fechada, reabra a folha antes."
            onConfirm={async () => {
              if (reason.trim().length < 5) throw new DialogMessage('Informe o motivo do cancelamento, de 5 a 200 caracteres.')
              await cancelVacationSchedule(s.id, { reason: reason.trim(), version: s.version })
              await reload()
            }}
            trigger={<button type="button" className={button + ' text-crit-ink'}>Cancelar programação</button>}>
            <Field label="Motivo" required>{(f) => <input {...f} value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
          </ConfirmDialog>
        ) : null}
      </div>
    </li>
  )
}

/** Aba "Férias" do funcionário (/app/rh/funcionarios/$id?aba=ferias, FLH-03 CA 1, 4, 5, 6 e 11): períodos, programações e "Programar férias". */
export function EmployeeVacations({ employee, canEdit }: { employee: EmployeeDetail; canEdit: boolean }) {
  const query = useGetEmployeeVacations(employee.id)
  const [scheduling, setScheduling] = useState(false)
  const [steps, setSteps] = useState<Step[]>([])
  const [warnings, setWarnings] = useState<string[]>([])
  if (query.isPending) return <div className="space-y-2"><Skeleton className="h-24" /><Skeleton className="h-24" /></div>
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar as férias." message="Confira a conexão e tente de novo." onRetry={() => query.refetch()} />
  const data = query.data
  const canSchedule = canEdit && data.periods.some((p) => (p.status === 'Available' || p.status === 'Expired') && p.balance > 0)
  const schedules = data.schedules
  return (
    <div className="max-w-3xl space-y-5">
      {steps.length > 0 ? <NextSteps steps={steps} onDismiss={() => setSteps([])} /> : null}
      {warnings.map((w) => <p key={w} role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{w}</p>)}
      <section aria-labelledby="ferias-periodos" className="space-y-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="ferias-periodos" className="inline-flex items-center text-lg font-bold">Períodos aquisitivos<Dica nome="Período aquisitivo" texto={VACATION_DICAS.aquisitivo} /></h2>
          {canSchedule ? <button type="button" className={button + ' bg-primary text-primary-foreground'} onClick={() => setScheduling(true)}>Programar férias</button> : null}
        </div>
        {data.periods.length === 0 ? <EmptyState title="Nenhum período aquisitivo completo ainda." description="O primeiro período fica completo 12 meses depois da admissão." />
          : <ul className="space-y-2">{data.periods.map((p) => <PeriodCard key={p.id} p={p} />)}</ul>}
      </section>
      <section aria-labelledby="ferias-programadas" className="space-y-2">
        <h2 id="ferias-programadas" className="text-lg font-bold">Férias programadas</h2>
        {schedules.length === 0 ? <EmptyState title="Nenhuma férias programada." description={canSchedule ? 'Clique em "Programar férias" para escolher as datas.' : 'Quando um período ficar completo, programe as férias aqui.'} />
          : <ul className="space-y-2">{schedules.map((s) => <ScheduleRow key={s.id} s={s} employeeId={employee.id} onChanged={setSteps} />)}</ul>}
      </section>
      {canSchedule ? <ScheduleDialog data={data} open={scheduling} onOpenChange={setScheduling} onDone={(next, w) => { setSteps(next); setWarnings(w) }} /> : null}
    </div>
  )
}
