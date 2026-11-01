import { useState } from 'react'
import { clearPayslipHourBank, setPayslipHourBank, useGetPayrollHourBankSensitive } from '@/api/generated/bemaqui'
import type { PayrollHourBankRow, PayrollRunSummary } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { hoursLabel, PAYROLL_DICAS, parseHours } from './payroll'

const NOTE_MIN = 5
const NOTE_MAX = 200

/**
 * Seção "Banco de horas" da folha calculada (FLH-02b CA 2): por funcionário com acordo, o saldo do espelho fechado, a decisão desta folha
 * e o saldo depois dela. O saldo não vence sozinho: "Pagar {n} h do banco" ou "Descontar {n} h do banco", com o motivo, recalcula o
 * holerite; a baixa no espelho acontece no fechamento. Com o saldo acima de 40 h, sugere pagar (sem decidir). Em cartões (400 px).
 */
export function HourBankSection({ run, editable, onChanged }: { run: PayrollRunSummary; editable: boolean; onChanged: () => void }) {
  const query = useGetPayrollHourBankSensitive(run.id)
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o banco de horas." message="Confira a conexão e tente de novo. Os holerites abaixo continuam valendo." onRetry={() => query.refetch()} />
  const canDecide = editable && query.data.editable
  const changed = async () => { await query.refetch(); onChanged() }
  return (
    <section aria-label="Banco de horas" className="space-y-2 rounded-md border border-border p-3">
      <h3 className="inline-flex items-center text-sm font-semibold">Banco de horas<Dica nome="Banco de horas" texto={PAYROLL_DICAS.bancoHoras} /></h3>
      {query.data.rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">Sem banco de horas neste mês.</p>
      ) : (
        <ul aria-label="Saldos do banco de horas" className="grid gap-2 md:grid-cols-2">
          {query.data.rows.map((row) => <BankCard key={row.payslipId} runId={run.id} row={row} canDecide={canDecide} onChanged={changed} />)}
        </ul>
      )}
    </section>
  )
}

