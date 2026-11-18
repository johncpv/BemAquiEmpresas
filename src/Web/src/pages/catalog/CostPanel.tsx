import type { ProductCostView } from '@/api/generated/model'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { DICAS, formatPercent, marginTone } from './catalog'

/**
 * Painel de custo e margem (CAT-01 CA 11): custo médio do item (pelo Estoque), margem e markup sobre o preço vigente e o preço sugerido pela
 * margem padrão da categoria. Só aparece com catalog.view_cost: sem ela, a API nem manda o custo (CA 10).
 */
export function CostPanel({ cost, price, onUseSuggested }: { cost: ProductCostView; price: number | null | undefined; onUseSuggested?: (price: number) => void }) {
  const tone = marginTone(cost.marginPercent ?? null)
  return (
    <aside aria-labelledby="custo-margem" className="space-y-3 rounded-md border border-border bg-card p-4">
      <h2 id="custo-margem" className="flex items-center text-base font-semibold">Custo e margem<Dica nome="Margem" texto={DICAS.margem} /></h2>
      <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
        <dt className="text-muted-foreground">Custo médio</dt>
        <dd className="text-right tabular-nums">{cost.averageCost > 0 ? formatMoney(cost.averageCost) : 'Sem custo ainda'}</dd>
        <dt className="text-muted-foreground">Preço</dt>
        <dd className="text-right tabular-nums">{price == null ? 'Sem preço' : formatMoney(price)}</dd>
        <dt className="text-muted-foreground">Margem</dt>
        <dd className="text-right"><StatusChip tone={tone}>{formatPercent(cost.marginPercent)}</StatusChip></dd>
        <dt className="text-muted-foreground">Markup</dt>
        <dd className="text-right tabular-nums">{formatPercent(cost.markupPercent)}</dd>
      </dl>
      {cost.averageCost <= 0 ? <p className="text-xs text-muted-foreground">O custo vem das entradas no estoque (compra ou ajuste). Sem entrada, a margem não tem base.</p> : null}
      {tone === 'crit' ? <p role="status" className="text-xs font-semibold text-crit-ink">O preço está abaixo do custo.</p> : null}
      {cost.suggestedPrice != null ? (
        <p className="text-sm">
          Pela margem da categoria: <b className="tabular-nums">{formatMoney(cost.suggestedPrice)}</b>
          {onUseSuggested && cost.suggestedPrice !== price ? (
            <button type="button" onClick={() => onUseSuggested(cost.suggestedPrice!)} className="ml-2 text-sm font-semibold text-primary">Usar este preço</button>
          ) : null}
        </p>
      ) : null}
    </aside>
  )
}
