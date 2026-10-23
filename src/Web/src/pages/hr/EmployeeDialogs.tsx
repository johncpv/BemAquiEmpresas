import { useState } from 'react'
import { cancelEmployeeLeave, changeEmployeePosition, changeEmployeeSalary, endEmployeeLeave, getPositionSalary, registerEmployeeLeave, terminateEmployee, updateEmployeeLeave, useListPositions } from '@/api/generated/bemaqui'
import type { EmployeeDetail, LeaveDetail } from '@/api/generated/model'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { formatDate, formatMoney } from '@/lib/format'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import {
  EMPLOYEE_DICAS, emptyLeaveDraft, LEAVE_REASON_MAX, LEAVE_TYPE_LABELS, type LeaveDraft, type LeaveType, leaveCancelError, leaveDraftError, leaveDraftFrom, leaveReturnError,
  leaveTypeOptions, previousDay, returnDate, todayIso,
} from './employee'

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

const period = (start: string, end: string) => `${formatDate(start)} a ${formatDate(end)}`

/** Antes → depois do afastamento editado (CAD-04c1b CA 1), no bloco de impacto do diálogo. */
function LeaveEditImpact({ leave, draft }: { leave: LeaveDetail; draft: LeaveDraft }) {
  const type = (t: LeaveType | '' | null) => (t ? LEAVE_TYPE_LABELS[t] : 'Afastado')
  const sameType = (leave.type ?? '') === draft.type
  const samePeriod = leave.startDate === draft.startDate && leave.expectedEndDate === draft.expectedEndDate
  return (
    <ul className="space-y-1">
      <li>Período: {period(leave.startDate, leave.expectedEndDate)}{samePeriod || !draft.expectedEndDate ? '' : ` → ${period(draft.startDate, draft.expectedEndDate)}`}</li>
      {leave.healthProtected ? null : <li>Tipo: {type(leave.type)}{sameType || !draft.type ? '' : ` → ${type(draft.type)}`}</li>}
    </ul>
  )
}

/**
 * Registrar afastamento (CAD-04c1 CA 1 e 5) ou editar o previsto ou em curso (CAD-04c1b CA 1 e 2): tipo, início (no registro
 * vem em hoje; na edição, o gravado), fim previsto e motivo sem CID. Doença e Acidente de trabalho só aparecem com
 * hr.view_health. Tela cheia a 400 px; erro dentro do diálogo; 409 de versão oferece "Recarregar" (ADR 0019).
 */
