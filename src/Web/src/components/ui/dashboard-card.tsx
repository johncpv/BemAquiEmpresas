import type { ReactNode } from 'react'
import { Dica } from '@/help/Dica'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { cn } from '@/lib/utils'

const time = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit' })

interface DashboardCardProps {
  title: string
  subtitle?: string
  /** Texto do "Como calculamos" (vira um <Dica>). */
  howWeCalculate?: string
  /** "Ver origem": leva ao drill-down ou à tela de origem dos números. */
  onOrigin?: () => void
  /** Quando os dados chegaram (o "Atualizado às {hh:mm}"), em ms (dataUpdatedAt do TanStack Query). */
  updatedAt?: number
  /** Estado da consulta do card; cada card tem os próprios estados (padrão 2 das Fases 4 a 8). */
  query?: { isPending: boolean; isError: boolean; refetch: () => unknown }
  /** Sem permissão: o card não é montado (nem a consulta). */
  allowed?: boolean
  empty?: { when: boolean; title: string; description?: string; action?: ReactNode }
  className?: string
  children?: ReactNode
}

/**
 * Card de painel (DSH-02 CA 16, D18): título, subtítulo, "Como calculamos", "Ver origem", "Atualizado às {hh:mm}" e os estados
 * carregando, vazio, erro ("Não foi possível carregar {card}." com "Tentar de novo") e sem permissão (não monta). Só tokens.
 */
export function DashboardCard({ title, subtitle, howWeCalculate, onOrigin, updatedAt, query, allowed = true, empty, className, children }: DashboardCardProps) {
  if (!allowed) return null
  let body: ReactNode
  if (query?.isPending) {
    body = <div aria-label={`Carregando ${title}`} className="space-y-2"><Skeleton className="h-6" /><Skeleton className="h-20" /></div>
  } else if (query?.isError) {
    body = <ErrorState title={`Não foi possível carregar ${title.charAt(0).toLowerCase()}${title.slice(1)}.`} message="Verifique a conexão e tente de novo. Se continuar, fale com o suporte." onRetry={() => query.refetch()} className="py-6" />
  } else if (empty?.when) {
    body = <EmptyState title={empty.title} description={empty.description} action={empty.action} className="py-6" />
  } else {
    body = children
  }

  const ready = query ? !query.isPending && !query.isError : true
  return (
    <section aria-label={title} className={cn('rounded-md border border-border bg-card p-4', className)}>
      <header className="mb-3 flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h2 className="inline-flex items-center text-base font-semibold">{title}{howWeCalculate ? <Dica nome={`Como calculamos: ${title}`} texto={howWeCalculate} /> : null}</h2>
          {subtitle ? <p className="text-xs text-muted-foreground">{subtitle}</p> : null}
        </div>
        <div className="flex items-center gap-3 text-xs text-muted-foreground">
          {ready && updatedAt ? <span className="tabular-nums">Atualizado às {time.format(new Date(updatedAt))}</span> : null}
          {ready && onOrigin ? <button type="button" onClick={onOrigin} className="min-h-[44px] font-semibold text-primary sm:min-h-0">Ver origem</button> : null}
        </div>
      </header>
      {body}
    </section>
  )
}
