import { Link, useParams } from '@tanstack/react-router'
import { useGetAmendment } from '@/api/generated/bemaqui'
import type { AmendmentDetail } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { COST_SECTIONS } from '../../quotes/quotes'
import { AmendmentActions } from './AmendmentActions'
import { AmendmentForm } from './AmendmentForm'
import { AMENDMENT_STATUS } from './amendments'

/** Aditivo (COM-07): novo, rascunho editável, enviado para aprovação, aprovado ou reprovado (modo leitura). */
export function AmendmentPage() {
  const { id, aditivoId } = useParams({ strict: false }) as { id: string; aditivoId: string }
  const isNew = aditivoId === 'novo'
  const query = useGetAmendment(id, aditivoId, { query: { enabled: !isNew } })
  const canEdit = useCan('projects.edit')
  if (isNew) return <><PageHeading title="Novo aditivo" /><AmendmentForm contractId={id} /></>
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o aditivo." onRetry={() => query.refetch()} />
  const a = query.data
  return (
    <>
      <PageHeading title={`Aditivo ${a.number} — ${a.contractNumber}`}>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <StatusChip tone={AMENDMENT_STATUS[a.status]?.tone ?? 'neutral'}>{AMENDMENT_STATUS[a.status]?.label ?? a.status}</StatusChip>
          {a.selfApproved ? <Tag>Aprovado pelo próprio autor</Tag> : null}
          <Link to={'/app/contratos/$id' as never} params={{ id } as never} search={{ aba: 'aditivos' } as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ver contrato</Link>
          <AmendmentActions amendment={a} />
        </div>
      </PageHeading>
      {a.status === 'Draft' && canEdit ? <AmendmentForm key={a.version} contractId={id} amendment={a} /> : <AmendmentReadOnly amendment={a} />}
    </>
  )
}

function AmendmentReadOnly({ amendment: a }: { amendment: AmendmentDetail }) {
  return (
    <div className="max-w-4xl space-y-4 text-sm">
      {a.status === 'Approved' ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2">Aditivo aprovado em {a.approvedAt ? formatDate(a.approvedAt.slice(0, 10)) : '—'}{a.approvedByName ? ` por ${a.approvedByName}` : ''}. Não muda mais.</p> : null}
      {a.status === 'Rejected' ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2">Aditivo reprovado: {a.rejectionComment}</p> : null}
      {a.status === 'PendingApproval' && !a.canDecide ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2">Aguardando a aprovação de outra pessoa.</p> : null}
      <p>{a.description}</p>
      <section aria-label="Antes e depois" className="grid gap-3 rounded-md border border-border bg-card p-4 sm:grid-cols-2">
        <p><span className="block text-xs text-muted-foreground">Contratado</span><Money value={a.preview.valueBefore} /> → <Money value={a.preview.valueAfter} className="font-semibold" /></p>
        <p><span className="block text-xs text-muted-foreground">Data prometida</span>{a.preview.promisedBefore ? `${formatDate(a.preview.promisedBefore)} → ${formatDate(a.preview.promisedAfter!)}` : '—'}</p>
      </section>
      {a.milestones.length ? (
        <ul className="divide-y divide-border rounded-md border border-border bg-card" aria-label="Marcos do acréscimo">
          {a.milestones.map((m) => <li key={m.sequence} className="flex justify-between gap-2 p-3"><span>{m.description} · {m.percent.toLocaleString('pt-BR')}%</span><Money value={m.amount} /></li>)}
        </ul>
      ) : null}
      {a.reductions.length ? (
        <ul className="divide-y divide-border rounded-md border border-border bg-card" aria-label="Títulos reduzidos">
          {a.reductions.map((r) => <li key={r.receivableId} className="flex justify-between gap-2 p-3"><span>{r.number ?? 'Título'}</span><span>{typeof r.currentAmount === 'number' ? <Money value={r.currentAmount} /> : null} → <Money value={r.newAmount} /></span></li>)}
        </ul>
      ) : null}
      {a.budgetDeltas && Object.keys(a.budgetDeltas).length ? (
        <ul className="rounded-md border border-border bg-card p-3" aria-label="Orçado por natureza">
          {Object.entries(a.budgetDeltas).map(([k, v]) => <li key={k} className="flex justify-between"><span>{COST_SECTIONS.find((s) => s.type === k)?.label ?? k}</span><Money value={v as number} /></li>)}
        </ul>
      ) : null}
    </div>
  )
}
