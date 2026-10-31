import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { FileDown, Lock } from 'lucide-react'
import { useState } from 'react'
import {
  closeTimesheetPeriod, generateTimesheetBatch, getDownloadTimesheetBatchUrl, getTimesheetCloseCheck, reopenTimesheetPeriod, useGetTimesheetOverview,
} from '@/api/generated/bemaqui'
import type { TimesheetCloseCheck } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog, type Precondition } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { periodLabel } from '../payroll/payroll'
import { InternalControlBanner } from './TimeClockPanel'
import { TimeClockTabs } from './TimeClockTabs'
import { minutes, shiftMonth, shortDay, signed, situationTone, TIMESHEET_DICAS } from './timesheet'

const text = (v: unknown) => (typeof v === 'string' && /^\d{4}-\d{2}$/.test(v) ? v : undefined)
const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1)

/** Pré-condições do fechamento (CA 11) por funcionário, cada uma com o atalho direto para a pendência. */
function preconditionsOf(check: TimesheetCloseCheck | null, navigate: ReturnType<typeof useNavigate>, period: string): Precondition[] {
  if (!check) return [{ ok: false, text: 'Conferindo as pendências do período…' }]
  const list: Precondition[] = check.blockers.map((b) => ({ ok: false, text: b }))
  for (const e of check.employees) {
    if (e.pendingAdjustments > 0) {
      list.push({ ok: false, text: `${e.name}: ${e.pendingAdjustments === 1 ? '1 pedido de ajuste pendente' : `${e.pendingAdjustments} pedidos de ajuste pendentes`}`,
        fix: { label: 'Ver pedidos de ajuste', onSelect: () => void navigate({ to: '/app/rh/ponto/ajustes', search: { funcionario: e.employeeId } as never }) } })
    }

    if (e.inconsistentDays.length > 0) {
      list.push({ ok: false, text: `${e.name}: ${e.inconsistentDays.length === 1 ? '1 dia inconsistente' : `${e.inconsistentDays.length} dias inconsistentes`} (${e.inconsistentDays.slice(0, 4).map(shortDay).join(', ')}${e.inconsistentDays.length > 4 ? '…' : ''})`,
        fix: { label: 'Abrir o espelho', onSelect: () => void navigate({ to: '/app/rh/ponto/espelho/$employeeId', params: { employeeId: e.employeeId }, search: { mes: period } as never }) } })
    }
  }

  if (list.length === 0) list.push({ ok: true, text: `Sem pendências: ${check.employeeCount === 1 ? '1 funcionário' : `${check.employeeCount} funcionários`} no período.` })
  return list
}

/**
 * RH → Ponto → Espelho (/app/rh/ponto/espelho?mes=2026-10, RH-01c CA 3, 11 a 15 e 18): a visão geral do período (um funcionário por linha
 * com o trabalhado, as extras, as faltas, o banco, a confirmação do funcionário e o chip de situação), "Fechar o período {mês}" (hr.edit)
 * com as pré-condições, "Reabrir período" (hr.approve) com motivo e "Baixar espelhos em PDF".
 */
