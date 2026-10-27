import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Download, Lock, Mail } from 'lucide-react'
import { useState } from 'react'
import { getDownloadAccountantExportUrl, getGetAccountantExportOverviewQueryKey, useGetAccountantExportOverview } from '@/api/generated/bemaqui'
import type { AccountantExportOverview } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { periodLabel, typeLabel } from '../payroll/payroll'
import { ACCOUNTANT_DICAS, currentMonth, mailtoContadora, monthOf, packageFileName, validMonth } from './accountant'

const primary = 'inline-flex min-h-11 items-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'

/**
 * RH → Exportar para a contadora (RH-03): a competência (sugerida = o mês anterior), o que vai entrar no pacote, o aviso da folha em
 * rascunho, "Gerar pacote (CSV)" e as exportações anteriores. Só com hr.view_sensitive; sem ela, o bloco "Dados protegidos" e nenhuma
 * chamada ao servidor.
 */
export function AccountantExportPage() {
  const canSensitive = useCan('hr.view_sensitive')
  return (
    <>
      <PageHeading title="RH: Exportar para a contadora">
        <p className="mt-2 text-muted-foreground">O pacote do mês para a contadora calcular a folha com o que já está no sistema, sem redigitar nada.</p>
      </PageHeading>
      <HrTabs />
      {canSensitive ? <ExportPanel /> : (
        <div className="flex max-w-3xl items-center gap-2 rounded-md border border-border bg-muted px-4 py-6 text-sm text-muted-foreground">
          <Lock aria-hidden="true" className="size-4" />
          Dados protegidos
          <Dica nome="Dados protegidos" texto={ACCOUNTANT_DICAS.protegidos} />
        </div>
      )}
    </>
  )
}

function ExportPanel() {
  const client = useQueryClient()
  const [month, setMonth] = useState<string | null>(null)
  const query = useGetAccountantExportOverview(month ? { period: month } : undefined, { query: { enabled: month === null || validMonth(month) } })
  const [generating, setGenerating] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<{ month: string; replaces: string | null } | null>(null)
  const shown = month ?? (query.data ? monthOf(query.data.period) : '')
  const invalid = month !== null && !validMonth(month)

  const generate = async (view: AccountantExportOverview) => {
    setGenerating(true)
    setError(null)
    try {
      const target = monthOf(view.period)
      await apiDownload(getDownloadAccountantExportUrl({ period: target }), packageFileName(target, view.layout))
      setDone({ month: target, replaces: view.previous ? formatDateTime(view.previous.at) : null })
      notifySuccess('Pacote gerado')
      void client.invalidateQueries({ queryKey: getGetAccountantExportOverviewQueryKey(month ? { period: month } : undefined) })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível gerar o pacote. Tente de novo.')
    } finally {
      setGenerating(false)
    }
  }

  return (
    <div className="space-y-6">
      <section aria-label="Gerar pacote" className="max-w-3xl space-y-4 rounded-md border border-border bg-card p-4">
        <div className="flex flex-wrap items-end gap-3">
          <label className="space-y-1 text-sm font-medium">
            <span>Competência</span>
            <input type="month" aria-label="Competência" value={shown} max={currentMonth()} aria-invalid={invalid ? true : undefined}
              onChange={(e) => { setMonth(e.target.value); setDone(null); setError(null) }} className={inputClass + ' block max-w-48 tabular-nums'} />
          </label>
          {query.data ? (
            <span className="flex flex-wrap items-center gap-2 pb-2 text-sm">
              <span className="inline-flex items-center font-semibold">Layout padrão {query.data.layout}<Dica nome="Layout padrão" texto={ACCOUNTANT_DICAS.layout} /></span>
              {query.data.layoutConfirmed ? <StatusChip tone="ok">Conferido com a contadora</StatusChip> : <StatusChip tone="warn">A confirmar com a contadora</StatusChip>}
            </span>
          ) : null}
        </div>
        {invalid ? <p role="alert" className="text-sm font-medium text-crit-ink">Escolha o mês atual ou um mês anterior.</p> : null}
        {query.isPending && !invalid ? <Skeleton className="h-32" /> : query.isError ? (
          <ErrorState title="Não foi possível montar o pacote." message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => query.refetch()} />
        ) : query.data && !invalid ? (
          <Preview view={query.data} generating={generating} error={error} onGenerate={() => void generate(query.data)} />
        ) : null}
        {done ? (
          <div className="space-y-2">
            <p role="status" className="text-sm font-semibold">Pacote de {periodLabel(done.month)} gerado.{done.replaces ? ` Substitui a exportação de ${done.replaces}.` : ''}</p>
            <NextSteps onDismiss={() => setDone(null)} steps={[{ label: 'Avisar a contadora', icon: <Mail aria-hidden="true" className="size-3.5" />, onSelect: () => { window.location.href = mailtoContadora(done.month, done.replaces) } }]} />
          </div>
        ) : null}
      </section>
      <History view={query.data} pending={query.isPending && !invalid} />
    </div>
  )
}

