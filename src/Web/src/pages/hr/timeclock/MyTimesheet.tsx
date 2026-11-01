import { useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, FileDown } from 'lucide-react'
import { useState } from 'react'
import { acknowledgeMyTimesheet, getDownloadMyTimesheetPdfUrl, useGetMyTimesheet, useListMyTimesheets } from '@/api/generated/bemaqui'
import { ApiError, apiDownload } from '@/api/http'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifyError } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatInstantDate } from '@/lib/format'
import { periodLabel } from '../payroll/payroll'
import { NOT_LINKED } from './time-clock'
import { TimesheetDays, TimesheetTotals } from './TimesheetDays'

const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1)
const noRetry403 = { retry: (count: number, error: unknown) => !(error instanceof ApiError && error.status < 500) && count < 2 }

/** O espelho do período escolhido, com "Li e confirmo" no fechado (CA 15) e o PDF. */
function MyPeriod({ period }: { period: string }) {
  const client = useQueryClient()
  const query = useGetMyTimesheet(period, { query: noRetry403 })
  const [acking, setAcking] = useState(false)
  const [downloading, setDownloading] = useState(false)
  if (query.isPending) return <div className="space-y-2"><Skeleton className="h-16" /><Skeleton className="h-40" /></div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o seu espelho." onRetry={() => query.refetch()} />
  const sheet = query.data
  const download = async () => {
    setDownloading(true)
    try {
      await apiDownload(getDownloadMyTimesheetPdfUrl(period), `espelho-${period}.pdf`)
    } catch (e) {
      notifyError(e instanceof Error ? e.message : 'Não foi possível baixar o espelho. Tente de novo.')
    } finally {
      setDownloading(false)
    }
  }

  return (
    <div className="space-y-3">
      {sheet.status === 'Closed' ? (
        <div className="flex flex-col gap-2 rounded-md border border-border bg-card p-3">
          <p className="text-sm font-semibold">{sheet.closedBanner}</p>
          {sheet.employeeAckAt ? (
            <p className="inline-flex items-center gap-1 text-sm text-ok-ink"><CheckCircle2 aria-hidden="true" className="size-4" />Confirmado em {formatInstantDate(sheet.employeeAckAt)}</p>
          ) : (
            <button type="button" onClick={() => setAcking(true)} className="min-h-[56px] rounded-md bg-primary px-4 text-base font-semibold text-primary-foreground">Li e confirmo</button>
          )}
          <button type="button" onClick={() => void download()} disabled={downloading} className="inline-flex min-h-[56px] items-center justify-center gap-2 rounded-md border border-border px-4 text-base font-semibold">
            <FileDown aria-hidden="true" className="size-5" />{downloading ? 'Baixando…' : 'Baixar espelho em PDF'}
          </button>
        </div>
      ) : <p className="text-sm text-muted-foreground">Período em aberto: os totais mudam até o RH fechar o período.</p>}
      <TimesheetTotals totals={sheet.totals} />
      {sheet.hasEntries || sheet.days.some((d) => d.expectedMinutes > 0)
        ? <TimesheetDays sheet={sheet} />
        : <EmptyState title="Nenhuma marcação neste período." description="As marcações chegam pelo Meu ponto ou pelo lançamento manual do RH." />}
      <ConfirmDialog verb="Li e confirmo" done="Confirmado" title={`Confirmar o espelho de ${periodLabel(period)}?`} open={acking} onOpenChange={setAcking}
        description="Você confirma que leu o espelho do período. É uma ciência: não bloqueia nada e não é assinatura legal. Fica registrada a data, a hora e o resumo dos totais."
        onConfirm={async () => { await acknowledgeMyTimesheet(period); await client.invalidateQueries() }} />
    </div>
  )
}

/** "Meu espelho" (RH-01c CA 8 e 15, ADR 0035): o próprio espelho do período em aberto e dos fechados; só carrega quando a pessoa abre. */
export function MyTimesheet({ initiallyOpen = false }: { initiallyOpen?: boolean }) {
  const [open, setOpen] = useState(initiallyOpen)
  const periods = useListMyTimesheets({ query: { enabled: open, ...noRetry403 } })
  const [chosen, setChosen] = useState<string | null>(null)
  const period = chosen ?? periods.data?.[0]?.period.slice(0, 7) ?? null

  let content = null
  if (open) {
    if (periods.isPending) content = <Skeleton className="h-24" />
    else if (periods.isError) {
      const notLinked = periods.error instanceof ApiError && periods.error.code === 'employee_not_active'
      content = notLinked ? <EmptyState title="Espelho indisponível" description={NOT_LINKED} /> : <ErrorState title="Não foi possível carregar o seu espelho." onRetry={() => periods.refetch()} />
    } else if (!period) content = <EmptyState title="Nenhum período ainda." description="O espelho aparece quando você começa a marcar o ponto." />
    else {
      content = (
        <div className="space-y-3">
          <label className="flex flex-col text-xs font-semibold">Período
            <select value={period} onChange={(e) => setChosen(e.target.value)} className="min-h-11 rounded-md border border-input bg-background px-2 text-sm">
              {periods.data.map((p) => (
                <option key={p.period} value={p.period.slice(0, 7)}>
                  {capitalize(periodLabel(p.period.slice(0, 7)))}{p.status === 'Closed' ? (p.employeeAckAt ? ' · confirmado' : ' · fechado, aguardando você') : ' · em aberto'}
                </option>
              ))}
            </select>
          </label>
          <MyPeriod key={period} period={period} />
        </div>
      )
    }
  }

  return (
    <section aria-labelledby="meu-espelho" className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <h2 id="meu-espelho" className="text-lg font-bold">Meu espelho</h2>
        <button type="button" onClick={() => setOpen((v) => !v)} aria-expanded={open} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">
          {open ? 'Ocultar' : 'Ver o meu espelho'}
        </button>
      </div>
      {content}
    </section>
  )
}
