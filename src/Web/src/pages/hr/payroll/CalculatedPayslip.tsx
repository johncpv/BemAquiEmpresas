import { useState } from 'react'
import { addPayslipManualLine, removePayslipManualLine } from '@/api/generated/bemaqui'
import type { PayrollItemDetail, PayslipDetail, PayslipLineDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { isManualCalculation, KIND_LABELS, PAYROLL_DICAS } from './payroll'

const NOTE_MIN = 5
const NOTE_MAX = 200

/**
 * Holerite da folha calculada (FLH-02a CA 12 e 13), em painel lateral (tela cheia a 400 px): as linhas em cartões, cada uma com "Ver
 * memória"; os avulsos com o motivo e "Tirar"; "Lançar avulso" (rubrica sem regra, com motivo) recalcula o holerite; o bloco "Memória de
 * cálculo" com a versão do motor, as tabelas, os parâmetros, as entradas do retrato e os encargos da empresa. Linha calculada não se edita.
 */
export function CalculatedPayslip({ payslip, items, editable, onClose, onChanged }: {
  payslip: PayslipDetail; items: PayrollItemDetail[]; editable: boolean; onClose: () => void; onChanged: () => Promise<unknown>
}) {
  const info = payslip.calculation
  const manualItems = items.filter((i) => i.isActive && isManualCalculation(i.calculation))
  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={payslip.employeeName} description={payslip.registration ? `Matrícula ${payslip.registration}` : undefined}
      footer={(
        <>
          <p aria-live="polite" className="mr-auto text-sm tabular-nums">
            Proventos <Money value={payslip.gross} /> · Descontos <Money value={payslip.deductions} /> · <b>Líquido <Money value={payslip.net} highlightNegative /></b>
          </p>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
        </>
      )}>
      <div className="space-y-4">
        {info?.isSimulation ? (
          <p role="note" className="inline-flex flex-wrap items-center rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
            Simulação: não é o holerite oficial.<Dica nome="Simulação" texto={PAYROLL_DICAS.simulacao} />
          </p>
        ) : null}
        {!info ? (
          <EmptyState title="Holerite ainda não calculado." description="Clique em &quot;Calcular&quot; no topo da folha para o sistema calcular as linhas." />
        ) : (
          <>
            {info.issues.length > 0 ? (
              <ul aria-label="Avisos do cálculo" className="space-y-1 text-sm">
                {info.issues.map((i) => <li key={i}><StatusChip tone="warn">Aviso</StatusChip> {i}</li>)}
              </ul>
            ) : null}
            {payslip.lines.length === 0 ? <p className="text-sm text-muted-foreground">Sem linhas: confira os avisos do cálculo.</p> : (
              <ul aria-label="Linhas do holerite" className="space-y-2">
                {payslip.lines.map((l, i) => <LineCard key={l.lineId ?? i} payslip={payslip} line={l} editable={editable} onChanged={onChanged} />)}
              </ul>
            )}
            <dl className="grid gap-2 rounded-md border border-border p-3 text-sm sm:grid-cols-2">
              <div><dt className="inline-flex items-center text-muted-foreground">Base do INSS<Dica nome="Base do INSS" texto={PAYROLL_DICAS.baseInss} /></dt><dd className="font-semibold"><Money value={payslip.inssBase} /></dd></div>
              <div><dt className="text-muted-foreground">Base do IRRF</dt><dd className="font-semibold"><Money value={payslip.irrfBase} /></dd></div>
              <div><dt className="text-muted-foreground">Base do FGTS</dt><dd className="font-semibold"><Money value={payslip.fgtsBase} /></dd></div>
              <div><dt className="inline-flex items-center text-muted-foreground">FGTS do mês<Dica nome="FGTS do mês" texto={PAYROLL_DICAS.informativa} /></dt><dd className="font-semibold"><Money value={payslip.fgtsAmount} /></dd></div>
            </dl>
            {editable ? <ManualForm payslip={payslip} items={manualItems} onChanged={onChanged} /> : null}
            <section aria-label="Memória de cálculo" className="space-y-2 rounded-md border border-border p-3 text-sm">
              <h3 className="inline-flex items-center font-semibold">Memória de cálculo<Dica nome="Memória de cálculo" texto={PAYROLL_DICAS.memoria} /></h3>
              <p className="text-muted-foreground">Motor {info.calculatorVersion}{info.calculatedAt ? ` · calculado em ${formatDateTime(info.calculatedAt)}` : ''}</p>
              <p className="inline-flex flex-wrap items-center text-muted-foreground">No IRRF, vale a dedução legal ou o desconto simplificado, o que der o menor imposto.<Dica nome="Desconto simplificado" texto={PAYROLL_DICAS.simplificado} /></p>
              <p className="font-semibold">Tabelas usadas</p>
              <ul className="space-y-0.5">
                {info.tables.map((t) => <li key={t.id} className="flex flex-wrap items-center gap-2">{t.label}<StatusChip tone={t.status === 'Active' ? 'ok' : 'warn'}>{t.status === 'Active' ? 'Conferida' : 'A confirmar com a contadora'}</StatusChip></li>)}
              </ul>
              <Pairs title="Entradas do cadastro e do ponto" rows={info.inputs} />
              <Pairs title="Parâmetros" rows={info.parameters} />
              <p className="font-semibold">Encargos da empresa: <Money value={info.employerCharges} /></p>
              <ul className="list-disc space-y-0.5 pl-5 text-muted-foreground">{info.employerChargesMemory.map((m) => <li key={m}>{m}</li>)}</ul>
            </section>
          </>
        )}
      </div>
    </SidePanel>
  )
}

