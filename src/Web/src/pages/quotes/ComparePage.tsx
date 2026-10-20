import { Link, useParams, useSearch } from '@tanstack/react-router'
import { ArrowRight, Minus, Pencil, Plus } from 'lucide-react'
import { useGetQuote, useGetQuoteConditions, useGetQuoteRevision, useListQuoteCosts } from '@/api/generated/bemaqui'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { type Change, delta, diffItems } from './compare'

const ICONS: Record<Change, typeof Plus> = { Incluído: Plus, Removido: Minus, Alterado: Pencil, Igual: ArrowRight }

function Delta({ before, after }: { before: number; after: number }) {
  const { diff, pct } = delta(before, after)
  if (diff === 0) return <span className="text-xs text-muted-foreground">sem mudança</span>
  return <span className="text-xs font-semibold">{diff > 0 ? '+' : ''}<Money value={diff} />{pct === null ? '' : ` (${pct > 0 ? '+' : ''}${pct.toLocaleString('pt-BR')}%)`}</span>
}

/** Comparar revisões (COM-02 CA 9): itens, marcos, validade, prazo e totais, com o que mudou em texto e ícone. */
export function ComparePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { com } = useSearch({ strict: false }) as { com?: string }
  const left = useGetQuote(id)
  const right = useGetQuoteRevision(id, com ?? '', { query: { enabled: !!com } })
  const leftConditions = useGetQuoteConditions(id)
  const rightConditions = useGetQuoteConditions(com ?? '', { query: { enabled: !!com } })
  const canCost = useCan('quotes.view_cost')
  const costs = useListQuoteCosts({ ids: `${id},${com ?? ''}` }, { query: { enabled: canCost && !!com } })
  if (left.isPending || right.isPending) return <Skeleton className="h-96" />
  if (left.isError || right.isError || !com) return <ErrorState title="Não foi possível carregar as revisões." onRetry={() => { void left.refetch(); void right.refetch() }} />
  const [before, after] = left.data.revision <= right.data.revision ? [left.data, right.data] : [right.data, left.data]
  const cost = (qid: string) => costs.data?.find((c) => c.id === qid)
  const milestones = (qid: string) => (qid === id ? leftConditions.data : rightConditions.data)?.milestones ?? []
  return (
    <>
      <PageHeading title={`Comparar ${before.number}: rev. ${before.revision} × rev. ${after.revision}`}>
        <p className="mt-2 text-sm"><Link to={'/app/orcamentos/$id' as never} params={{ id: after.id } as never} className="font-semibold text-primary hover:underline">Voltar ao orçamento</Link></p>
      </PageHeading>
      <section aria-label="Totais" className="mb-4 grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
        <p><span className="block text-xs text-muted-foreground">Preço total</span><Money value={before.totalPrice} /> → <Money value={after.totalPrice} className="font-semibold" /> <Delta before={before.totalPrice} after={after.totalPrice} /></p>
        {canCost ? <p><span className="block text-xs text-muted-foreground">Custo total</span><Money value={cost(before.id)?.totalCost ?? 0} /> → <Money value={cost(after.id)?.totalCost ?? 0} className="font-semibold" /></p> : null}
        <p><span className="block text-xs text-muted-foreground">Validade</span>{before.validUntil ? formatDate(before.validUntil) : '—'} → {after.validUntil ? formatDate(after.validUntil) : '—'}</p>
        <p><span className="block text-xs text-muted-foreground">Prazo de entrega</span>{before.deliveryDays ?? '—'} → {after.deliveryDays ?? '—'} dias</p>
      </section>
      <ul className="space-y-2" aria-label="Itens comparados">
        {diffItems(before.items, after.items).map((d) => {
          const Icon = ICONS[d.change]
          return (
            <li key={d.sequence} className="grid gap-2 rounded-md border border-border bg-card p-3 text-sm sm:grid-cols-[8rem_1fr_1fr]">
              <span className="inline-flex items-center gap-1 font-semibold"><Icon aria-hidden="true" className="size-4" />{d.change}</span>
              <span><span className="block text-xs text-muted-foreground">Antes</span>{d.before ? <>{d.before.description} · {d.before.quantity} × <Money value={d.before.unitPrice} /></> : '—'}</span>
              <span><span className="block text-xs text-muted-foreground">Depois</span>{d.after ? <>{d.after.description} · {d.after.quantity} × <Money value={d.after.unitPrice} /></> : '—'}{d.before && d.after ? <> <Delta before={d.before.total} after={d.after.total} /></> : null}</span>
            </li>
          )
        })}
      </ul>
      <section aria-label="Marcos" className="mt-4 grid gap-3 text-sm sm:grid-cols-2">
        {[before, after].map((q) => (
          <div key={q.id} className="rounded-md border border-border bg-card p-3">
            <h2 className="mb-1 font-semibold">Marcos da rev. {q.revision}</h2>
            <ul>{milestones(q.id).map((m) => <li key={m.id}>{m.description}: {m.percent.toLocaleString('pt-BR')}% · <Money value={m.amount} /></li>)}</ul>
          </div>
        ))}
      </section>
    </>
  )
}
