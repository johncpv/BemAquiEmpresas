import { Link } from '@tanstack/react-router'

const TABS = [
  { to: '/app/rh/ponto/marcacoes', label: 'Marcações' },
  { to: '/app/rh/ponto/ajustes', label: 'Ajustes para aprovar' },
] as const

/** Abas do RH → Ponto (RH-01a e RH-01b): marcações e ajustes; cada aba é uma rota própria, com a própria ajuda. */
export function TimeClockTabs() {
  return (
    <nav aria-label="Ponto" className="flex gap-1 overflow-x-auto">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }}
          className="min-h-11 whitespace-nowrap rounded-md border border-border px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:bg-primary aria-[current=page]:text-primary-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
