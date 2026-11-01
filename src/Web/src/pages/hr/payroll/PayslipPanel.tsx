import { useState } from 'react'
import { getPreviousPayslipSensitive, savePayslip, useGetPayslipSensitive, useGetPayslipTimesheetHours, useListPayrollItems } from '@/api/generated/bemaqui'
import type { PayrollItemDetail, PayslipDetail, PayslipLineDetail, PayslipTimesheetReference } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { Money } from '@/components/ui/money'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatMoney } from '@/lib/format'
import { minutes, signed } from '../timeclock/timesheet'
import { KIND_LABELS, PAYROLL_DICAS, periodLabel, totals } from './payroll'

interface Row {
  payrollItemId: string
  reference: string
  amount: string | null
}

interface Draft {
  rows: Row[]
  inssBase: string | null
  fgtsBase: string | null
  irrfBase: string | null
  fgtsAmount: string | null
}

const toRow = (l: PayslipLineDetail): Row => ({ payrollItemId: l.payrollItemId, reference: l.reference ?? '', amount: l.amount.toFixed(2) })
const money = (v: number) => v.toFixed(2)
const toDraft = (p: { lines: PayslipLineDetail[]; inssBase: number; fgtsBase: number; irrfBase: number; fgtsAmount: number }): Draft =>
  ({ rows: p.lines.map(toRow), inssBase: money(p.inssBase), fgtsBase: money(p.fgtsBase), irrfBase: money(p.irrfBase), fgtsAmount: money(p.fgtsAmount) })

/**
 * Holerite (RH-02a CA 5, 6 e 15) em painel lateral (tela cheia a 400 px): as linhas no RowsEditor (rubrica, referência e valor), as
 * bases do INSS, do FGTS e do IRRF e o FGTS do mês; os totais recalculam ao vivo, com o rodapé anunciado em aria-live, pela mesma
 * regra do servidor. "Copiar do mês anterior" traz o holerite anterior do mesmo tipo como rascunho.
 */
export function PayslipPanel({ runId, payslipId, editable, onClose, onSaved }: { runId: string; payslipId: string; editable: boolean; onClose: () => void; onSaved: () => void }) {
  const query = useGetPayslipSensitive(runId, payslipId)
  const items = useListPayrollItems()
  if (query.isPending || items.isPending) {
    return <SidePanel open onOpenChange={(o) => !o && onClose()} title="Holerite"><Skeleton className="h-64" /></SidePanel>
  }

  if (query.isError || items.isError || !query.data) {
    return <SidePanel open onOpenChange={(o) => !o && onClose()} title="Holerite"><ErrorState title="Não foi possível carregar o holerite." onRetry={() => { void query.refetch(); void items.refetch() }} /></SidePanel>
  }

  return <PayslipForm key={query.data.version} payslip={query.data} items={items.data} editable={editable && query.data.editable} onClose={onClose} onReload={() => query.refetch()} onSaved={onSaved} />
}

