import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { AlertTriangle, Loader2 } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import {
  calculatePayrollRun, getGetPayrollCalculationStatusQueryKey, recalculatePayrollRun, rectifyPayrollRun, useGetPayrollCalculationStatus, useGetPayrollDifferenceSensitive,
  useListPayrollCalculations, useListPayrollCalculationTotalsSensitive,
} from '@/api/generated/bemaqui'
import type { PayrollCalculationStatus, PayrollRunSummary } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PAYROLL_DICAS } from './payroll'

const button = 'inline-flex min-h-11 items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60'
const primary = 'inline-flex min-h-11 items-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'

/** Depois de 30 s calculando, o aviso de demora (Telas e estados da FLH-02a). */
const SLOW_MS = 30_000

/**
 * Folha calculada (FLH-02a CA 1, 14 e 15): "Calcular" e "Recalcular", o cálculo em segundo plano, os chips "Simulação" e "Calculada com
 * tabelas a confirmar", a faixa da conferência paralela com "Comparar com a lançada", o aviso "Cadastro ou tabela mudou depois do
 * cálculo" com "Recalcular" (rascunho) ou "Ver diferença" e "Gerar retificação" (fechada), os erros por funcionário e o próximo passo.
 */
export function CalculationBar({ run, onChanged }: { run: PayrollRunSummary; onChanged: () => Promise<void> }) {
  const client = useQueryClient()
  const canSensitive = useCan('hr.view_sensitive')
  const canCreate = useCan('hr.create') && canSensitive
  const canEdit = useCan('hr.edit') && canSensitive
  const query = useGetPayrollCalculationStatus(run.id, { query: { refetchInterval: (q) => (q.state.data?.calculating ? 3000 : false) } })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [steps, setSteps] = useState(false)
  const [diff, setDiff] = useState(false)
  const status = query.data
  // Ao terminar o cálculo em segundo plano, a folha e os holerites recarregam.
  const calculating = status?.calculating ?? false
  const before = useRef(calculating)
  useEffect(() => {
    if (before.current && !calculating) void onChanged()
    before.current = calculating
  }, [calculating, onChanged])

  if (query.isPending) return <Skeleton className="mb-4 h-16" />
  if (query.isError || !status) return <ErrorState className="mb-4" title="Não foi possível carregar a situação do cálculo." onRetry={() => query.refetch()} />

  const draft = run.status === 'Draft'
  const calculate = async () => {
    setBusy(true)
    setError(null)
    try {
      const result = status.calculated ? await recalculatePayrollRun(run.id) : await calculatePayrollRun(run.id)
      notifySuccess(result.message)
      await client.invalidateQueries({ queryKey: getGetPayrollCalculationStatusQueryKey(run.id) })
      await onChanged()
      if (!result.queued) setSteps(true)
    } catch (e) {
      setError(e instanceof ApiError ? e : new ApiError(500, { detail: 'Não foi possível calcular a folha.' }))
    } finally {
      setBusy(false)
    }
  }

  const allowed = status.calculated ? canEdit : canCreate
  const verb = status.calculated ? 'Recalcular' : 'Calcular'
  const link = error?.data.link
  return (
    <div className="mb-4 space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        {status.isSimulation ? <span className="inline-flex items-center"><StatusChip tone="info">Simulação</StatusChip><Dica nome="Simulação" texto={PAYROLL_DICAS.simulacao} /></span> : null}
        {status.unconfirmedTables.length > 0 ? (
          <Link to="/app/rh/tabelas-folha" className="inline-flex"><StatusChip tone="warn">Calculada com tabelas a confirmar</StatusChip></Link>
        ) : null}
        {status.calculatedAt ? <span className="text-sm text-muted-foreground">Calculada em {formatDateTime(status.calculatedAt)} · motor {status.calculatorVersion}</span> : <span className="text-sm text-muted-foreground">Ainda não calculada.</span>}
        {draft && allowed && !status.calculating ? (
          <button type="button" disabled={busy} onClick={() => void calculate()} className={status.calculated && status.staleCount === 0 ? button : primary}>
            {busy ? <Loader2 aria-hidden="true" className="size-4 animate-spin" /> : null}{verb}
          </button>
        ) : null}
      </div>
      {status.calculating ? <Calculating since={status.calculatingSince ?? null} /> : null}
      {error ? (
        <div role="alert" className="flex flex-wrap items-center gap-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <AlertTriangle aria-hidden="true" className="size-4" />
          {error.message}
          {typeof link === 'string' ? <a href={link} className="font-semibold text-primary">Resolver</a> : null}
        </div>
      ) : null}
      {status.isSimulation ? (
        <p role="note" className="rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
          Simulação: em conferência paralela com a folha da contadora. A folha lançada continua a oficial; esta não fecha nem vai para o funcionário.
          {status.nextStep ? <> <a href={status.nextStep.link} className="font-semibold text-primary">{status.nextStep.label}</a></> : null}
        </p>
      ) : null}
      {run.rectifiesRunId ? (
        <p role="note" className="inline-flex flex-wrap items-center rounded-md border border-border bg-muted px-3 py-2 text-sm">
          Retificação: só as diferenças da folha fechada.<Dica nome="Retificação" texto={PAYROLL_DICAS.retificacao} />
          <Link to="/app/rh/folha/$id" params={{ id: run.rectifiesRunId }} className="ml-2 font-semibold text-primary">Abrir a folha fechada</Link>
        </p>
      ) : null}
      {status.staleCount > 0 ? <StaleBand run={run} status={status} canEdit={canEdit} recalculating={busy} onRecalculate={() => void calculate()} onDifference={() => setDiff(true)} onChanged={onChanged} /> : null}
      {status.rectifications.length > 0 ? (
        <p className="text-sm text-muted-foreground">
          {status.rectifications.length === 1 ? 'Retificação gerada: ' : 'Retificações geradas: '}
          {status.rectifications.map((id, i) => <Link key={id} to="/app/rh/folha/$id" params={{ id }} className="mr-2 font-semibold text-primary">Retificação {i + 1}</Link>)}
        </p>
      ) : null}
      <Issues status={status} />
      {steps ? (
        <NextSteps onDismiss={() => setSteps(false)} steps={status.nextStep ? [{ label: status.nextStep.label, onSelect: () => { window.location.assign(status.nextStep!.link) } }] : []} />
      ) : null}
      {diff ? <DifferencePanel run={run} onClose={() => setDiff(false)} /> : null}
    </div>
  )
}