function Pairs({ title, rows }: { title: string; rows: { label: string; value: string }[] }) {
  return (
    <>
      <p className="font-semibold">{title}</p>
      <dl className="grid gap-x-4 gap-y-0.5 sm:grid-cols-2">
        {rows.map((r) => <div key={r.label} className="flex justify-between gap-2"><dt className="text-muted-foreground">{r.label}</dt><dd className="tabular-nums">{r.value}</dd></div>)}
      </dl>
    </>
  )
}

function LineCard({ payslip, line, editable, onChanged }: { payslip: PayslipDetail; line: PayslipLineDetail; editable: boolean; onChanged: () => Promise<unknown> }) {
  const [open, setOpen] = useState(false)
  const memory = line.memory ?? []
  const id = `memoria-${line.lineId ?? line.code}`
  return (
    <li className="space-y-1 rounded-md border border-border p-3 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-semibold">{line.code} · {line.description}</span>
        <span className="flex items-center gap-2 tabular-nums">
          {line.reference ? <span className="text-muted-foreground">{line.reference}</span> : null}
          <span className="text-muted-foreground">{KIND_LABELS[line.type]}</span>
          <Money value={line.amount} className="font-semibold" />
        </span>
      </div>
      {line.calculated ? null : <p className="text-muted-foreground"><StatusChip tone="neutral">Avulso</StatusChip> {line.note}</p>}
      <div className="flex flex-wrap gap-2">
        {memory.length > 0 ? (
          <button type="button" aria-expanded={open} aria-controls={id} onClick={() => setOpen((o) => !o)} className="min-h-11 text-xs font-semibold text-primary sm:min-h-0">{open ? 'Esconder memória' : 'Ver memória'}</button>
        ) : null}
        {editable && !line.calculated && line.lineId ? (
          <ConfirmDialog verb="Tirar" done="Avulso retirado" title={`Tirar o avulso ${line.code}?`} description="O holerite é recalculado sem este lançamento."
            onConfirm={async () => { await removePayslipManualLine(payslip.payrollRunId, payslip.id, line.lineId!); await onChanged() }}
            trigger={<button type="button" className="min-h-11 text-xs font-semibold text-crit-ink sm:min-h-0">Tirar</button>} />
        ) : null}
      </div>
      {open ? <ul id={id} className="list-disc space-y-0.5 pl-5 text-muted-foreground">{memory.map((m) => <li key={m}>{m}</li>)}</ul> : null}
    </li>
  )
}

/** "Lançar avulso" (CA 13): só rubrica sem regra; o motivo é obrigatório; o holerite volta recalculado. */
function ManualForm({ payslip, items, onChanged }: { payslip: PayslipDetail; items: PayrollItemDetail[]; onChanged: () => Promise<unknown> }) {
  const [item, setItem] = useState('')
  const [reference, setReference] = useState('')
  const [amount, setAmount] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async () => {
    setError(null)
    if (note.trim().length < NOTE_MIN) {
      setError({ message: '', fields: { note: [`Informe o motivo do lançamento, de ${NOTE_MIN} a ${NOTE_MAX} caracteres.`] } })
      return
    }

    setBusy(true)
    try {
      await addPayslipManualLine(payslip.payrollRunId, payslip.id, { payrollItemId: item || null, reference: reference.trim() || null, amount: amount === null ? 0 : Number(amount), note: note.trim() })
      notifySuccess('Avulso lançado')
      setItem('')
      setReference('')
      setAmount(null)
      setNote('')
      await onChanged()
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível lançar o avulso.', fields: {} })
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-label="Lançar avulso" className="space-y-3 rounded-md border border-border p-3">
      <h3 className="inline-flex items-center text-sm font-semibold">Lançar avulso<Dica nome="Lançamento avulso" texto={PAYROLL_DICAS.avulso} /></h3>
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      <div className="grid gap-3 sm:grid-cols-3">
        <div className="sm:col-span-2">
          <Field label="Rubrica" required error={error?.fields.payrollItemId?.[0]}>
            {(p) => (
              <select {...p} value={item} onChange={(e) => setItem(e.target.value)} className={inputClass}>
                <option value="">Escolha a rubrica sem regra</option>
                {items.map((i) => <option key={i.id} value={i.id}>{i.code} · {i.name} ({KIND_LABELS[i.kind]})</option>)}
              </select>
            )}
          </Field>
        </div>
        <Field label="Valor" required error={error?.fields.amount?.[0]}>{(p) => <MoneyInput {...p} value={amount} onChange={setAmount} />}</Field>
      </div>
      <Field label="Referência" error={error?.fields.reference?.[0]} hint="Opcional, até 20 caracteres.">
        {(p) => <input {...p} value={reference} maxLength={20} onChange={(e) => setReference(e.target.value)} className={inputClass} />}
      </Field>
      <Field label="Motivo" required error={error?.fields.note?.[0]} hint="Fica no holerite e na auditoria. Ex.: pensão judicial, ofício 77/2026.">
        {(p) => <textarea {...p} value={note} maxLength={NOTE_MAX} rows={2} onChange={(e) => setNote(e.target.value)} className={inputClass} />}
      </Field>
      <button type="button" disabled={busy} onClick={() => void submit()} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Lançar avulso</button>
    </section>
  )
}
