import { formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'

interface MoneyProps {
  value: number
  /** false: arredonda para reais (painéis e KPIs). */
  cents?: boolean
  /** Negativo em vermelho crítico (ex.: saldo projetado). */
  highlightNegative?: boolean
  className?: string
}

/** Valor em R$ com tabular-nums (FND-09a CA 15). */
export function Money({ value, cents = true, highlightNegative = false, className }: MoneyProps) {
  return (
    <span className={cn('tabular-nums', highlightNegative && value < 0 && 'text-crit-ink', className)}>
      {formatMoney(value, cents)}
    </span>
  )
}
