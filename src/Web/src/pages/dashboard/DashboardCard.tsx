import { Link } from '@tanstack/react-router'
import { useId, type ReactNode } from 'react'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { CARD_NAMES, HOW, updatedLabel } from './dashboard'

/** Estado de uma consulta do TanStack Query, o suficiente para o card. */
export interface CardQuery<T> {
  data?: T
  isPending: boolean
  isError: boolean
  refetch: () => unknown
}

/**
 * Card do painel (padrão 2 das Fases 4 a 8): título, "Como calculamos", estados próprios (skeleton, erro com "Tentar de novo"
 * sem derrubar os outros, CA 11) e "Atualizado às" (CA 12). Versão local do DSH-01a: unificar com o DashboardCard da DSH-02
 * depois do merge.
 */
export function DashboardCard<T extends { updatedAt: string }>({ code, title, description, how, query, action, compact, className, children }: {
  code: string
  title: string
  description?: string
  how: string
  query: CardQuery<T>
  action?: ReactNode
  compact?: boolean
  className?: string
  children: (data: T) => ReactNode
}) {
  const id = useId()
  return (
    <section aria-labelledby={id} data-card={code} className={cn('flex min-w-0 flex-col rounded-md border border-border bg-card', compact ? 'p-4' : 'p-4 sm:p-5', className)}>
      <header className="mb-3 flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h2 id={id} className={cn('flex items-center font-semibold', compact ? 'text-sm text-muted-foreground' : 'text-base')}>
            {title}
            <Dica nome={`Como calculamos: ${title}`} texto={how} />
          </h2>
          {description ? <p className="text-xs text-muted-foreground">{description}</p> : null}
        </div>
        {action}
      </header>
      <div className="flex-1">
        {query.isPending ? (
          <div aria-busy="true" aria-label={`Carregando ${CARD_NAMES[code] ?? title}`} className="space-y-2">
            <Skeleton className={compact ? 'h-8 w-2/3' : 'h-40'} />
            <Skeleton className="h-4 w-1/2" />
          </div>
        ) : query.isError || !query.data ? (
          <ErrorState className="py-6" title={`Não foi possível carregar ${CARD_NAMES[code] ?? title}.`} message="Os outros números continuam valendo. Tente de novo em instantes." onRetry={() => void query.refetch()} />
        ) : (
          children(query.data)
        )}
      </div>
      {query.data ? (
        <p className="mt-3 flex items-center text-xs text-muted-foreground">
          {updatedLabel(query.data.updatedAt)}
          <Dica nome="Atualizado às" texto={HOW.updated} />
        </p>
      ) : null}
    </section>
  )
}

/**
 * Número do painel (padrão 2): o valor é clicável e leva ao relatório de origem com os mesmos filtros (CA 7, "Ver origem").
 * Versão local do DSH-01a: unificar com o Kpi da DSH-02 depois do merge.
 */
export function Kpi({ value, href, label, sub, chip, meter }: { value: ReactNode; href?: string; label: string; sub?: ReactNode; chip?: ReactNode; meter?: { pct: number; label: string } }) {
  return (
    <div className="space-y-1.5">
      <p className="flex flex-wrap items-center gap-2">
        {href ? (
          <Link to={href as never} aria-label={`${label}: ver origem`} className="text-[26px] font-bold leading-tight tracking-tight tabular-nums text-foreground hover:text-primary hover:underline focus-visible:outline-2 focus-visible:outline-ring">
            {value}
          </Link>
        ) : (
          <span className="text-[26px] font-bold leading-tight tracking-tight tabular-nums">{value}</span>
        )}
        {chip}
      </p>
      {meter ? (
        <div role="meter" aria-label={meter.label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(meter.pct)} className="h-2 rounded-full bg-muted">
          <span className="block h-2 rounded-full bg-primary" style={{ width: `${meter.pct}%` }} />
        </div>
      ) : null}
      {sub ? <div className="text-sm text-muted-foreground">{sub}</div> : null}
    </div>
  )
}

/** "Ver origem" textual, com 44 px de alvo no celular. */
export function OriginLink({ href, children = 'Ver origem' }: { href: string; children?: ReactNode }) {
  return (
    <Link to={href as never} className="inline-flex min-h-11 items-center text-sm font-semibold text-primary hover:underline sm:min-h-0">
      {children}
    </Link>
  )
}
