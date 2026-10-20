import { Link } from '@tanstack/react-router'
import { FINANCE_GROUPS } from '@/navigation/modules'

/**
 * Abas do Financeiro em dois grupos (FND-09e CA 12, D12): "Caixa e pagamentos" (operação) e "Configuração financeira".
 * Cada aba é uma rota própria (FND-09b CA 8); cada story acrescenta a sua no `modules.ts`.
 */
export function FinanceTabs() {
  return (
    <nav aria-label="Financeiro" className="mb-5 space-y-2 border-b border-border pb-1 sm:flex sm:gap-6 sm:space-y-0">
      {FINANCE_GROUPS.map((g) => (
        <div key={g.label} role="group" aria-label={g.label} className="min-w-0">
          <p className="px-3 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{g.label}</p>
          <div className="flex gap-1 overflow-x-auto">
            {g.tabs.map((t) => (
              <Link key={t.slug} to={`/app/financeiro/${t.slug}` as never} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
                {t.label}
              </Link>
            ))}
          </div>
        </div>
      ))}
    </nav>
  )
}
