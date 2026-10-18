import { cva } from 'class-variance-authority'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

export type StatusTone = 'ok' | 'warn' | 'crit' | 'info' | 'neutral'

const chip = cva('inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-semibold', {
  variants: {
    tone: {
      ok: 'bg-ok-soft text-ok-ink',
      warn: 'bg-warn-soft text-warn-ink',
      crit: 'bg-crit-soft text-crit-ink',
      info: 'bg-info-soft text-info-ink',
      // Situação sem gravidade, como "Inativo" (FND-09d CA 18): o vermelho fica para o crítico.
      neutral: 'bg-muted text-muted-foreground',
    },
  },
})

const dot = { ok: 'bg-ok', warn: 'bg-warn', crit: 'bg-crit', info: 'bg-info', neutral: 'bg-muted-foreground' } as const

/** Chip de estado (FND-09a CA 13): cor semântica com rótulo de texto; o estado nunca é só cor. */
export function StatusChip({ tone, children, className }: { tone: StatusTone; children: ReactNode; className?: string }) {
  return (
    <span className={cn(chip({ tone }), className)} data-tone={tone}>
      <span aria-hidden="true" className={cn('size-1.5 rounded-full', dot[tone])} />
      {children}
    </span>
  )
}
