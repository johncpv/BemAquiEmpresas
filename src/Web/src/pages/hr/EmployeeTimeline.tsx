import { useState } from 'react'
import { useGetEmployeeTimeline, useListEmployeeLeavesSensitive } from '@/api/generated/bemaqui'
import type { EmployeeDetail, LeaveChangeDetail, LeaveDetail } from '@/api/generated/model'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime, formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { EMPLOYEE_DICAS, LEAVE_STATE_LABELS, LEAVE_STATE_TONES, LEAVE_TYPE_LABELS, type LeaveType, leaveActions } from './employee'
import { CancelLeaveDialog, EndLeaveDialog, LeaveDialog } from './EmployeeDialogs'
import { EmployeeProtected } from './EmployeeProtected'

const money = (v: string | null) => (v === null ? '—' : formatMoney(Number(v)))
const button = 'rounded-md border border-border px-3 py-1.5 text-sm font-semibold'
const period = (start: string, end: string) => `${formatDate(start)} a ${formatDate(end)}`

/**
 * Linha do tempo (CA 16): trocas de cargo com hr.view; reajustes só com hr.view_sensitive (o servidor filtra). Afastamentos
 * (CAD-04c1) só pela rota /leaves/sensitive: sem a permissão, nada é pedido e aparece "Dados protegidos". Editar, encerrar e
 * cancelar (CAD-04c1b) com hr.edit; Doença e Acidente de trabalho só aparecem com hr.view_health.
 */
export function EmployeeTimeline({ employee, onChanged, onReload }: { employee: EmployeeDetail; onChanged: (e: EmployeeDetail) => void; onReload: () => Promise<unknown> }) {
  const canSensitive = useCan('hr.view_sensitive')
  return (
    <div className="max-w-3xl space-y-6">
      <section aria-labelledby="afastamentos" className="space-y-3">
        <h2 id="afastamentos" className="text-base font-semibold">Afastamentos</h2>
        {canSensitive ? <EmployeeLeaves employee={employee} onChanged={onChanged} onReload={onReload} /> : <EmployeeProtected />}
      </section>
      <section aria-labelledby="carreira" className="space-y-3">
        <h2 id="carreira" className="text-base font-semibold">Cargo e salário</h2>
        <CareerTimeline id={employee.id} canSensitive={canSensitive} />
      </section>
    </div>
  )
}

type LeaveAction = { kind: 'edit' | 'end' | 'cancel'; leave: LeaveDetail }

function EmployeeLeaves({ employee, onChanged, onReload }: { employee: EmployeeDetail; onChanged: (e: EmployeeDetail) => void; onReload: () => Promise<unknown> }) {
  const query = useListEmployeeLeavesSensitive(employee.id)
  const canEdit = useCan('hr.edit')
  const canHealth = useCan('hr.view_health')
  const [action, setAction] = useState<LeaveAction | null>(null)
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os afastamentos." onRetry={() => query.refetch()} />
  if (query.data.length === 0) {
    return <EmptyState title="Nenhum afastamento registrado." description='Use "Registrar afastamento" no topo: a situação fica Afastado entre o início e o fim previsto e volta a Ativo sozinha.' />
  }

  const editable = canEdit && employee.status !== 'Terminated'
  const close = (o: boolean) => { if (!o) setAction(null) }
  const reload = async () => { await Promise.all([onReload(), query.refetch()]) }
  return (
    <>
      <ol className="space-y-3">
        {query.data.map((l) => {
          const allowed = leaveActions(l, canHealth)
          return (
            <li key={l.id} className="rounded-md border border-border bg-card p-3 text-sm">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <span className="flex items-center gap-1.5">
                  <b>{l.healthProtected || !l.type ? 'Afastado' : `Afastamento: ${LEAVE_TYPE_LABELS[l.type]}`}</b>
                  {l.healthProtected ? <Dica nome="Dado de saúde" texto={EMPLOYEE_DICAS.saude} /> : null}
                </span>
                <StatusChip tone={LEAVE_STATE_TONES[l.state]}>{LEAVE_STATE_LABELS[l.state]}</StatusChip>
              </div>
              <p className={'mt-1 tabular-nums' + (l.state === 'Cancelled' ? ' line-through' : '')}>
                {period(l.startDate, l.expectedEndDate)}{l.state === 'Cancelled' ? '' : ` · volta prevista em ${formatDate(l.expectedReturnDate)}`}
              </p>
              <p className="mt-1 text-muted-foreground">{l.reason ? l.reason + ' · ' : ''}registrado em {formatDateTime(l.createdAt)}</p>
              {l.changes.length > 0 ? (
                <ul aria-label="Mudanças do afastamento" className="mt-2 space-y-1 border-l-2 border-border pl-3 text-muted-foreground">
                  {l.changes.map((c) => <li key={c.id} className="tabular-nums">{changeText(c)}</li>)}
                </ul>
              ) : null}
              {editable && (allowed.edit || allowed.end || allowed.cancel) ? (
                <div className="mt-3 flex flex-wrap gap-2">
                  {allowed.edit ? <button type="button" className={button} onClick={() => setAction({ kind: 'edit', leave: l })}>Editar</button> : null}
                  {allowed.end ? <button type="button" className={button} onClick={() => setAction({ kind: 'end', leave: l })}>Encerrar afastamento</button> : null}
                  {allowed.cancel ? <button type="button" className={button + ' text-crit-ink'} onClick={() => setAction({ kind: 'cancel', leave: l })}>Cancelar afastamento</button> : null}
                </div>
              ) : null}
            </li>
          )
        })}
      </ol>
      {action?.kind === 'edit' ? <LeaveDialog key={action.leave.id} employee={employee} leave={action.leave} canHealth={canHealth} open onOpenChange={close} onReload={reload} onDone={onChanged} /> : null}
      {action?.kind === 'end' ? <EndLeaveDialog key={action.leave.id} employee={employee} leave={action.leave} open onOpenChange={close} onReload={reload} onDone={onChanged} /> : null}
      {action?.kind === 'cancel' ? <CancelLeaveDialog key={action.leave.id} employee={employee} leave={action.leave} open onOpenChange={close} onReload={reload} onDone={onChanged} /> : null}
    </>
  )
}

