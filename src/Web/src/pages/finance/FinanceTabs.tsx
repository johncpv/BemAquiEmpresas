import { Link } from '@tanstack/react-router'

const TABS = [
  { to: '/app/financeiro/contas', label: 'Contas e caixas' },
  { to: '/app/financeiro/condicoes-de-pagamento', label: 'Condições de pagamento' },
] as const

/** Abas do Financeiro (CAD-05b): cada aba é uma rota própria (FND-09b CA 8). */
export function FinanceTabs() {
  return (
    <nav aria-label="Financeiro" className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
