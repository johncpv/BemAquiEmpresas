import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { type ReactNode, useEffect, useRef } from 'react'
import { getGetPlanningQueueQueryKey, getGetPlanningSummaryQueryKey, getListOrderForecastsQueryKey, useGetPlanningSummary, useRecalculateSchedule } from '@/api/generated/bemaqui'
import type { PlanningSummary } from '@/api/generated/model'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'
import { failedLine, PLANNING_DICAS, runLine, warningAction } from './planning'
import { QueueTab } from './QueueTab'

const primary = 'inline-flex min-h-[44px] items-center justify-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const action = 'inline-flex min-h-[44px] items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'

/**
 * Fábrica → Produção → Planejamento (PLN-01a e PLN-02): o cabeçalho com a última execução do plano e "Recalcular agora" (production.edit);
 * as abas Fila (padrão, PLN-02) e Sem previsão (as OS que o plano não conseguiu datar, cada uma com a ação que resolve). Cronograma e
 * Ocupação chegam na PLN-03 e PLN-04.
 */
export function PlanningPage() {
  const client = useQueryClient()
  const canEdit = useCan('production.edit')
  // PLN-01b: enquanto há recálculo pendente, o cabeçalho mostra "Recalculando…" e confere de novo a cada 3 s.
  const query = useGetPlanningSummary({ query: { refetchInterval: (q) => (q.state.data?.pending ? 3000 : false) } })
  const recalc = useRecalculateSchedule()
  // PLN-02: terminado o recálculo (salvar a fila pede um), a fila busca as datas novas.
  const pending = query.data?.pending ?? false
  const wasPending = useRef(pending)
  useEffect(() => {
    if (wasPending.current && !pending) void client.invalidateQueries({ queryKey: getGetPlanningQueueQueryKey() })
    wasPending.current = pending
  }, [pending, client])

  const recalculate = async () => {
    try {
      const result = await recalc.mutateAsync()
      if (result.run) {
        if (result.run.status === 'Failed') notifyError(failedLine(result.run))
        else notifySuccess(runLine(result.run))
      } else {
        notifySuccess('Recálculo na fila. As datas novas chegam em instantes.')
      }
      await client.invalidateQueries({ queryKey: getGetPlanningSummaryQueryKey() })
      await client.invalidateQueries({ queryKey: getListOrderForecastsQueryKey() })
      await client.invalidateQueries({ queryKey: getGetPlanningQueueQueryKey() })
    } catch {
      notifyError('Não foi possível recalcular. Tente de novo em instantes.', () => void recalculate())
    }
  }
  const button = canEdit ? (
    <button type="button" className={primary} disabled={recalc.isPending} onClick={() => void recalculate()}>
      {recalc.isPending ? 'Recalculando…' : 'Recalcular agora'}
    </button>
  ) : null

  return (
    <>
      <PageHeading title="Produção: Planejamento">
        <p className="mt-2 text-muted-foreground">O plano calcula quando cada etapa de cada OS começa e termina, pela fila e pelas horas que cada centro tem por dia.</p>
      </PageHeading>
      <ProductionTabs />
      {query.isPending ? <Skeleton className="h-40" /> : query.isError ? (
        <ErrorState title="Não foi possível carregar o planejamento." message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => query.refetch()} />
      ) : (
        <div className="space-y-4">
          <PlanningHeader summary={query.data} button={button} recalculating={recalc.isPending} />
          <UrlTabs label="Seções do planejamento" fallback="fila" tabs={[
            { id: 'fila', label: 'Fila', content: <QueueTab canEdit={canEdit} /> },
            { id: 'sem-previsao', label: `Sem previsão (${query.data.warnings.length})`, content: <Warnings summary={query.data} /> },
          ]} />
        </div>
      )}
    </>
  )
}

function PlanningHeader({ summary, button, recalculating }: { summary: PlanningSummary; button: ReactNode; recalculating: boolean }) {
  const last = summary.lastRun
  const failed = last?.status === 'Failed'
  return (
    <>
      <section aria-label="Última execução do plano" className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-border bg-card p-4">
        <div className="min-w-0 space-y-1 text-sm">
          <p className="font-semibold tabular-nums" aria-live="polite">
            {recalculating || summary.pending ? 'Recalculando…' : summary.lastSuccess ? runLine(summary.lastSuccess) : 'O plano ainda não foi calculado.'}
          </p>
          <p className="text-muted-foreground tabular-nums">
            {summary.openOrders === 1 ? '1 OS de fabricação aberta' : `${summary.openOrders} OS de fabricação abertas`}
            {summary.lastSuccess ? `, ${summary.lastSuccess.changedOrdersCount} com data alterada no último cálculo` : ''}. O plano se refaz sozinho a cada mudança na fábrica (em até 10 s) e de hora em hora.
          </p>
          {summary.pending && summary.lastSuccess ? <StatusChip tone="info">{runLine(summary.lastSuccess)}</StatusChip> : null}
        </div>
        {button}
      </section>

      {failed && last ? (
        <div role="alert" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-l-4 border-crit-soft border-l-crit bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <span>{failedLine(last)}</span>
          {button}
        </div>
      ) : null}
    </>
  )
}

function Warnings({ summary }: { summary: PlanningSummary }) {
  if (summary.openOrders === 0) {
    return (
      <EmptyState title="Nenhuma OS de fabricação aberta para planejar." description="As OS nascem com a aprovação do orçamento ou pelo botão na lista de OS."
        action={<Link to={'/app/producao/os' as never} className={action}>Ver OS de fabricação</Link>} />
    )
  }

  if (summary.warnings.length === 0) {
    return <p className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Todas as OS abertas têm previsão. Veja as datas na aba Etapas de cada OS e no Kanban.</p>
  }

  return (
    <section aria-labelledby="sem-previsao">
      <h2 id="sem-previsao" className="mb-2 flex items-center font-semibold">
        OS sem previsão ({summary.warnings.length})<Dica nome="Sem previsão" texto={PLANNING_DICAS.semPrevisao} />
      </h2>
      <ul className="divide-y divide-border rounded-md border border-border bg-card">
        {summary.warnings.map((w) => {
          const next = warningAction(w)
          return (
            <li key={w.orderId} className="flex flex-wrap items-center justify-between gap-2 border-l-4 border-l-warn p-3 text-sm">
              <span className="min-w-0">
                <Link to={'/app/producao/os/$id' as never} params={{ id: w.orderId } as never} className="font-semibold text-primary tabular-nums hover:underline">{w.orderNumber}</Link>
                <span className="block text-warn-ink">Sem previsão: {w.reason}</span>
              </span>
              <Link to={next.to as never} params={next.params as never} search={next.search as never} className={action}>{next.label}</Link>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