/** Rastro (CAD-04c1b CA 7): o antes e o depois de cada mudança, com quando foi feita. */
function changeText(c: LeaveChangeDetail) {
  const when = formatDateTime(c.createdAt)
  if (c.kind === 'Cancelled') return `Cancelado em ${when}${c.reason ? `: ${c.reason}` : ''}`
  if (c.kind === 'EndedEarly') return `Encerrado antes em ${when}: fim previsto ${formatDate(c.oldExpectedEndDate)} → ${formatDate(c.newExpectedEndDate)}`
  const parts: string[] = []
  if (c.oldStartDate !== c.newStartDate || c.oldExpectedEndDate !== c.newExpectedEndDate) parts.push(`${period(c.oldStartDate, c.oldExpectedEndDate)} → ${period(c.newStartDate, c.newExpectedEndDate)}`)
  if (c.oldType && c.newType && c.oldType !== c.newType) parts.push(`${LEAVE_TYPE_LABELS[c.oldType as LeaveType]} → ${LEAVE_TYPE_LABELS[c.newType as LeaveType]}`)
  return `Editado em ${when}${parts.length > 0 ? `: ${parts.join('; ')}` : ''}`
}

function CareerTimeline({ id, canSensitive }: { id: string; canSensitive: boolean }) {
  const query = useGetEmployeeTimeline(id)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  if (query.data.length === 0) {
    return <EmptyState title="Nenhuma troca de cargo ou reajuste ainda." description={canSensitive ? 'Use "Trocar cargo" ou "Reajustar salário": cada mudança entra aqui, com a vigência e o motivo.' : 'Use "Trocar cargo": cada mudança entra aqui, com a vigência e o motivo. Os reajustes só aparecem com a permissão Ver dados sensíveis.'} />
  }

  return (
    <ol className="space-y-3">
      {query.data.map((e) => (
        <li key={e.id} className="rounded-md border border-border bg-card p-3 text-sm">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <b>{e.kind === 'Salary' ? 'Reajuste de salário' : 'Troca de cargo'}</b>
            <span className="tabular-nums text-muted-foreground">Vigência {formatDate(e.effectiveDate)}</span>
          </div>
          <p className="mt-1 tabular-nums">
            {e.kind === 'Salary' ? `${money(e.oldValue)} → ${money(e.newValue)}` : `${e.oldValue ?? '—'} → ${e.newValue}`}
          </p>
          <p className="mt-1 text-muted-foreground">{e.reason} · registrado em {formatDateTime(e.createdAt)}</p>
        </li>
      ))}
    </ol>
  )
}