function Preview({ view, generating, error, onGenerate }: { view: AccountantExportOverview; generating: boolean; error: string | null; onGenerate: () => void }) {
  return (
    <div className="space-y-3">
      <div>
        <h2 className="text-sm font-semibold">O que vai entrar no pacote de {periodLabel(monthOf(view.period))}</h2>
        <ul aria-label="O que vai entrar no pacote" className="mt-2 space-y-1 text-sm">
          {view.sections.map((s) => (
            <li key={s.key} className="flex flex-wrap items-center gap-2">
              <StatusChip tone={s.included ? 'ok' : 'neutral'}>{s.included ? 'Entra' : 'Não entra'}</StatusChip>
              <span>{s.summary}{s.fileName ? <span className="text-muted-foreground"> ({s.fileName})</span> : null}</span>
            </li>
          ))}
        </ul>
      </div>
      {view.draftRuns.map((r) => (
        <p key={r.id} role="note" className="flex flex-wrap items-center gap-2 rounded-md border-l-4 border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          A folha {typeLabel(r.type)} de {periodLabel(monthOf(view.period))} ainda está em rascunho: ela não entra no pacote.
          <Link to="/app/rh/folha/$id" params={{ id: r.id }} className="inline-flex min-h-11 items-center font-semibold underline sm:min-h-0">Abrir a folha</Link>
        </p>
      ))}
      {view.notIncluded.length > 0 ? (
        <details className="text-sm">
          <summary className="cursor-pointer font-semibold text-muted-foreground">O que não entra ({view.notIncluded.length})</summary>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-muted-foreground">{view.notIncluded.map((n) => <li key={n}>{n}</li>)}</ul>
        </details>
      ) : null}
      {view.previous ? <p className="text-sm text-muted-foreground">Já exportada em {formatDateTime(view.previous.at)}{view.previous.userName ? ` por ${view.previous.userName}` : ''}: o novo pacote substitui essa exportação.</p> : null}
      <FormError message={error ?? undefined} />
      <button type="button" onClick={onGenerate} disabled={generating} className={primary}>
        <Download aria-hidden="true" className="size-4" />
        {generating ? 'Gerando pacote...' : 'Gerar pacote (CSV)'}
      </button>
    </div>
  )
}

function History({ view, pending }: { view: AccountantExportOverview | undefined; pending: boolean }) {
  return (
    <section aria-labelledby="exportacoes-anteriores" className="max-w-3xl space-y-2">
      <h2 id="exportacoes-anteriores" className="text-base font-semibold">Exportações anteriores</h2>
      {pending ? <Skeleton className="h-24" /> : !view ? null : view.history.length === 0 ? (
        <EmptyState title="Nenhuma exportação ainda. Escolha o mês e gere o pacote." description="Cada pacote gerado fica registrado aqui (quando, quem, a competência e a versão do layout). O arquivo não fica guardado no sistema." />
      ) : (
        <>
          <table className="hidden w-full text-sm sm:table">
            <thead>
              <tr className="border-b border-border text-left text-xs text-muted-foreground">
                <th className="py-2 pr-3 font-semibold">Gerado em</th>
                <th className="py-2 pr-3 font-semibold">Quem</th>
                <th className="py-2 pr-3 font-semibold">Competência</th>
                <th className="py-2 font-semibold">Layout</th>
              </tr>
            </thead>
            <tbody>
              {view.history.map((h) => (
                <tr key={h.id} className="border-b border-border">
                  <td className="py-2 pr-3 tabular-nums">{formatDateTime(h.at)}</td>
                  <td className="py-2 pr-3">{h.userName ?? 'Usuário removido'}</td>
                  <td className="py-2 pr-3">{periodLabel(monthOf(h.period))}</td>
                  <td className="py-2">{h.layout}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <ul aria-label="Exportações anteriores" className="space-y-2 sm:hidden">
            {view.history.map((h) => (
              <li key={h.id} className="rounded-md border border-border bg-card p-3 text-sm">
                <p className="font-semibold">{periodLabel(monthOf(h.period))} · layout {h.layout}</p>
                <p className="text-muted-foreground"><span className="tabular-nums">{formatDateTime(h.at)}</span> · {h.userName ?? 'Usuário removido'}</p>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}
