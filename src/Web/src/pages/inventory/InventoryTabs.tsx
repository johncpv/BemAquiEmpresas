import { Link } from '@tanstack/react-router'

const TABS = [{ to: '/app/estoque/locais', label: 'Locais' }] as const

/** Abas do Estoque (CAD-03): cada aba é uma rota própria (FND-09b CA 8). Itens entra com a CAD-02a. */
export function InventoryTabs() {
  return (
    <nav aria-label="Estoque" className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