function Calculating({ since }: { since: string | null }) {
  const [slow, setSlow] = useState(false)
  useEffect(() => {
    const start = since ? new Date(since).getTime() : Date.now()
    const wait = Math.max(0, SLOW_MS - (Date.now() - start))
    const timer = window.setTimeout(() => setSlow(true), wait)
    return () => window.clearTimeout(timer)
  }, [since])
  return (
    <p role="status" aria-live="polite" className="flex items-center gap-2 rounded-md border border-border bg-muted px-3 py-2 text-sm">
      <Loader2 aria-hidden="true" className="size-4 animate-spin" />
      {slow ? 'O cálculo está demorando mais que o normal. Você pode sair desta tela: o aviso chega no sino quando terminar.' : 'Calculando os holerites…'}
    </p>
  )
}

function StaleBand({ run, status, canEdit, recalculating, onRecalculate, onDifference, onChanged }: {
  run: PayrollRunSummary; status: PayrollCalculationStatus; canEdit: boolean; recalculating: boolean; onRecalculate: () => void; onDifference: () => void; onChanged: () => Promise<void>
}) {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const draft = run.status === 'Draft'
  return (
    <div role="status" className="space-y-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
      <p className="font-semibold">{status.staleMessage}</p>
      {status.staleReasons.length > 0 ? <p>{status.staleReasons.join(' · ')}</p> : null}
      <div className="flex flex-wrap gap-2">
        {draft ? (
          canEdit ? <button type="button" disabled={recalculating} onClick={onRecalculate} className={primary}>Recalcular</button> : null
        ) : (
          <>
            <button type="button" onClick={onDifference} className={button + ' bg-card text-foreground'}>Ver diferença</button>
            {canEdit && !run.rectifiesRunId ? (
              <span className="inline-flex items-center">
                <ConfirmDialog verb="Gerar retificação" done="Retificação gerada" title="Gerar a retificação desta folha?"
                  description="Nasce uma folha complementar em rascunho, ligada a esta, só com as diferenças por funcionário e rubrica. Esta folha não muda. A retificação segue o fechamento normal (antes da virada, a conferência paralela)."
                  onConfirm={async () => {
                    const created = await rectifyPayrollRun(run.id, idempotency.options)
                    idempotency.renew()
                    await onChanged()
                    await navigate({ to: '/app/rh/folha/$id', params: { id: created.id } })
                  }}
                  trigger={<button type="button" className={primary}>Gerar retificação</button>} />
                <Dica nome="Retificação" texto={PAYROLL_DICAS.retificacao} />
              </span>
            ) : null}
          </>
        )}
      </div>
    </div>
  )
}

