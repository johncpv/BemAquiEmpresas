import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { FileDown, Lock } from 'lucide-react'
import { useState } from 'react'
import { getDownloadEmployeeTimesheetPdfUrl, reviewTimesheetDay, useGetEmployeeTimesheet } from '@/api/generated/bemaqui'
import type { TimesheetDayView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { notifyError } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { periodLabel } from '../payroll/payroll'
import { InternalControlBanner } from './TimeClockPanel'
import { todayInSaoPaulo } from './time-clock'
import { TimesheetDays, TimesheetTotals } from './TimesheetDays'
import { monthOf, shiftMonth, shortDay, TIMESHEET_DICAS } from './timesheet'

const text = (v: unknown) => (typeof v === 'string' && /^\d{4}-\d{2}$/.test(v) ? v : undefined)
const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1)

/**
 * Espelho do funcionário (/app/rh/ponto/espelho/$employeeId?mes=2026-10, RH-01c CA 3, 6, 7, 9, 11 e 18): os dias em tabela (cartões no
 * celular), os totais e o banco no topo, a faixa de controle interno e, fechado, quem fechou. No dia inconsistente, os atalhos "Lançar
 * marcação", "Ver pedidos de ajuste" e "Marcar como revisado" (hr.edit, com comentário).
 */
export function TimesheetEmployeePage() {
  const { employeeId } = useParams({ strict: false }) as { employeeId: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const period = text(search.mes) ?? monthOf(todayInSaoPaulo())
  const canEdit = useCan('hr.edit')
  const client = useQueryClient()
  const query = useGetEmployeeTimesheet(employeeId, period, { query: { retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2 } })
  const [reviewing, setReviewing] = useState<TimesheetDayView | null>(null)
  const [comment, setComment] = useState('')
  const [downloading, setDownloading] = useState(false)
  const go = (mes: string) => void navigate({ to: '.', replace: true, search: { mes } as never })

  const download = async () => {
    setDownloading(true)
    try {
      await apiDownload(getDownloadEmployeeTimesheetPdfUrl(employeeId, period), `espelho-${period}.pdf`)
    } catch (e) {
      notifyError(e instanceof Error ? e.message : 'Não foi possível baixar o espelho. Tente de novo.')
    } finally {
      setDownloading(false)
    }
  }

  let content
  if (query.isPending) content = <div className="space-y-2"><Skeleton className="h-20" /><Skeleton className="h-64" /></div>
  else if (query.isError) {
    const missing = query.error instanceof ApiError && query.error.status === 404
    content = <ErrorState title={missing ? 'Funcionário não encontrado.' : 'Não foi possível carregar o espelho.'} message={missing ? 'Volte para o espelho geral e escolha o funcionário.' : undefined} onRetry={missing ? undefined : () => query.refetch()} />
  } else {
    const sheet = query.data
    content = (
      <div className="space-y-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="font-display text-xl font-bold">{sheet.employeeName}</h2>
            <p className="text-sm text-muted-foreground">
              {sheet.registration ? `Matrícula ${sheet.registration} · ` : ''}{sheet.positionName} · {sheet.scheduleText}
            </p>
            <p className="inline-flex items-center text-sm text-muted-foreground tabular-nums">
              Período de apuração: {formatDate(sheet.start)} a {formatDate(sheet.end)}
              <Dica nome="Período de apuração" texto={TIMESHEET_DICAS.periodo} />
            </p>
          </div>
          {sheet.status === 'Closed' ? (
            <button type="button" onClick={() => void download()} disabled={downloading}
              className="inline-flex min-h-11 items-center gap-2 rounded-md border border-border px-3 text-sm font-semibold">
              <FileDown aria-hidden="true" className="size-4" />{downloading ? 'Baixando…' : 'Baixar espelho em PDF'}
            </button>
          ) : null}
        </div>
        {sheet.closedBanner ? (
          <p role="status" className="flex items-center gap-2 rounded-md border border-border bg-muted px-3 py-2 text-sm font-semibold">
            <Lock aria-hidden="true" className="size-4" />{sheet.closedBanner}{sheet.employeeAckAt ? ' · Confirmado pelo funcionário' : ' · Aguardando o funcionário'}
          </p>
        ) : sheet.reopenReason ? (
          <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Período reaberto: {sheet.reopenReason}</p>
        ) : null}
        {sheet.pendingAdjustments > 0 ? (
          <p role="status" className="text-sm text-warn-ink">
            {sheet.pendingAdjustments === 1 ? '1 pedido de ajuste aguarda aprovação. ' : `${sheet.pendingAdjustments} pedidos de ajuste aguardam aprovação. `}
            <Link to="/app/rh/ponto/ajustes" search={{ funcionario: employeeId } as never} className="font-semibold text-primary">Ver pedidos de ajuste</Link>
          </p>
        ) : null}
        <TimesheetTotals totals={sheet.totals} />
        {sheet.hasEntries || sheet.days.some((d) => d.expectedMinutes > 0) ? (
          <TimesheetDays sheet={sheet} actions={{ employeeId, canReview: canEdit && sheet.status === 'Open', onReview: (d) => { setComment(''); setReviewing(d) } }} />
        ) : (
          <EmptyState title="Nenhuma marcação neste período." description="As marcações chegam pelo Meu ponto ou pelo lançamento manual."
            action={<Link to="/app/rh/funcionarios/$id" params={{ id: employeeId }} search={{ aba: 'ponto' } as never} className="text-sm font-semibold text-primary">Lançar marcação</Link>} />
        )}
      </div>
    )
  }

  return (
    <>
      <PageHeading title="RH: Espelho de ponto">
        <p className="mt-1 text-muted-foreground">Os dias do funcionário no período, com o esperado pela jornada, o trabalhado, as extras, as faltas e o banco de horas.</p>
      </PageHeading>
      <HrTabs />
      <div className="max-w-5xl space-y-4">
        <InternalControlBanner />
        <div className="flex flex-wrap items-center gap-2">
          <Link to="/app/rh/ponto/espelho" search={{ mes: period } as never} className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar ao espelho geral</Link>
          <button type="button" onClick={() => go(shiftMonth(period, -1))} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Mês anterior</button>
          <span className="font-semibold">{capitalize(periodLabel(period))}</span>
          <button type="button" onClick={() => go(shiftMonth(period, 1))} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Mês seguinte</button>
        </div>
        {content}
      </div>
      <ConfirmDialog verb="Marcar como revisado" done="Dia revisado" title={reviewing ? `Marcar ${shortDay(reviewing.day)} como revisado?` : 'Marcar como revisado?'}
        open={!!reviewing} onOpenChange={(o) => { if (!o) setReviewing(null) }}
        description={reviewing ? `${reviewing.issues.join(' · ')}. O dia conta só os pares fechados e deixa de bloquear o fechamento.` : undefined}
        onConfirm={async () => {
          if (!reviewing) return
          if (comment.trim().length < 5) throw new DialogMessage('Escreva o comentário da revisão, com pelo menos 5 caracteres.')
          await reviewTimesheetDay(employeeId, { day: reviewing.day, comment: comment.trim() })
          await client.invalidateQueries()
        }}>
        <Field label="Comentário" required hint="O que foi conferido. Não informe CID nem diagnóstico.">
          {(p) => <textarea {...p} rows={3} maxLength={200} value={comment} onChange={(e) => setComment(e.target.value)} className={inputClass} />}
        </Field>
      </ConfirmDialog>
    </>
  )
}
