import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * Atributo neutro (FND-09d CA 18), separado do chip de situação: "Cliente", "Sem documento", "Importado do Dolibarr".
 * Não carrega gravidade; o significado está no texto.
 */
export function Tag({ children, className, title }: { children: ReactNode; className?: string; title?: string }) {
  return (
    <span title={title} className={cn('inline-flex items-center rounded-full border border-border bg-muted px-2 py-0.5 text-xs font-medium text-foreground', className)}>
      {children}
    </span>
  )
}