/** Erros e avisos por funcionário (Telas e estados): o que trava fica em vermelho; os outros funcionários seguem calculados. */
function Issues({ status }: { status: PayrollCalculationStatus }) {
  if (status.issues.length === 0) return null
  const blocking = status.issues.filter((i) => i.blocking)
  const list = (
    <ul className="space-y-1">
      {status.issues.map((i) => (
        <li key={`${i.employeeId}-${i.message}`} className="flex flex-wrap items-center gap-2">
          <StatusChip tone={i.blocking ? 'crit' : 'warn'}>{i.blocking ? 'Sem cálculo' : 'Aviso'}</StatusChip>
          <span><b>{i.employeeName}</b>: {i.message}</span>
          {i.blocking ? <Link to="/app/rh/funcionarios/$id" params={{ id: i.employeeId }} className="font-semibold text-primary">Abrir o cadastro</Link> : null}
        </li>
      ))}
    </ul>
  )
  return status.issues.length <= 5 ? <section aria-label="Avisos do cálculo" className="text-sm">{list}</section> : (
    <details className="text-sm">
      <summary className="cursor-pointer font-semibold">{status.issues.length} avisos do cálculo{blocking.length > 0 ? ` (${blocking.length} sem cálculo)` : ''}</summary>
      <div className="mt-2">{list}</div>
    </details>
  )
}

/** "Ver diferença" (CA 14): o que mudaria por funcionário e rubrica, sem recalcular a folha fechada. */
function DifferencePanel({ run, onClose }: { run: PayrollRunSummary; onClose: () => void }) {
  const query = useGetPayrollDifferenceSensitive(run.id)
  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title="Ver diferença" description="O cálculo de hoje contra a folha fechada. Nada é gravado.">
      {query.isPending ? <Skeleton className="h-48" /> : query.isError || !query.data ? <ErrorState title="Não foi possível calcular a diferença." onRetry={() => query.refetch()} /> : query.data.employees.length === 0 ? (
        <EmptyState title="Nada mudou." description="A folha fechada bate com o cálculo de hoje." />
      ) : (
        <div className="space-y-3">
          <p className="text-sm">Líquido fechado <Money value={query.data.netBefore} className="font-semibold" /> · pelo cálculo de hoje <Money value={query.data.netAfter} className="font-semibold" /></p>
          {query.data.reasons.length > 0 ? <p className="text-sm text-muted-foreground">{query.data.reasons.join(' · ')}</p> : null}
          <ul aria-label="Diferenças por funcionário" className="space-y-2">
            {query.data.employees.map((e) => (
              <li key={e.employeeId} className="space-y-1 rounded-md border border-border p-3 text-sm">
                <p className="font-semibold">{e.employeeName}{e.registration ? <span className="font-normal text-muted-foreground"> · {e.registration}</span> : null}</p>
                <p className="tabular-nums text-muted-foreground">Líquido <Money value={e.netBefore} /> → <Money value={e.netAfter} className="font-semibold text-foreground" /></p>
                <ul className="space-y-0.5">
                  {e.lines.map((l) => (
                    <li key={l.payrollItemId} className="flex flex-wrap justify-between gap-2 tabular-nums">
                      <span>{l.code} · {l.name}</span>
                      <span><Money value={l.before} /> → <Money value={l.after} className="font-semibold" /></span>
                    </li>
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        </div>
      )}
    </SidePanel>
  )
}

/** Histórico do cálculo (CA 19): quem, quando, a versão do motor, as tabelas e os parâmetros; os totais só com hr.view_sensitive. */
export function CalculationHistory({ run }: { run: PayrollRunSummary }) {
  const canSensitive = useCan('hr.view_sensitive')
  const query = useListPayrollCalculations(run.id)
  const totals = useListPayrollCalculationTotalsSensitive(run.id, { query: { enabled: canSensitive } })
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico do cálculo." onRetry={() => query.refetch()} />
  if (query.data.length === 0) return <EmptyState title="A folha ainda não foi calculada." description="Clique em &quot;Calcular&quot; no topo da folha." />
  const byId = new Map((totals.data ?? []).map((t) => [t.id, t]))
  return (
    <section aria-label="Histórico do cálculo" className="space-y-2">
      <h3 className="text-sm font-semibold">Histórico do cálculo</h3>
      <ul className="divide-y divide-border rounded-md border border-border text-sm">
        {query.data.map((e) => {
          const t = byId.get(e.id)
          return (
            <li key={e.id} className="space-y-1 px-3 py-2">
              <p><b>{e.actionLabel}</b> · {formatDateTime(e.at)}{e.byName ? ` · ${e.byName}` : ''} · motor {e.calculatorVersion} · {e.employees === 1 ? '1 funcionário' : `${e.employees} funcionários`}</p>
              <p className="text-muted-foreground">Tabelas: {e.tables.map((x) => `${x.label}${x.status === 'Active' ? '' : ' (a confirmar)'}`).join(', ')}</p>
              {e.parameters.length > 0 ? <p className="text-muted-foreground">Parâmetros: {e.parameters.join(', ')}</p> : null}
              {t ? (
                <p className="tabular-nums">
                  {t.netBefore !== null && t.netBefore !== undefined ? <>Líquido antes <Money value={t.netBefore} /> · </> : null}Líquido depois <Money value={t.netAfter} className="font-semibold" />
                </p>
              ) : null}
            </li>
          )
        })}
      </ul>
    </section>
  )
}
