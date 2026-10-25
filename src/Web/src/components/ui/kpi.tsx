import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

export interface KpiMeter {
  /** Valor atual e máximo do medidor (ex.: margem projetada sobre a orçada). */
  value: number
  max: number
  /** Texto do medidor para o leitor de tela ("82% da margem orçada"). */
  label: string
  /** Tom semântico do preenchimento: o estado nunca é só cor (o texto vai em `label` e na linha de contexto). */
  tone?: 'ok' | 'warn' | 'crit' | 'info'
}

interface KpiProps {
  label: string
  value: ReactNode
  /** Linha de contexto ("−3,6 pp abaixo dos 23,0% orçados"). */
  context?: ReactNode
  meter?: KpiMeter
  /** Número clicável até a origem (padrão 2 das Fases 4 a 8): abre o drill-down ou a tela de origem. */
  onClick?: () => void
  /** Nome acessível do clique ("Ver origem do realizado"). */
  actionLabel?: string
  className?: string
}

const FILL = { ok: 'bg-ok', warn: 'bg-warn', crit: 'bg-crit', info: 'bg-info' } as const

/**
 * Indicador (DSH-02 CA 16, D18): rótulo, valor com tabular-nums, linha de contexto e medidor opcional; clicável até a origem. Só
 * tokens de cor. O relatório "Orçado × realizado" é a primeira tela que o usa; o painel do dono (DSH-01a) usa o mesmo.
 */
export function Kpi({ label, value, context, meter, onClick, actionLabel, className }: KpiProps) {
  const pct = meter && meter.max > 0 ? Math.max(0, Math.min(100, (meter.value / meter.max) * 100)) : 0
  const body = (
    <>
      <span className="block text-xs font-medium text-muted-foreground">{label}</span>
      <span className="mt-1 block font-display text-2xl font-bold tabular-nums font-wider">{value}</span>
      {context ? <span className="mt-1 block text-xs text-muted-foreground">{context}</span> : null}
      {meter ? (
        <span role="meter" aria-label={meter.label} aria-valuemin={0} aria-valuemax={meter.max} aria-valuenow={meter.value} className="mt-2 block h-1.5 overflow-hidden rounded-full bg-muted">
          <span className={cn('block h-full rounded-full', FILL[meter.tone ?? 'info'])} style={{ width: `${pct}%` }} />
        </span>
      ) : null}
    </>
  )
  const box = cn('block w-full rounded-md border border-border bg-card p-3 text-left', className)
  return onClick ? (
    <button type="button" onClick={onClick} className={cn(box, 'hover:border-primary focus-visible:outline-2 focus-visible:outline-primary')}>
      {body}{actionLabel ? <span className="sr-only"> ({actionLabel})</span> : null}
    </button>
  ) : (
    <div className={box}>{body}</div>
  )
}
