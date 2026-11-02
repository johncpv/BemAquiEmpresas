import { Link } from '@tanstack/react-router'

const TABS = [
  { to: '/app/patrimonio/bens', label: 'Bens' },
  { to: '/app/patrimonio/manutencao', label: 'Manutenção' },
  { to: '/app/patrimonio/custo', label: 'Custo de manutenção' },
  { to: '/app/patrimonio/locais', label: 'Locais' },
] as const

/** Abas do Patrimônio (PAT-01a; Manutenção na PAT-03a): cada aba é uma rota própria (FND-09b CA 8). */
export function AssetsTabs() {
  return (
    <nav aria-label="Patrimônio" className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
