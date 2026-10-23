import { Link } from '@tanstack/react-router'
import { INVENTORY_GROUPS } from '@/navigation/modules'

/**
 * Abas do Estoque em dois grupos (padrão 9 da Fase 3): "Operação" (Saldos, Movimentos, Lançamentos pendentes) e "Cadastros"
 * (Itens, Locais, Unidades, Categorias, Garantia padrão). Cada aba é uma rota própria (FND-09b CA 8).
 */
export function InventoryTabs() {
  return (
    <nav aria-label="Estoque" className="mb-5 space-y-2 border-b border-border pb-1 sm:flex sm:gap-6 sm:space-y-0">
      {INVENTORY_GROUPS.map((g) => (
        <div key={g.label} role="group" aria-label={g.label} className="min-w-0">
          <p className="px-3 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{g.label}</p>
          <div className="flex gap-1 overflow-x-auto">
            {g.tabs.map((t) => (
              <Link key={t.slug} to={`/app/estoque/${t.slug}` as never} activeProps={{ 'aria-current': 'page' }} activeOptions={{ exact: false }}
                className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
                {t.label}
              </Link>
            ))}
          </div>
        </div>
      ))}
    </nav>
  )
}
