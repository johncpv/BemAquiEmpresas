import { Link } from '@tanstack/react-router'
import { useGetOrderQuality } from '@/api/generated/bemaqui'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { ageLabel, NC_STATUS, QUALITY_DICAS, resultLabel } from './quality'

const action = 'inline-flex min-h-9 items-center rounded-md border border-border px-3 text-sm font-semibold text-primary'

/**
 * Aba Qualidade da OS (PRD-05 CA 7 e 11): cada tentativa (a mais nova primeiro) com o resultado, quem decidiu e quando, a marca
 * "Autoinspeção", a NC e os itens do checklist daquela tentativa; e as NC da OS. A tentativa em andamento leva à inspeção.
 */
export function OrderQualityTab({ orderId }: { orderId: string }) {
  const query = useGetOrderQuality(orderId)
  const canDecide = useCan('production.approve_quality')
  if (query.isPending) return <Skeleton className="h-40" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a qualidade da OS." onRetry={() => query.refetch()} />
  const q = query.data
  if (q.attempts.length === 0) {
    return <EmptyState title="A OS ainda não chegou à qualidade." description="Quando a etapa anterior à de qualidade terminar, a inspeção aparece aqui com o checklist e o resultado." />
  }
  return (
    <div className="space-y-5">
      <p className="flex flex-wrap items-center gap-2 text-sm">
        <span className="tabular-nums">Tentativa atual: {q.currentAttempt}</span><Dica nome="Tentativa" texto={QUALITY_DICAS.tentativa} />
        {q.selfInspection ? <span className="inline-flex items-center"><StatusChip tone="warn">Autoinspeção</StatusChip><Dica nome="Autoinspeção" texto={QUALITY_DICAS.autoinspecao} /></span> : null}
        {q.readyToComplete ? <StatusChip tone="ok">Pronta para concluir</StatusChip> : null}
      </p>
      <ol className="space-y-3" aria-label="Tentativas da qualidade">
        {q.attempts.map((a) => (
          <li key={`${a.stepId}-${a.attempt}-${a.result ?? 'pendente'}`} className="rounded-md border border-border bg-card p-3 text-sm">
            <p className="flex flex-wrap items-center gap-2">
              <b className="font-semibold tabular-nums">Tentativa {a.attempt}</b>
              <span>{a.stepCode} · {a.stepName}</span>
              {a.result === 'Approved' ? <StatusChip tone="ok">Aprovada</StatusChip> : a.result === 'Rejected' ? <StatusChip tone="crit">Reprovada</StatusChip> : <StatusChip tone="info">Aguardando decisão</StatusChip>}
              {a.selfInspection ? <StatusChip tone="warn">Autoinspeção</StatusChip> : null}
              {a.decidedAt ? <span className="text-muted-foreground tabular-nums">{a.decidedByName ?? 'Usuário'} · {formatDateTime(a.decidedAt)}</span> : null}
              {!a.result && canDecide ? <Link to="/app/producao/qualidade/$stepId" params={{ stepId: a.stepId }} className={action}>Inspecionar</Link> : null}
            </p>
            {a.nonConformityId ? (
              <p className="mt-1">
                <Link to="/app/producao/nao-conformidades/$id" params={{ id: a.nonConformityId }} className="font-semibold text-primary hover:underline">{a.nonConformityNumber}</Link>
                {a.returnToStepName ? <span className="text-muted-foreground"> · voltou para {a.returnToStepName}</span> : null}
              </p>
            ) : null}
            {a.items.length > 0 ? (
              <ul className="mt-2 grid gap-1 sm:grid-cols-2">
                {a.items.map((i) => (
                  <li key={i.text} className="flex flex-wrap items-center gap-2">
                    <StatusChip tone={i.result === 'NonConforming' ? 'crit' : i.result ? 'ok' : 'neutral'}>{resultLabel(i.result)}</StatusChip>
                    <span>{i.text}</span>
                    {i.markedByName ? <span className="text-xs text-muted-foreground">{i.markedByName}</span> : null}
                  </li>
                ))}
              </ul>
            ) : null}
          </li>
        ))}
      </ol>
      <section aria-labelledby="ncs-da-os" className="space-y-2">
        <h3 id="ncs-da-os" className="flex items-center text-sm font-semibold">Não conformidades da OS<Dica nome="Não conformidade" texto={QUALITY_DICAS.naoConformidade} /></h3>
        {q.nonConformities.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma não conformidade nesta OS.</p> : (
          <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
            {q.nonConformities.map((n) => (
              <li key={n.id} className="flex flex-wrap items-center gap-2 p-3">
                <Link to="/app/producao/nao-conformidades/$id" params={{ id: n.id }} className="font-semibold text-primary tabular-nums hover:underline">{n.number}</Link>
                <StatusChip tone={NC_STATUS[n.status].tone}>{NC_STATUS[n.status].label}</StatusChip>
                <span className="min-w-0 flex-1 truncate">{n.description}</span>
                <span className="text-muted-foreground">{n.responsibleName} · {ageLabel(n.ageDays)}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}