function BankCard({ runId, row, canDecide, onChanged }: { runId: string; row: PayrollHourBankRow; canDecide: boolean; onChanged: () => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const decided = row.decisionMinutes !== 0
  return (
    <li className="space-y-2 rounded-md border border-border p-3 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-semibold">{row.employeeName}{row.registration ? <span className="font-normal text-muted-foreground"> · {row.registration}</span> : null}</span>
        <span className="tabular-nums">Saldo <b>{hoursLabel(row.balanceMinutes, true)}</b></span>
      </div>
      <p className="tabular-nums text-muted-foreground">No período {hoursLabel(row.monthMinutes, true)} · depois da folha {hoursLabel(row.balanceAfterMinutes, true)}</p>
      {decided ? (
        <div className="flex flex-wrap items-center gap-2">
          <StatusChip tone="info">{row.decisionMinutes > 0 ? `Pagar ${hoursLabel(row.decisionMinutes)} do banco` : `Descontar ${hoursLabel(-row.decisionMinutes)} do banco`}</StatusChip>
          <span className="text-muted-foreground">{row.decisionNote}</span>
          {canDecide ? <ClearButton runId={runId} row={row} onChanged={onChanged} /> : null}
        </div>
      ) : row.suggestedPayMinutes ? (
        <p role="note" className="flex flex-wrap items-center gap-2">
          <StatusChip tone="warn">Saldo acima de 40 h</StatusChip>
          {canDecide ? <button type="button" onClick={() => setOpen(true)} className="min-h-11 text-xs font-semibold text-primary sm:min-h-0">Pagar {hoursLabel(row.suggestedPayMinutes)} do banco</button> : <span>Sugestão: pagar {hoursLabel(row.suggestedPayMinutes)} do banco.</span>}
        </p>
      ) : null}
      {canDecide && !decided && !row.suggestedPayMinutes && row.balanceMinutes !== 0 && !open ? (
        <button type="button" onClick={() => setOpen(true)} className="min-h-11 text-xs font-semibold text-primary sm:min-h-0">{row.balanceMinutes > 0 ? 'Pagar horas do banco' : 'Descontar horas do banco'}</button>
      ) : null}
      {open ? <DecisionForm runId={runId} row={row} onDone={async () => { setOpen(false); await onChanged() }} onCancel={() => setOpen(false)} /> : null}
    </li>
  )
}

function ClearButton({ runId, row, onChanged }: { runId: string; row: PayrollHourBankRow; onChanged: () => Promise<void> }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const clear = async () => {
    setBusy(true)
    try {
      await clearPayslipHourBank(runId, row.payslipId)
      notifySuccess('Decisão retirada')
      await onChanged()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível tirar a decisão.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <button type="button" disabled={busy} onClick={() => void clear()} className="min-h-11 text-xs font-semibold text-crit-ink sm:min-h-0">Tirar</button>
      {error ? <p role="alert" className="w-full text-xs font-medium text-crit-ink">{error}</p> : null}
    </>
  )
}

/** "Pagar" (saldo positivo) ou "Descontar" (saldo negativo), já preenchido com a sugestão ou o saldo; o motivo é obrigatório. */
function DecisionForm({ runId, row, onDone, onCancel }: { runId: string; row: PayrollHourBankRow; onDone: () => Promise<void>; onCancel: () => void }) {
  const pay = row.balanceMinutes > 0
  const proposed = row.suggestedPayMinutes ?? Math.abs(row.balanceMinutes)
  const [hours, setHours] = useState(String(Math.floor(proposed / 60)) + (proposed % 60 ? `:${String(proposed % 60).padStart(2, '0')}` : ''))
  const [note, setNote] = useState('')
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const [busy, setBusy] = useState(false)
  const minutes = parseHours(hours)
  const verb = pay ? 'Pagar' : 'Descontar'
  const submit = async () => {
    setError(null)
    if (minutes === null) {
      setError({ message: '', fields: { minutes: ['Informe as horas, maior que zero (ex.: 10 ou 10:30).'] } })
      return
    }

    if (note.trim().length < NOTE_MIN) {
      setError({ message: '', fields: { note: [`Informe o motivo, de ${NOTE_MIN} a ${NOTE_MAX} caracteres.`] } })
      return
    }

    setBusy(true)
    try {
      await setPayslipHourBank(runId, row.payslipId, { action: pay ? 'Pay' : 'Discount', minutes, note: note.trim() })
      notifySuccess(pay ? 'Horas do banco pagas na folha' : 'Horas do banco descontadas na folha')
      await onDone()
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível gravar a decisão do banco.', fields: {} })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-2 rounded-md border border-border p-2">
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      <Field label={`Horas para ${verb.toLowerCase()}`} required error={error?.fields.minutes?.[0]} hint={`Até ${hoursLabel(Math.abs(row.balanceMinutes))}. ${pay ? 'Pagas como hora extra de dia útil.' : 'Descontadas pelo valor da hora.'}`}>
        {(p) => <input {...p} inputMode="decimal" value={hours} onChange={(e) => setHours(e.target.value.replace(/[^0-9:,.]/g, ''))} className={inputClass + ' max-w-28 tabular-nums'} />}
      </Field>
      <Field label="Motivo" required error={error?.fields.note?.[0]} hint="Fica no holerite e na auditoria.">
        {(p) => <textarea {...p} value={note} maxLength={NOTE_MAX} rows={2} onChange={(e) => setNote(e.target.value)} className={inputClass} />}
      </Field>
      <div className="flex flex-wrap gap-2">
        <button type="button" disabled={busy} onClick={() => void submit()} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">
          {verb} {minutes ? hoursLabel(minutes) : ''} do banco
        </button>
        <button type="button" onClick={onCancel} className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Cancelar</button>
      </div>
    </div>
  )
}
