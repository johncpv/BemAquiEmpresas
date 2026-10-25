import { Link } from '@tanstack/react-router'
import { AlertTriangle, ChevronRight } from 'lucide-react'
import { useEffect, type ReactNode } from 'react'
import { useGetMyAgenda } from '@/api/generated/bemaqui'
import type { AgendaFailure, AgendaItem } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { SELF_INSPECTION, SELF_INSPECTION_TIP, groupByTier, isEmpty, normalize, splitLink, stripe } from './agenda'

/** Recarrega a cada 60 s e ao voltar o foco para a aba (CA 6): o item resolvido em outra aba some sem recarregar a página. */
const REFRESH_MS = 60_000

function AppLink({ link, className, children, label }: { link: string; className?: string; children: ReactNode; label?: string }) {
  const { to, search } = splitLink(link)
  return (
    <Link to={to} search={search as never} className={className} aria-label={label}>
      {children}
    </Link>
  )
}

function Card({ item }: { item: AgendaItem }) {
  const tone = stripe(item.tier)
  return (
    <article
      aria-labelledby={`pauta-${item.category}`}
      className={cn('flex flex-col rounded-md border border-border bg-card p-4 shadow-xs', tone && 'border-l-4', tone === 'warn' && 'border-l-warn', tone === 'crit' && 'border-l-crit')}
    >
      <h4 id={`pauta-${item.category}`} className="font-semibold">{item.title}</h4>
      {item.why ? <p className="mt-1 text-sm text-muted-foreground">{item.why}</p> : null}
      <ul className="mt-3 divide-y divide-border">
        {item.entries.map((e, k) => (
          // A 400 px o cartão mostra os 3 primeiros; "Ver todos" leva à lista.
          <li key={e.link} className={cn('flex items-center gap-3 py-2', k >= 3 && 'max-sm:hidden')}>
            <div className="min-w-0 flex-1">
              <p className="flex flex-wrap items-center gap-x-2 text-sm font-medium tabular-nums">
                <span className="truncate">{e.title}</span>
                {e.badge === SELF_INSPECTION ? (
                  <span className="inline-flex items-center">
                    <StatusChip tone="warn">{SELF_INSPECTION}</StatusChip>
                    <Dica texto={SELF_INSPECTION_TIP} nome={SELF_INSPECTION} />
                  </span>
                ) : e.badge ? <StatusChip tone="info">{e.badge}</StatusChip> : null}
              </p>
              <p className="truncate text-xs text-muted-foreground tabular-nums">
                {[e.detail, e.dueDate ? formatDate(e.dueDate) : null].filter(Boolean).join(' · ')}
                {e.amount !== null && e.amount !== undefined ? <> · <Money value={e.amount} /></> : null}
              </p>
            </div>
            <AppLink link={e.link} label={`${item.action}: ${e.title}`}
              className="inline-flex min-h-11 flex-none items-center rounded-md border border-border px-3 text-sm font-semibold hover:border-primary sm:min-h-9">
              {item.action}
            </AppLink>
          </li>
        ))}
      </ul>
      {item.link ? (
        <AppLink link={item.link} className="mt-2 inline-flex min-h-11 items-center gap-1 self-start text-sm font-semibold text-primary sm:min-h-0">
          Ver todos ({item.count}) <ChevronRight aria-hidden="true" className="size-4" />
        </AppLink>
      ) : null}
    </article>
  )
}

function FailureCard({ failure, onRetry }: { failure: AgendaFailure; onRetry: () => void }) {
  return (
    <article role="alert" className="flex flex-col gap-2 rounded-md border border-crit-soft bg-card p-4">
      <p className="flex items-center gap-2 text-sm font-semibold">
        <AlertTriangle aria-hidden="true" className="size-4 text-crit-ink" />
        {failure.message}
      </p>
      <button type="button" onClick={onRetry} className="inline-flex min-h-11 items-center self-start rounded-md border border-border px-3 text-sm font-semibold sm:min-h-9">
        Tentar de novo
      </button>
    </article>
  )
}

/**
 * Minha pauta (DSH-04): o que depende de você hoje, em três níveis, cada linha com o verbo da ação e o link que já abre a ação.
 * Calculada na leitura (sem tabela), recarregada a cada 60 s e no foco. `onCount` leva a contagem ao título da aba (CA 7).
 */
export function MinhaPauta({ onCount }: { onCount: (total: number) => void }) {
  const query = useGetMyAgenda(undefined, { query: { refetchInterval: REFRESH_MS, refetchOnWindowFocus: true, staleTime: 0 } })
  const view = normalize(query.data)
  const total = view.total
  useEffect(() => onCount(total), [total, onCount])

  return (
    <section aria-labelledby="pauta" className="mb-8">
      <h2 id="pauta" className="mb-3 text-[15px] font-semibold">Minha pauta</h2>
      {query.isPending ? (
        <div className="grid gap-3 md:grid-cols-3" aria-busy="true" aria-label="Carregando a pauta">
          {[0, 1, 2].map((k) => <Skeleton key={k} className="h-40" />)}
        </div>
      ) : query.isError ? (
        <ErrorState title="Não foi possível carregar a sua pauta." message="Verifique a conexão e tente de novo. As telas dos módulos continuam funcionando." onRetry={() => void query.refetch()} />
      ) : isEmpty(view) ? (
        <EmptyState title="Nada pendente para você hoje." description="Quando houver aprovação, vencimento ou atraso que dependa de você, ele aparece aqui. Enquanto isso, use os atalhos dos seus módulos logo abaixo." />
      ) : (
        <div className="space-y-6">
          {groupByTier(view).map((group) => (
            <div key={group.tier} aria-labelledby={`nivel-${group.tier}`} role="group">
              <h3 id={`nivel-${group.tier}`} className="mb-2 flex items-center text-sm font-semibold uppercase tracking-wide text-muted-foreground">
                {group.label}
                {group.tip ? <Dica texto={group.tip} nome={group.label} /> : null}
              </h3>
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {group.items.map((item) => <Card key={item.category} item={item} />)}
                {group.failures.map((f) => <FailureCard key={f.category} failure={f} onRetry={() => void query.refetch()} />)}
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}