function PayslipForm({ payslip, items, editable, onClose, onReload, onSaved }: { payslip: PayslipDetail; items: PayrollItemDetail[]; editable: boolean; onClose: () => void; onReload: () => Promise<unknown>; onSaved: () => void }) {
  const start = toDraft(payslip)
  const [draft, setDraft] = useState<Draft>(start)
  const [error, setError] = useState<{ message: string; code?: string; fields: Record<string, string[]> } | null>(null)
  const [copied, setCopied] = useState<string | null>(null)
  const used = new Set(payslip.lines.map((l) => l.payrollItemId))
  const choices = items.filter((i) => i.isActive || used.has(i.id))
  const kindOf = (id: string) => items.find((i) => i.id === id)?.kind ?? null
  const live = totals(draft.rows.map((r) => ({ kind: kindOf(r.payrollItemId), amount: r.amount === null ? null : Number(r.amount) })))
  const dirty = JSON.stringify(draft) !== JSON.stringify(start)

  const save = async () => {
    try {
      await savePayslip(payslip.payrollRunId, payslip.id, {
        lines: draft.rows.map((r) => ({ payrollItemId: r.payrollItemId || null, reference: r.reference.trim() || null, amount: r.amount === null ? 0 : Number(r.amount) })),
        inssBase: Number(draft.inssBase ?? 0), fgtsBase: Number(draft.fgtsBase ?? 0), irrfBase: Number(draft.irrfBase ?? 0), fgtsAmount: Number(draft.fgtsAmount ?? 0), version: payslip.version,
      })
      notifySuccess('Holerite salvo')
      onSaved()
      onClose()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, code: e.code, fields: e.fields } : { message: 'Não foi possível salvar o holerite.', fields: {} })
      return false
    }
  }

  const copy = async () => {
    try {
      const previous = await getPreviousPayslipSensitive(payslip.payrollRunId, payslip.id)
      if (!previous.period) {
        setCopied('Não há holerite anterior deste tipo para este funcionário.')
        return
      }

      setDraft(toDraft(previous))
      setCopied(`Copiado de ${periodLabel(previous.period)}. Confira e salve.`)
    } catch {
      setCopied('Não foi possível copiar do mês anterior.')
    }
  }

  // RH-01c CA 16: só a referência muda; a linha que falta entra sem valor, para o valor da contadora.
  const applyHours = (refs: PayslipTimesheetReference[]) => {
    setDraft((d) => {
      const rows = d.rows.map((r) => {
        const ref = refs.find((x) => x.payrollItemId === r.payrollItemId)
        return ref ? { ...r, reference: ref.reference } : r
      })
      for (const ref of refs) {
        if (ref.minutes > 0 && !rows.some((r) => r.payrollItemId === ref.payrollItemId)) rows.push({ payrollItemId: ref.payrollItemId, reference: ref.reference, amount: null })
      }

      return { ...d, rows }
    })
    setCopied('Referências preenchidas pelo espelho. Os valores continuam vindo da contadora: confira e salve.')
  }

  const rowError = (i: number) => error?.fields[`lines[${i}]`]?.[0]
  const fieldError = (name: string) => error?.fields[name]?.[0]
  const base = (label: string, key: 'inssBase' | 'fgtsBase' | 'irrfBase' | 'fgtsAmount', dica?: string) => (
    <Field label={label} error={fieldError(key)} dica={dica}>
      {(p) => <MoneyInput {...p} value={draft[key]} disabled={!editable} onChange={(v) => setDraft((d) => ({ ...d, [key]: v }))} />}
    </Field>
  )

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={payslip.employeeName} description={payslip.registration ? `Matrícula ${payslip.registration}` : undefined}
      dirty={editable && dirty} onSave={save}
      footer={(
        <>
          <p aria-live="polite" className="mr-auto text-sm tabular-nums">
            Proventos {formatMoney(live.gross)} · Descontos {formatMoney(live.deductions)} · <b>Líquido <Money value={live.net} highlightNegative /></b>
          </p>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {editable ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar holerite</button> : null}
        </>
      )}>
      <div className="space-y-4">
        {error && Object.keys(error.fields).length === 0 ? (
          <div role="alert" className="space-y-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
            <p>{error.message}</p>
            {error.code === 'concurrency_conflict' ? <button type="button" onClick={() => void onReload()} className="rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground">Recarregar</button> : null}
          </div>
        ) : error?.fields.lines ? <FormError message={error.fields.lines[0]} /> : null}
        <TimesheetHours runId={payslip.payrollRunId} payslipId={payslip.id} editable={editable} onUse={applyHours} />
        {editable ? (
          <div className="flex flex-wrap items-center gap-2">
            <button type="button" onClick={() => void copy()} className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Copiar do mês anterior</button>
            {copied ? <p role="status" className="text-sm text-muted-foreground">{copied}</p> : null}
          </div>
        ) : null}
        <RowsEditor<Row> noun="linha" rows={draft.rows} disabled={!editable} max={100}
          onChange={(rows) => setDraft((d) => ({ ...d, rows }))}
          newRow={() => ({ payrollItemId: '', reference: '', amount: null })}
          rowError={rowError}
          summary={`Líquido: ${formatMoney(live.net)}`}
          columns={[
            {
              label: 'Rubrica', width: 'minmax(0, 2fr)', render: (row, i, update) => (
                <select aria-label={`Rubrica da linha ${i + 1}`} value={row.payrollItemId} disabled={!editable} onChange={(e) => update({ payrollItemId: e.target.value })} className={inputClass}>
                  <option value="">Escolha a rubrica</option>
                  {choices.map((item) => <option key={item.id} value={item.id}>{item.code} · {item.name} ({KIND_LABELS[item.kind]})</option>)}
                </select>
              ),
            },
            {
              label: 'Referência', width: '7rem', render: (row, i, update) => (
                <input aria-label={`Referência da linha ${i + 1}`} value={row.reference} maxLength={20} disabled={!editable} onChange={(e) => update({ reference: e.target.value })} placeholder="30 dias" className={inputClass} title={PAYROLL_DICAS.referencia} />
              ),
            },
            {
              label: 'Valor', width: '9rem', render: (row, i, update) => (
                <MoneyInput aria-label={`Valor da linha ${i + 1}`} value={row.amount} disabled={!editable} onChange={(amount) => update({ amount })} />
              ),
            },
          ]} />
        <fieldset className="grid gap-3 sm:grid-cols-2">
          <legend className="mb-2 text-sm font-semibold">Bases e FGTS (da contadora)</legend>
          {base('Base do INSS', 'inssBase', PAYROLL_DICAS.baseInss)}
          {base('Base do FGTS', 'fgtsBase')}
          {base('Base do IRRF', 'irrfBase')}
          {base('FGTS do mês', 'fgtsAmount', PAYROLL_DICAS.informativa)}
        </fieldset>
      </div>
    </SidePanel>
  )
}

/** "Usar horas do espelho" (RH-01c CA 16): os totais do espelho fechado do mês e o botão que preenche só a referência das rubricas "Origem no ponto". */
function TimesheetHours({ runId, payslipId, editable, onUse }: { runId: string; payslipId: string; editable: boolean; onUse: (refs: PayslipTimesheetReference[]) => void }) {
  const query = useGetPayslipTimesheetHours(runId, payslipId, { query: { retry: false } })
  if (query.isPending) return <Skeleton className="h-16" />
  if (query.isError) return null
  const h = query.data
  if (!h.available) return <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm text-muted-foreground">{h.message}</p>
  return (
    <section aria-label="Horas do espelho" className="space-y-2 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
      <p className="font-semibold">Espelho de ponto · {h.message}</p>
      <p className="tabular-nums">
        Extras {minutes(h.overtimeMinutes, '0 min')} · Faltas {minutes(h.absenceMinutes, '0 min')} · Atrasos {minutes(h.lateMinutes, '0 min')}
        {h.bankBalanceMinutes !== null && h.bankBalanceMinutes !== undefined ? ` · Banco ${signed(h.bankMonthMinutes)} no período, ${signed(h.bankBalanceMinutes)} acumulado` : ''}
      </p>
      {editable && h.references.length > 0 ? (
        <button type="button" onClick={() => onUse(h.references)} className="min-h-11 rounded-md border border-border bg-card px-3 py-2 font-semibold text-foreground">Usar horas do espelho</button>
      ) : null}
    </section>
  )
}