export function TimesheetOverviewPage() {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const canEdit = useCan('hr.edit')
  const canApprove = useCan('hr.approve')
  const query = useGetTimesheetOverview({ period: text(search.mes) }, { query: { retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2 } })
  const period = text(search.mes) ?? query.data?.period.slice(0, 7) ?? ''
  const [closing, setClosing] = useState(false)
  const [check, setCheck] = useState<TimesheetCloseCheck | null>(null)
  // O diálogo é controlado (aberto pelo botão), então o onOpen do ConfirmDialog não dispara: a conferência sai no clique.
  const loadCheck = () => {
    setCheck(null)
    void getTimesheetCloseCheck(period).then(setCheck).catch(() => setCheck({ period, start: '', end: '', canClose: false, blockers: ['Não foi possível conferir as pendências. Feche e abra de novo.'], employees: [], employeeCount: 0 }))
  }
  const [reopening, setReopening] = useState(false)
  const [reason, setReason] = useState('')
  const [steps, setSteps] = useState<Step[]>([])
  const [batching, setBatching] = useState(false)
  const go = (mes: string) => void navigate({ to: '.', replace: true, search: { mes } as never })
  const label = period ? periodLabel(period) : ''

  const downloadAll = async () => {
    setBatching(true)
    try {
      const result = await generateTimesheetBatch(period)
      if (result.queued || !result.attachmentId) notifySuccess(`${result.message} Você recebe o aviso no sino quando terminar.`)
      else await apiDownload(getDownloadTimesheetBatchUrl(period, result.attachmentId), result.fileName)
    } catch (e) {
      notifyError(e instanceof Error ? e.message : 'Não foi possível gerar os espelhos. Tente de novo.', () => void downloadAll())
    } finally {
      setBatching(false)
    }
  }

  let content
  if (query.isPending) content = <div className="space-y-2"><Skeleton className="h-12" /><Skeleton className="h-64" /></div>
  else if (query.isError) {
    const invalid = query.error instanceof ApiError && query.error.status === 422
    content = <ErrorState title={invalid && query.error instanceof ApiError ? query.error.message : 'Não foi possível carregar o espelho.'} message={invalid ? 'Escolha outro mês.' : undefined} onRetry={invalid ? undefined : () => query.refetch()} />
  } else if (query.data.rows.length === 0) {
    content = (
      <EmptyState title="Nenhuma marcação neste período." description="As marcações chegam pelo Meu ponto ou pelo lançamento manual."
        action={<Link to="/app/rh/funcionarios" className="text-sm font-semibold text-primary">Ver os funcionários</Link>} />
    )
  } else {
    const data = query.data
    content = (
      <div className="space-y-3">
        {data.closedBanner ? <p role="status" className="flex items-center gap-2 rounded-md border border-border bg-muted px-3 py-2 text-sm font-semibold"><Lock aria-hidden="true" className="size-4" />{data.closedBanner}</p> : null}
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full text-sm">
            <caption className="sr-only">Espelho de {label}</caption>
            <thead className="bg-muted text-left text-xs font-semibold text-muted-foreground">
              <tr>
                <th scope="col" className="sticky left-0 bg-muted px-3 py-2">Funcionário</th>
                <th scope="col" className="px-3 py-2 text-right">Trabalhado</th>
                <th scope="col" className="px-3 py-2 text-right">Extras</th>
                <th scope="col" className="px-3 py-2 text-right">Faltas</th>
                <th scope="col" className="px-3 py-2 text-right">Atrasos</th>
                <th scope="col" className="px-3 py-2 text-right">Banco <Dica nome="Banco de horas" texto={TIMESHEET_DICAS.banco} /></th>
                <th scope="col" className="px-3 py-2">Funcionário confirmou</th>
                <th scope="col" className="px-3 py-2">Situação</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {data.rows.map((r) => (
                <tr key={r.employeeId}>
                  <th scope="row" className="sticky left-0 bg-card px-3 py-2 text-left font-semibold">
                    <Link to="/app/rh/ponto/espelho/$employeeId" params={{ employeeId: r.employeeId }} search={{ mes: period } as never} className="text-primary">{r.name}</Link>
                    {r.registration ? <span className="block text-xs font-normal text-muted-foreground">Matrícula {r.registration}</span> : null}
                  </th>
                  <td className="px-3 py-2 text-right tabular-nums">{minutes(r.workedMinutes, '0 min')} <span className="text-xs text-muted-foreground">de {minutes(r.expectedMinutes, '0 min')}</span></td>
                  <td className="px-3 py-2 text-right tabular-nums">{minutes(r.overtimeMinutes)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{minutes(r.absenceMinutes)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{minutes(r.lateMinutes)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{r.bankBalanceMinutes === null || r.bankBalanceMinutes === undefined ? '—' : `${signed(r.bankMonthMinutes)} · ${signed(r.bankBalanceMinutes)}`}</td>
                  <td className="px-3 py-2 text-xs">{r.ackLabel}</td>
                  <td className="px-3 py-2"><StatusChip tone={situationTone(r.situationTone)}>{r.situationLabel}</StatusChip></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    )
  }

  const data = query.data
  return (
    <>
      <PageHeading title="RH: Ponto">
        <p className="mt-1 text-muted-foreground">O espelho de cada funcionário no período, para conferir, fechar e mandar à contadora e à folha.</p>
      </PageHeading>
      <HrTabs />
      <div className="max-w-5xl space-y-4">
        <TimeClockTabs />
        <InternalControlBanner />
        {steps.length > 0 ? <NextSteps steps={steps} onDismiss={() => setSteps([])} /> : null}
        <div className="flex flex-wrap items-center gap-2">
          <button type="button" onClick={() => period && go(shiftMonth(period, -1))} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Mês anterior</button>
          <span className="font-semibold">{label ? capitalize(label) : '…'}</span>
          <button type="button" onClick={() => period && go(shiftMonth(period, 1))} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Mês seguinte</button>
          {data ? (
            <span className="inline-flex items-center text-sm text-muted-foreground tabular-nums">
              {formatDate(data.start)} a {formatDate(data.end)}
              <Dica nome="Período de apuração" texto={TIMESHEET_DICAS.periodo} />
            </span>
          ) : null}
          <span className="ml-auto flex flex-wrap gap-2">
            {data?.status === 'Closed' ? (
              <button type="button" onClick={() => void downloadAll()} disabled={batching} className="inline-flex min-h-11 items-center gap-2 rounded-md border border-border px-3 text-sm font-semibold">
                <FileDown aria-hidden="true" className="size-4" />{batching ? 'Gerando…' : 'Baixar espelhos em PDF'}
              </button>
            ) : null}
            {data?.status === 'Closed' && canApprove ? (
              <button type="button" onClick={() => { setReason(''); setReopening(true) }} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Reabrir período</button>
            ) : null}
            {data?.closable && canEdit ? (
              <button type="button" onClick={() => { loadCheck(); setClosing(true) }} className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">Fechar o período {label}</button>
            ) : null}
          </span>
        </div>
        {content}
      </div>
      <ConfirmDialog verb="Fechar período" done={`Período ${label} fechado`} title={`Fechar o período ${label}?`} size="lg" open={closing} onOpenChange={setClosing}
        description="Os totais de cada funcionário ficam congelados (esperado, trabalhado, extras, faltas, atrasos, abonado e banco de horas) e nenhum ajuste entra mais no período, até alguém reabrir."
        preconditions={preconditionsOf(check, navigate, period)}
        onConfirm={async () => {
          await closeTimesheetPeriod(period)
          await client.invalidateQueries()
          setSteps([
            { label: 'Baixar espelhos em PDF', onSelect: () => void downloadAll() },
            { label: `Lançar a folha de ${label}`, onSelect: () => void navigate({ to: '/app/rh/folha' }) },
            { label: 'Gerar pacote para a contadora', onSelect: () => void navigate({ to: '/app/rh/exportar-contadora', search: { competencia: period } as never }) },
          ])
        }} />
      <ConfirmDialog verb="Reabrir período" done="Período reaberto" title={`Reabrir o período ${label}?`} open={reopening} onOpenChange={setReopening}
        description="Os espelhos voltam a aberto, junto com os períodos seguintes já fechados (o saldo do banco depende deles). As confirmações dos funcionários ficam sem efeito, o Proprietário é avisado e a folha já fechada do mês mostra o aviso."
        onConfirm={async () => {
          if (reason.trim().length < 5) throw new DialogMessage('Informe o motivo da reabertura, com pelo menos 5 caracteres.')
          await reopenTimesheetPeriod(period, { reason: reason.trim() })
          await client.invalidateQueries()
        }}>
        <Field label="Motivo" required hint="Fica no histórico e vai no aviso ao Proprietário.">
          {(p) => <textarea {...p} rows={3} maxLength={200} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
        </Field>
      </ConfirmDialog>
    </>
  )
}
