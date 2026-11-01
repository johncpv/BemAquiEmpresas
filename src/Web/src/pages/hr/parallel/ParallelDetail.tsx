import { useQueryClient } from '@tanstack/react-query'
import { Download, RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { classifyPayrollDifference, comparePayrollParallel, getDownloadPayrollCorrectionCasesUrl, getDownloadPayrollParallelTermUrl, getGetPayrollParallelSensitiveQueryKey, useGetPayrollParallelSensitive } from '@/api/generated/bemaqui'
import type { PayrollDifferenceResult, PayrollParallelCell, PayrollParallelDetail, PayrollParallelEmployee } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { periodLabel } from '../payroll/payroll'
import { AcceptDialog } from './ParallelDialogs'
import { CLASSIFICATIONS, type Classification, formatDiffPct, monthOf, PARALLEL_DICAS, STATUS, TYPE_LABELS, visibleRows } from './parallel'

const secondary = 'inline-flex min-h-11 items-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'

/**
 * A competência lado a lado (CA 1 e 2, só com hr.view_sensitive): os líquidos, as rubricas com mais diferença, a tabela por funcionário
 * e rubrica (contadora, sistema, diferença em R$ e em %) com "Só diferenças", o drill-down na memória do sistema com a classificação, o
 * termo, os casos de correção, "Comparar de novo" e o aceite pelo Proprietário. A 400 px, cartões por funcionário.
 */
export function ParallelDetail({ id, isOwner, onChanged }: { id: string; isOwner: boolean; onChanged: () => Promise<void> }) {
  const client = useQueryClient()
  const query = useGetPayrollParallelSensitive(id)
  const [onlyDifferences, setOnlyDifferences] = useState(true)
  const [open, setOpen] = useState<{ employee: PayrollParallelEmployee; cell: PayrollParallelCell } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetPayrollParallelSensitiveQueryKey(id) })
    await onChanged()
  }

  if (query.isPending) return <Skeleton className="h-64" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a comparação." message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => query.refetch()} />
  const d = query.data
  const run = d.run
  const month = periodLabel(run.period)
  const status = STATUS[run.status] ?? { label: run.statusLabel, tone: 'neutral' as const }
  const hasCases = d.employees.some((e) => e.rows.some((r) => r.hasCorrectionCase))

  const compareAgain = async () => {
    setBusy(true)
    setError(null)
    try {
      await comparePayrollParallel({ period: monthOf(run.period), type: run.payrollType })
      notifySuccess('Comparada de novo')
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível comparar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  const download = async (url: string, name: string) => {
    setError(null)
    try {
      await apiDownload(url, name)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível baixar o arquivo. Tente de novo.')
    }
  }

  return (
    <section aria-labelledby="comparacao" className="space-y-4 rounded-md border border-border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="comparacao" className="flex flex-wrap items-center gap-2 text-base font-semibold">
          {TYPE_LABELS[run.runType] ?? run.runType} de {month}
          <StatusChip tone={status.tone}>{status.label}</StatusChip>
        </h2>
        <div className="flex flex-wrap gap-2">
          {run.status !== 'Accepted' ? (
            <button type="button" className={secondary} disabled={busy} onClick={() => void compareAgain()}>
              <RefreshCw aria-hidden="true" className="size-4" />{busy ? 'Comparando...' : 'Comparar de novo'}
            </button>
          ) : null}
          {hasCases ? (
            <button type="button" className={secondary} onClick={() => void download(getDownloadPayrollCorrectionCasesUrl(id), `casos-correcao-${monthOf(run.period)}.json`)}>
              <Download aria-hidden="true" className="size-4" />Baixar casos de correção
            </button>
          ) : null}
          {d.canAccept ? (
            <span className="inline-flex items-center">
              <button type="button" className={secondary} onClick={() => void download(getDownloadPayrollParallelTermUrl(id), `termo-conferencia-${monthOf(run.period)}.pdf`)}>
                <Download aria-hidden="true" className="size-4" />Baixar termo de conferência
              </button>
              <Dica nome="Termo de conferência" texto={PARALLEL_DICAS.termo} />
            </span>
          ) : null}
          {d.canAccept && isOwner ? <AcceptDialog detail={d} onAccepted={refresh} /> : null}
        </div>
      </div>
      <FormError message={error ?? undefined} />
      {run.status === 'Accepted' ? (
        <p role="status" className="text-sm">Competência aceita em {run.acceptedAt ? formatDateTime(run.acceptedAt) : ''}{run.acceptedByName ? ` por ${run.acceptedByName}` : ''}; contadora {run.accountantName}, CRC {run.accountantCrc}.</p>
      ) : run.status === 'Rejected' ? (
        <p role="note" className="rounded-md border-l-4 border-l-crit bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          Há diferença em {run.differences === 1 ? '1 linha' : `${run.differences} linhas`}: classifique cada uma, corrija a causa (folha da contadora corrigida ou parâmetro ajustado e folha recalculada) e compare de novo. Só a diferença zero libera o aceite.
        </p>
      ) : null}
      <Summary detail={d} />
      {d.items.length > 0 ? (
        <div>
          <h3 className="text-sm font-semibold">Rubricas com mais diferença</h3>
          <ul className="mt-1 flex flex-wrap gap-2 text-sm">
            {d.items.map((i) => (
              <li key={i.code} className="rounded-md border border-border px-2 py-1 tabular-nums">{i.code} {i.name}: {i.differences} {i.differences === 1 ? 'funcionário' : 'funcionários'}, <Money value={i.totalDifference} /></li>
            ))}
          </ul>
        </div>
      ) : null}
      <label className="inline-flex items-center gap-2 text-sm">
        <input type="checkbox" checked={onlyDifferences} onChange={(e) => setOnlyDifferences(e.target.checked)} className="size-4" />
        Só diferenças
      </label>
      <Table detail={d} onlyDifferences={onlyDifferences} onOpen={(employee, cell) => setOpen({ employee, cell })} />
      {open ? (
        <DifferencePanel runId={id} accepted={run.status === 'Accepted'} employee={open.employee} cell={open.cell} onClose={() => setOpen(null)} onSaved={refresh} />
      ) : null}
    </section>
  )
}

function Summary({ detail }: { detail: PayrollParallelDetail }) {
  return (
    <dl className="grid gap-2 text-sm sm:grid-cols-3">
      <div className="rounded-md bg-muted px-3 py-2"><dt className="text-muted-foreground">Líquido da contadora</dt><dd className="text-base font-semibold"><Money value={detail.enteredNet} /></dd></div>
      <div className="rounded-md bg-muted px-3 py-2"><dt className="text-muted-foreground">Líquido do sistema</dt><dd className="text-base font-semibold"><Money value={detail.calculatedNet} /></dd></div>
      <div className="rounded-md bg-muted px-3 py-2"><dt className="text-muted-foreground">Diferença do líquido</dt><dd className="text-base font-semibold"><Money value={detail.netDifference} highlightNegative /></dd></div>
    </dl>
  )
}

/** Tabela analítica (padrão 3): coluna fixa do funcionário; a célula da diferença abre o painel com o mesmo total. */
function Table({ detail, onlyDifferences, onOpen }: { detail: PayrollParallelDetail; onlyDifferences: boolean; onOpen: (e: PayrollParallelEmployee, c: PayrollParallelCell) => void }) {
  const people = detail.employees.map((e) => ({ employee: e, rows: visibleRows(e.rows, onlyDifferences) })).filter((p) => p.rows.length > 0)
  if (people.length === 0) {
    return <EmptyState title="Nenhuma diferença nesta competência." description={'Desmarque "Só diferenças" para ver todas as rubricas lado a lado.'} />
  }

  const situation = (c: PayrollParallelCell) => c.equal ? <StatusChip tone="ok">Igual</StatusChip> : <StatusChip tone="crit">Diferente</StatusChip>
  return (
    <>
      <div className="hidden overflow-x-auto sm:block">
        <table className="w-full min-w-[720px] text-sm">
          <thead>
            <tr className="border-b border-border text-left text-xs text-muted-foreground">
              <th className="sticky left-0 bg-card py-2 pr-3 font-semibold">Funcionário</th>
              <th className="py-2 pr-3 font-semibold">Rubrica</th>
              <th className="py-2 pr-3 text-right font-semibold">Contadora</th>
              <th className="py-2 pr-3 text-right font-semibold">Sistema</th>
              <th className="py-2 pr-3 text-right font-semibold">Diferença</th>
              <th className="py-2 pr-3 text-right font-semibold"><span className="inline-flex items-center">Diferença %</span></th>
              <th className="py-2 font-semibold">Situação</th>
            </tr>
          </thead>
          <tbody>
            {people.map(({ employee, rows }) => rows.map((c, i) => (
              <tr key={c.id} className="border-b border-border">
                <td className="sticky left-0 bg-card py-2 pr-3 align-top">
                  {i === 0 ? (
                    <>
                      <span className="font-semibold">{employee.registration ? `${employee.registration} — ` : ''}{employee.name}</span>
                      <span className="block text-xs tabular-nums text-muted-foreground">Líquido <Money value={employee.enteredNet} /> × <Money value={employee.calculatedNet} /></span>
                    </>
                  ) : null}
                </td>
                <td className="py-2 pr-3">{c.code} {c.name}</td>
                <td className="py-2 pr-3 text-right"><Money value={c.entered} /></td>
                <td className="py-2 pr-3 text-right"><Money value={c.calculated} /></td>
                <td className="py-2 pr-3 text-right">
                  <button type="button" onClick={() => onOpen(employee, c)} className="font-semibold tabular-nums text-primary underline-offset-2 hover:underline" aria-label={`Ver a diferença de ${c.code} de ${employee.name}`}>
                    <Money value={c.difference} />
                  </button>
                </td>
                <td className="py-2 pr-3 text-right tabular-nums">{formatDiffPct(c.differencePct)}</td>
                <td className="py-2">{situation(c)}{c.classificationLabel ? <span className="ml-1 text-xs text-muted-foreground">{c.classificationLabel}</span> : null}</td>
              </tr>
            )))}
          </tbody>
        </table>
        <p className="mt-1 inline-flex items-center text-xs text-muted-foreground">A diferença % é sobre o valor da contadora.<Dica nome="Diferença %" texto={PARALLEL_DICAS.diferencaPct} /></p>
      </div>
      <ul aria-label="Funcionários" className="space-y-2 sm:hidden">
        {people.map(({ employee, rows }) => (
          <li key={employee.employeeId} className="rounded-md border border-border p-3 text-sm">
            <p className="font-semibold">{employee.name}</p>
            <p className="text-xs tabular-nums text-muted-foreground">Líquido <Money value={employee.enteredNet} /> × <Money value={employee.calculatedNet} /></p>
            <ul className="mt-2 space-y-2">
              {rows.map((c) => (
                <li key={c.id}>
                  <button type="button" onClick={() => onOpen(employee, c)} className="flex w-full min-h-11 flex-wrap items-center justify-between gap-1 rounded-md border border-border px-2 py-1 text-left">
                    <span>{c.code} {c.name}</span>
                    <span className="tabular-nums"><Money value={c.entered} /> × <Money value={c.calculated} /> · <b><Money value={c.difference} /></b> ({formatDiffPct(c.differencePct)})</span>
                  </button>
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ul>
    </>
  )
}

/** Drill-down da diferença (padrão 3): os mesmos valores da célula, a memória do sistema e a classificação com a justificativa (CA 2). */
function DifferencePanel({ runId, accepted, employee, cell, onClose, onSaved }: {
  runId: string; accepted: boolean; employee: PayrollParallelEmployee; cell: PayrollParallelCell; onClose: () => void; onSaved: () => Promise<void>
}) {
  const [classification, setClassification] = useState<Classification | ''>((cell.classification as Classification | null) ?? '')
  const [justification, setJustification] = useState(cell.justification ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<PayrollDifferenceResult | null>(null)
  const canClassify = !accepted && !cell.equal
  const save = async () => {
    if (!classification) {
      setError('Escolha a classificação da diferença.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      const saved = await classifyPayrollDifference(runId, cell.id, { classification, justification })
      setResult(saved)
      notifySuccess('Classificação salva')
      await onSaved()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={`${cell.code} ${cell.name}`} description={employee.name}
      footer={canClassify ? <button type="button" disabled={busy} onClick={() => void save()} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">{busy ? 'Salvando...' : 'Salvar classificação'}</button> : undefined}>
      <dl className="grid grid-cols-2 gap-2 text-sm">
        <dt className="text-muted-foreground">Contadora</dt><dd className="text-right"><Money value={cell.entered} /></dd>
        <dt className="text-muted-foreground">Sistema</dt><dd className="text-right"><Money value={cell.calculated} /></dd>
        <dt className="font-semibold">Diferença</dt><dd className="text-right font-semibold"><Money value={cell.difference} /> ({formatDiffPct(cell.differencePct)})</dd>
      </dl>
      <h3 className="mt-4 text-sm font-semibold">Memória de cálculo do sistema</h3>
      {cell.memory.length === 0 ? <p className="text-sm text-muted-foreground">Sem memória: a rubrica não saiu do motor (lançamento avulso ou só na folha da contadora).</p> : (
        <ul className="mt-1 list-disc space-y-1 pl-5 text-sm">{cell.memory.map((m, i) => <li key={i}>{m}</li>)}</ul>
      )}
      {canClassify ? (
        <div className="mt-4 space-y-3">
          <Field label="Classificação" required>
            {(f) => (
              <select {...f} value={classification} onChange={(e) => setClassification(e.target.value as Classification)} className={inputClass}>
                <option value="">Escolha</option>
                {CLASSIFICATIONS.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
              </select>
            )}
          </Field>
          <Field label="Justificativa" required hint="De 5 a 500 caracteres. A justificativa não aceita a competência: corrija e compare de novo.">
            {(f) => <textarea {...f} value={justification} maxLength={500} rows={4} onChange={(e) => setJustification(e.target.value)} className={inputClass} />}
          </Field>
          <FormError message={error ?? undefined} />
        </div>
      ) : cell.classificationLabel ? (
        <p className="mt-4 text-sm"><b>{cell.classificationLabel}</b>{cell.justification ? `: ${cell.justification}` : ''}</p>
      ) : null}
      {result?.nextStep ? (
        <a href={result.nextStep.link} className="mt-3 inline-flex min-h-11 items-center rounded-md border border-primary-soft bg-primary-soft px-3 py-2 text-sm font-semibold text-primary">{result.nextStep.label}</a>
      ) : null}
      {result?.cell.hasCorrectionCase ? <p role="status" className="mt-3 text-sm">Caso de correção gerado, sem dado que identifique o funcionário. Baixe em &quot;Baixar casos de correção&quot;.</p> : null}
    </SidePanel>
  )
}