export function LeaveDialog({ employee, leave, canHealth, onDone, onReload, open, onOpenChange }: {
  employee: EmployeeDetail
  /** Com o afastamento, o diálogo edita ("Salvar afastamento"); sem ele, registra. */
  leave?: LeaveDetail
  canHealth: boolean
  onDone: (e: EmployeeDetail) => void
  onReload: () => Promise<unknown>
  open: boolean
  onOpenChange: (o: boolean) => void
}) {
  const initial = () => (leave ? leaveDraftFrom(leave) : emptyLeaveDraft(todayIso()))
  const [draft, setDraft] = useState(initial)
  const set = (patch: Partial<typeof draft>) => setDraft((d) => ({ ...d, ...patch }))
  const current = leave?.state === 'Current'
  return (
    <ConfirmDialog verb={leave ? 'Salvar afastamento' : 'Registrar afastamento'} done={leave ? 'Afastamento salvo' : 'Afastamento registrado'}
      title={leave ? `Editar afastamento de ${employee.name}` : `Registrar afastamento de ${employee.name}`} size="lg"
      description={leave
        ? current
          ? 'O afastamento já começou: o início fica hoje ou antes e o fim previsto, hoje ou depois. Se ele voltou antes, use "Encerrar afastamento".'
          : 'Corrija o tipo, as datas ou o motivo. A situação e a linha do tempo mudam na hora; o antes e o depois ficam no rastro.'
        : 'Durante o afastamento, a situação fica Afastado e o apontamento recusa o funcionário. A situação volta a Ativo sozinha no dia seguinte ao fim previsto.'}
      open={open} onOpenChange={onOpenChange} onOpen={() => setDraft(initial())} onReload={onReload}
      impact={leave ? <LeaveEditImpact leave={leave} draft={draft} /> : undefined}
      onConfirm={async () => {
        const error = leaveDraftError(draft)
        if (error) throw new DialogMessage(error)
        const body = { type: draft.type as LeaveType, startDate: draft.startDate, expectedEndDate: draft.expectedEndDate, reason: draft.reason.trim() || null, version: employee.version }
        onDone(await (leave ? updateEmployeeLeave(employee.id, leave.id, body) : registerEmployeeLeave(employee.id, body)))
      }}>
      <Field label="Tipo" required dica={canHealth ? undefined : EMPLOYEE_DICAS.saude}>
        {(p) => (
          <select {...p} value={draft.type} onChange={(e) => set({ type: e.target.value as LeaveType | '' })} className={inputClass}>
            <option value="">Escolha</option>
            {leaveTypeOptions(canHealth).map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
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

interface LeaveActionProps {
  employee: EmployeeDetail
  leave: LeaveDetail
  onDone: (e: EmployeeDetail) => void
  onReload: () => Promise<unknown>
  open: boolean
  onOpenChange: (o: boolean) => void
}

/**
 * Encerrar antes do previsto (CAD-04c1b CA 4 e 5): a volta vem em hoje (propositivo) e fica entre o início e o fim previsto,
 * nunca depois de hoje. O impacto mostra o fim previsto antes e depois; a situação muda na hora.
 */
export function EndLeaveDialog({ employee, leave, onDone, onReload, open, onOpenChange }: LeaveActionProps) {
  const [back, setBack] = useState(todayIso())
  const valid = !leaveReturnError(leave, back, todayIso())
  return (
    <ConfirmDialog verb="Encerrar afastamento" done="Afastamento encerrado" title={`Encerrar o afastamento de ${employee.name}?`} size="lg"
      description="Use quando o funcionário voltou antes do fim previsto. A situação volta a Ativo a partir da volta e o fim do afastamento é avisado uma vez só."
      open={open} onOpenChange={onOpenChange} onOpen={() => setBack(todayIso())} onReload={onReload}
      impact={(
        <ul className="space-y-1">
          <li>Fim previsto: {formatDate(leave.expectedEndDate)}{valid ? ` → ${formatDate(previousDay(back))}` : ''}</li>
          <li>Volta: {formatDate(leave.expectedReturnDate)}{valid ? ` → ${formatDate(back)}` : ''}</li>
        </ul>
      )}
      onConfirm={async () => {
        const error = leaveReturnError(leave, back, todayIso())
        if (error) throw new DialogMessage(error)
        onDone(await endEmployeeLeave(employee.id, leave.id, { returnDate: back, version: employee.version }))
      }}>
      <Field label="Data da volta" required dica={EMPLOYEE_DICAS.volta} hint={`Entre ${formatDate(leave.startDate)} e ${formatDate(leave.expectedEndDate)}, até hoje.`}>
        {(p) => <DateInput {...p} value={back} onChange={setBack} />}
      </Field>
    </ConfirmDialog>
  )
}

/**
 * Cancelar o afastamento que não começou (CAD-04c1b CA 6): cancelamento lógico com motivo; ele continua na linha do tempo como
 * "Cancelado". Afastamento já iniciado não se cancela, só se encerra.
 */
export function CancelLeaveDialog({ employee, leave, onDone, onReload, open, onOpenChange }: LeaveActionProps) {
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog verb="Cancelar afastamento" done="Afastamento cancelado" destructive title={`Cancelar o afastamento de ${employee.name}?`}
      description="O afastamento previsto deixa de valer e não conta na situação. Ele continua na linha do tempo como Cancelado, com o motivo."
      open={open} onOpenChange={onOpenChange} onOpen={() => setReason('')} onReload={onReload}
      impact={<p>Período: {period(leave.startDate, leave.expectedEndDate)} → cancelado</p>}
      onConfirm={async () => {
        const error = leaveCancelError(reason)
        if (error) throw new DialogMessage(error)
        onDone(await cancelEmployeeLeave(employee.id, leave.id, { reason: reason.trim(), version: employee.version }))
      }}>
      <Field label="Motivo do cancelamento" required hint="Ex.: lançado em duplicidade. Não informe CID nem diagnóstico.">
        {(p) => <input {...p} value={reason} maxLength={LEAVE_REASON_MAX} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
      </Field>
    </ConfirmDialog>
  )
}