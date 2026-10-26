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
  /** "tv": tamanho do Painel da TV (kit TV, PNL-01a CA 18), lido a metros de distância. */
  size?: 'default' | 'tv'
  className?: string
}

const SEGMENT = { done: 'bg-muted-foreground', now: 'bg-signal', next: 'bg-border' } as const

const SIZES = {
  default: { gap: 'gap-2', letter: 'h-[26px] min-w-[30px] rounded-[5px] px-1.5 text-[13px]', segment: 'h-1.5 w-3.5', segments: 'gap-0.5', label: 'text-sm', compact: 'text-xs' },
  tv: { gap: 'gap-4', letter: 'h-[1.6em] min-w-[1.8em] rounded-[8px] px-2 text-[length:var(--tv-line)]', segment: 'h-3 w-8', segments: 'gap-1', label: 'text-[length:var(--tv-line)]', compact: 'text-[length:var(--tv-line)]' },
} as const

/**
 * Indicador de etapa no estilo display de andar (CLAUDE.md, FND-09a CA 14, ENG-01 CA 14): letra da etapa em --signal sobre
 * o trilho escuro e um segmento por etapa; só a atual fica em âmbar. Acima de 8 etapas mostra "letra · posição/total" para
 * caber a 400 px. O nome e a posição vão no rótulo acessível.
 */
export function StageIndicator({ stages, current, letters, showLabel, size = 'default', className }: StageIndicatorProps) {
  const total = stages.length
  if (total === 0) return null
  const index = Math.min(Math.max(current, 0), total - 1)
  const name = stages[index] ?? ''
  const letter = (letters?.[index] || name.trim().charAt(0)).toUpperCase()
  const compact = total > STAGE_COMPACT_ABOVE
  const s = SIZES[size]
  return (
    <span role="img" aria-label={`Etapa ${name}, ${index + 1} de ${total}`} data-size={size} className={cn('inline-flex min-w-0 max-w-full items-center', s.gap, className)}>
      <span className={cn('grid shrink-0 place-items-center bg-rail font-display font-extrabold tracking-wider text-signal font-wider', s.letter)}>
        {letter}
      </span>
      {compact ? (
        <span aria-hidden="true" data-compact="" className={cn('shrink-0 whitespace-nowrap font-semibold tabular-nums text-muted-foreground', s.compact)}>· {index + 1}/{total}</span>
      ) : (
        <span aria-hidden="true" className={cn('flex shrink-0', s.segments)}>
          {stages.map((_, i) => {
            const state = segmentState(i, index)
            return <span key={i} data-state={state} className={cn('rounded-sm', s.segment, SEGMENT[state])} />
          })}
        </span>
      )}
      {showLabel ? <span aria-hidden="true" className={cn('min-w-0 truncate font-medium', s.label)}>{name}</span> : null}
    </span>
  )
}