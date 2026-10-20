import type { QuoteCostDetail, QuoteDetail } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'

const pct = (v: number) => `${v.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`

/** Rodapé fixo de totais (CA 15): custo, preço, margem e modo; sem quotes.view_cost, só o preço. */
export function QuoteTotals({ quote, costs }: { quote: QuoteDetail; costs?: QuoteCostDetail }) {
  const margin = costs?.marginPct
  return (
    <div aria-live="polite" className="sticky bottom-20 z-10 mt-4 flex flex-wrap items-center gap-x-6 gap-y-1 rounded-md border border-border bg-card px-4 py-2 text-sm shadow-sm">
      {costs ? <span>Custo total <Money value={costs.totalCost} className="font-semibold" /></span> : null}
      <span>Preço total <Money value={quote.totalPrice} className="font-semibold" /></span>
      {costs ? <span>Margem <b className="tabular-nums">{margin === null || margin === undefined ? '—' : pct(margin)}</b></span> : null}
      {costs ? <span className="text-muted-foreground">{costs.pricingMode === 'Markup' ? 'Markup sobre o custo' : 'Margem sobre o preço'}</span> : null}
      {margin !== null && margin !== undefined && margin < 0 ? <StatusChip tone="crit">Preço abaixo do custo</StatusChip> : null}
    </div>
  )
}
