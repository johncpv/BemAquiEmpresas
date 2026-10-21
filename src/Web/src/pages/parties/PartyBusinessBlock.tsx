import type { ReactNode } from 'react'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'

type Query = { isPending: boolean; isError: boolean; refetch: () => unknown }

/** Bloco da aba "Comercial e financeiro" (FIN-06b): carregando, erro com "Tentar de novo" e vazio, um por módulo. */
export function PartyBusinessBlock({ title, query, empty, link, children }: { title: string; query: Query; empty: boolean; link?: ReactNode; children: ReactNode }) {
  let body: ReactNode = children
  if (query.isPending) body = <Skeleton className="h-20" />
  else if (query.isError) body = <ErrorState title={`Não foi possível carregar ${title.toLowerCase()}.`} onRetry={() => void query.refetch()} />
  else if (empty) body = <EmptyState title={`Sem ${title.toLowerCase()} para este terceiro.`} />
  return (
    <section className="space-y-2" aria-label={title}>
      <h3 className="flex flex-wrap items-center justify-between gap-2 text-sm font-semibold">{title}{link}</h3>
      {body}
    </section>
  )
}
