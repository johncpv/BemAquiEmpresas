import { Link } from '@tanstack/react-router'

const TABS = [
  { to: '/app/rh/funcionarios', label: 'Funcionários' },
  { to: '/app/rh/cargos', label: 'Cargos' },
  { to: '/app/rh/jornadas', label: 'Jornadas' },
  { to: '/app/rh/sindicatos', label: 'Sindicatos' },
] as const

/** Abas do RH (CAD-04a): cada aba é uma rota própria (FND-09b CA 8). */
export function HrTabs() {
  return (
    <nav aria-label="RH" className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
