import { cn } from '@/lib/utils'

interface StageIndicatorProps {
  stages: readonly string[]
  /** Índice da etapa atual (0 = primeira). */
  current: number
  className?: string
}

/**
 * Indicador de etapa no estilo display de andar (CLAUDE.md, FND-09a CA 14): letra da etapa em --signal sobre o trilho
 * escuro e um segmento por etapa, preenchido até a atual.
 */
export function StageIndicator({ stages, current, className }: StageIndicatorProps) {
  const index = Math.min(Math.max(current, 0), stages.length - 1)
  const name = stages[index] ?? ''
  return (
    <span role="img" aria-label={`Etapa ${name}, ${index + 1} de ${stages.length}`} className={cn('inline-flex items-center gap-2', className)}>
      <span className="grid h-[26px] min-w-[30px] place-items-center rounded-[5px] bg-rail px-1.5 font-display text-[13px] font-extrabold tracking-wider text-signal font-wider">
        {name.charAt(0).toUpperCase()}
      </span>
      <span aria-hidden="true" className="flex gap-0.5">
        {stages.map((stage, i) => (
          <span key={stage} className={cn('h-1.5 w-3.5 rounded-sm', i <= index ? 'bg-signal' : 'bg-border')} />
        ))}
      </span>
    </span>
  )
}
