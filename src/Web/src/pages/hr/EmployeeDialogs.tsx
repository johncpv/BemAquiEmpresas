import { useState } from 'react'
import { changeEmployeePosition, changeEmployeeSalary, getPositionSalary, registerEmployeeLeave, terminateEmployee, useListPositions } from '@/api/generated/bemaqui'
import type { EmployeeDetail } from '@/api/generated/model'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { formatDate, formatMoney } from '@/lib/format'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { EMPLOYEE_DICAS, emptyLeaveDraft, LEAVE_REASON_MAX, LEAVE_TYPE_LABELS, type LeaveType, leaveDraftError, returnDate, todayIso } from './employee'

const button = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

function filled(value: string, message: string) {
  if (!value.trim()) throw new DialogMessage(message)
  return value.trim()
}

/** Reajustar salário (CA 14): novo salário, vigência e motivo; o anterior vai para a linha do tempo. */
export function SalaryDialog({ employee, current, onDone }: { employee: EmployeeDetail; current: number; onDone: (e: EmployeeDetail) => void }) {
  const [salary, setSalary] = useState<string | null>(null)
  const [date, setDate] = useState(todayIso())
  const [reason, setReason] = useState('')
  const reset = () => { setSalary(null); setDate(todayIso()); setReason('') }
  return (
    <ConfirmDialog verb="Reajustar salário" done="Salário reajustado" title={`Reajustar o salário de ${employee.name}`} description={`Salário atual: ${formatMoney(current)}.`} onOpen={reset}
      onConfirm={async () => {
        if (!salary || Number(salary) <= 0) throw new DialogMessage('Informe o novo salário.')
        onDone(await changeEmployeeSalary(employee.id, { newSalary: Number(salary), effectiveDate: filled(date, 'Informe a data de vigência.'), reason: filled(reason, 'Informe o motivo.') }))
      }}
      trigger={<button type="button" className={button}>Reajustar salário</button>}>
      <Field label="Novo salário" required>{(p) => <MoneyInput {...p} value={salary} onChange={setSalary} />}</Field>
      <Field label="Vigência" required>{(p) => <DateInput {...p} value={date} onChange={setDate} />}</Field>
      <Field label="Motivo" required hint="Ex.: dissídio, mérito, promoção.">{(p) => <input {...p} value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
    </ConfirmDialog>
  )
}

/** Trocar cargo (CA 15): com hr.view_sensitive, sugere o salário de referência do cargo novo; aplicar é escolha do usuário. */
export function PositionDialog({ employee, onDone }: { employee: EmployeeDetail; onDone: (e: EmployeeDetail) => void }) {
  const canSensitive = useCan('hr.view_sensitive')
  const positions = useListPositions({ pageSize: 100 })
  const [positionId, setPositionId] = useState('')
  const [date, setDate] = useState(todayIso())
  const [reason, setReason] = useState('')
  const [reference, setReference] = useState<number | null>(null)
  const [apply, setApply] = useState(true)
  const reset = () => { setPositionId(''); setDate(todayIso()); setReason(''); setReference(null); setApply(true) }
  const choose = async (id: string) => {
    setPositionId(id)
    setReference(null)
    if (canSensitive && id) setReference((await getPositionSalary(id).catch(() => null))?.referenceSalary ?? null)
  }
  const options = (positions.data?.items ?? []).filter((p) => p.isActive && p.id !== employee.positionId)
  return (
    <ConfirmDialog verb="Trocar cargo" done="Cargo trocado" title={`Trocar o cargo de ${employee.name}`} description={`Cargo atual: ${employee.positionName}.`} onOpen={reset}
      onConfirm={async () => {
        if (!positionId) throw new DialogMessage('Escolha o cargo novo.')
        onDone(await changeEmployeePosition(employee.id, {
          positionId, effectiveDate: filled(date, 'Informe a data de vigência.'), reason: filled(reason, 'Informe o motivo.'), newSalary: reference !== null && apply ? reference : null,
        }))
      }}
      trigger={<button type="button" className={button}>Trocar cargo</button>}>
      <Field label="Cargo novo" required>
        {(p) => (
          <select {...p} value={positionId} onChange={(e) => void choose(e.target.value)} className={inputClass}>
            <option value="">Escolha</option>
            {options.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
          </select>
        )}
      </Field>
      <Field label="Vigência" required>{(p) => <DateInput {...p} value={date} onChange={setDate} />}</Field>
      <Field label="Motivo" required hint="Ex.: promoção, reorganização.">{(p) => <input {...p} value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
      {reference !== null ? (
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="size-4" checked={apply} onChange={(e) => setApply(e.target.checked)} />
          Aplicar o salário de referência do cargo novo ({formatMoney(reference)})
        </label>
      ) : null}
    </ConfirmDialog>
  )
}

/** Registrar desligamento (CA 17): a data precisa ser igual ou depois da admissão; nada é apagado. */
export function TerminationDialog({ employee, onDone, open, onOpenChange }: { employee: EmployeeDetail; onDone: (e: EmployeeDetail) => void; open?: boolean; onOpenChange?: (o: boolean) => void }) {
  const [date, setDate] = useState(todayIso())
  return (
    <ConfirmDialog verb="Registrar desligamento" done="Desligamento registrado" title={`Registrar o desligamento de ${employee.name}?`}
      description="A situação passa a Desligado e ele some dos seletores. Nada é apagado; o usuário vinculado continua ativo até você desativar." onOpen={() => setDate(todayIso())}
      open={open} onOpenChange={onOpenChange}
      onConfirm={async () => onDone(await terminateEmployee(employee.id, { terminationDate: filled(date, 'Informe a data do desligamento.') }))}
      trigger={open === undefined ? <button type="button" className={button}>Registrar desligamento</button> : undefined}>
      <Field label="Data do desligamento" required>{(p) => <DateInput {...p} value={date} onChange={setDate} />}</Field>
    </ConfirmDialog>
  )
}

/**
 * Registrar afastamento (CAD-04c1 CA 1 e 5): tipo, início (vem em hoje), fim previsto e motivo sem CID. Tela cheia a 400 px;
 * erro dentro do diálogo; 409 de versão oferece "Recarregar" (ADR 0019).
 */
export function LeaveDialog({ employee, onDone, onReload, open, onOpenChange }: {
  employee: EmployeeDetail
  onDone: (e: EmployeeDetail) => void
  onReload: () => Promise<unknown>
  open: boolean
  onOpenChange: (o: boolean) => void
}) {
  const [draft, setDraft] = useState(emptyLeaveDraft(todayIso()))
  const set = (patch: Partial<typeof draft>) => setDraft((d) => ({ ...d, ...patch }))
  return (
    <ConfirmDialog verb="Registrar afastamento" done="Afastamento registrado" title={`Registrar afastamento de ${employee.name}`} size="lg"
      description="Durante o afastamento, a situação fica Afastado e o apontamento recusa o funcionário. A situação volta a Ativo sozinha no dia seguinte ao fim previsto."
      open={open} onOpenChange={onOpenChange} onOpen={() => setDraft(emptyLeaveDraft(todayIso()))} onReload={onReload}
      onConfirm={async () => {
        const error = leaveDraftError(draft)
        if (error) throw new DialogMessage(error)
        onDone(await registerEmployeeLeave(employee.id, {
          type: draft.type as LeaveType, startDate: draft.startDate, expectedEndDate: draft.expectedEndDate, reason: draft.reason.trim() || null, version: employee.version,
        }))
      }}>
      <Field label="Tipo" required>
        {(p) => (
          <select {...p} value={draft.type} onChange={(e) => set({ type: e.target.value as LeaveType | '' })} className={inputClass}>
            <option value="">Escolha</option>
            {(Object.keys(LEAVE_TYPE_LABELS) as LeaveType[]).map((t) => <option key={t} value={t}>{LEAVE_TYPE_LABELS[t]}</option>)}
          </select>
        )}
      </Field>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Início" required>{(p) => <DateInput {...p} value={draft.startDate} onChange={(v) => set({ startDate: v })} />}</Field>
        <Field label="Fim previsto" required hint="Último dia afastado.">{(p) => <DateInput {...p} value={draft.expectedEndDate} onChange={(v) => set({ expectedEndDate: v })} />}</Field>
      </div>
      {draft.expectedEndDate && draft.expectedEndDate >= draft.startDate ? <p className="text-sm tabular-nums text-muted-foreground">Volta prevista em {formatDate(returnDate(draft.expectedEndDate))}.</p> : null}
      <Field label="Motivo" hint="Não informe CID nem diagnóstico.">
        {(p) => (
          <div className="flex items-start gap-1.5">
            <input {...p} value={draft.reason} maxLength={LEAVE_REASON_MAX} onChange={(e) => set({ reason: e.target.value })} className={inputClass} />
            <Dica nome="Motivo do afastamento" texto={EMPLOYEE_DICAS.motivoAfastamento} />
          </div>
        )}
      </Field>
    </ConfirmDialog>
  )
}