import { AlertTriangle, Inbox } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/** Bloco fantasma enquanto carrega (revisão §2.4). */
export function Skeleton({ className }: { className?: string }) {
  return <div aria-hidden="true" className={cn('animate-pulse rounded bg-muted', className)} />
}

/** Vazio com ação para começar (CLAUDE.md): a tela diz o que fazer, não só "sem dados". */
export function EmptyState({ title, description, action, className }: { title: string; description?: string; action?: ReactNode; className?: string }) {
  return (
    <div role="status" className={cn('flex flex-col items-center gap-2 rounded-md border border-dashed border-border bg-card px-6 py-10 text-center', className)}>
      <Inbox aria-hidden="true" className="size-6 text-muted-foreground" />
      <p className="font-semibold">{title}</p>
      {description ? <p className="max-w-md text-sm text-muted-foreground">{description}</p> : null}
      {action ? <div className="mt-2">{action}</div> : null}
    </div>
  )
}

/** Erro com o que houve e como resolver, e "Tentar de novo" ligado ao refetch do TanStack Query. */
export function ErrorState({ title = 'Não foi possível carregar.', message, onRetry, className }: { title?: string; message?: string; onRetry?: () => void; className?: string }) {
  return (
    <div role="alert" className={cn('flex flex-col items-center gap-2 rounded-md border border-crit-soft bg-card px-6 py-10 text-center', className)}>
      <AlertTriangle aria-hidden="true" className="size-6 text-crit-ink" />
      <p className="font-semibold">{title}</p>
      <p className="max-w-md text-sm text-muted-foreground">{message ?? 'Verifique a conexão e tente de novo. Se continuar, fale com o suporte.'}</p>
      {onRetry ? (
        <button type="button" onClick={onRetry} className="mt-2 min-h-11 rounded-md border border-border bg-card px-3 py-1.5 text-sm font-semibold">
          Tentar de novo
        </button>
      ) : null}
    </div>
  )
}

/** Mensagem de campo (FND-09c CA 8): ligada ao campo por aria-describedby. */
export function InlineError({ id, message }: { id: string; message?: string }) {
  if (!message) return null
  return (
    <p id={id} role="alert" className="mt-1 text-xs font-medium text-crit-ink">
      {message}
    </p>
  )
}
