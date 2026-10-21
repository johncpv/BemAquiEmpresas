import { segmentState, STAGE_COMPACT_ABOVE } from '@/lib/stages'
import { cn } from '@/lib/utils'

interface StageIndicatorProps {
  stages: readonly string[]
  /** Índice da etapa atual (0 = primeira). */
  current: number
  /** Letra de cada etapa vinda do roteiro (ENG-01, D10); a inicial do nome é só a reserva (ex.: "T" de Entregue, COM-04a CA 17). */
  letters?: readonly (string | null | undefined)[]
  /** Mostra o nome da etapa ao lado dos segmentos. */
  showLabel?: boolean
  className?: string
}

const SEGMENT = { done: 'bg-muted-foreground', now: 'bg-signal', next: 'bg-border' } as const

/**
 * Indicador de etapa no estilo display de andar (CLAUDE.md, FND-09a CA 14, ENG-01 CA 14): letra da etapa em --signal sobre
 * o trilho escuro e um segmento por etapa; só a atual fica em âmbar. Acima de 8 etapas mostra "letra · posição/total" para
 * caber a 400 px. O nome e a posição vão no rótulo acessível.
 */
export function StageIndicator({ stages, current, letters, showLabel, className }: StageIndicatorProps) {
  const total = stages.length
  if (total === 0) return null
  const index = Math.min(Math.max(current, 0), total - 1)
  const name = stages[index] ?? ''
  const letter = (letters?.[index] || name.trim().charAt(0)).toUpperCase()
  const compact = total > STAGE_COMPACT_ABOVE
  return (
    <span role="img" aria-label={`Etapa ${name}, ${index + 1} de ${total}`} className={cn('inline-flex min-w-0 max-w-full items-center gap-2', className)}>
      <span className="grid h-[26px] min-w-[30px] shrink-0 place-items-center rounded-[5px] bg-rail px-1.5 font-display text-[13px] font-extrabold tracking-wider text-signal font-wider">
        {letter}
      </span>
      {compact ? (
        <span aria-hidden="true" data-compact="" className="shrink-0 whitespace-nowrap text-xs font-semibold tabular-nums text-muted-foreground">· {index + 1}/{total}</span>
      ) : (
        <span aria-hidden="true" className="flex shrink-0 gap-0.5">
          {stages.map((_, i) => {
            const state = segmentState(i, index)
            return <span key={i} data-state={state} className={cn('h-1.5 w-3.5 rounded-sm', SEGMENT[state])} />
          })}
        </span>
      )}
      {showLabel ? <span aria-hidden="true" className="min-w-0 truncate text-sm font-medium">{name}</span> : null}
    </span>
  )
}