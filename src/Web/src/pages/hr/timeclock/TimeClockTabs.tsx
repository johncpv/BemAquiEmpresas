import { Link } from '@tanstack/react-router'

const TABS = [
  { to: '/app/rh/ponto/marcacoes', label: 'Marcações' },
  { to: '/app/rh/ponto/ajustes', label: 'Ajustes para aprovar' },
  { to: '/app/rh/ponto/espelho', label: 'Espelho' },
] as const

/**
 * Sub-abas do RH → Ponto (RH-01a, RH-01b e RH-01c): marcações, ajustes e o espelho; cada uma é uma rota própria, com a
 * própria ajuda. Ficam na página porque a barra do topo só tem a aba "Ponto". A faixa quebra linha quando não cabe (nenhuma aba escondida).
 */
export function TimeClockTabs() {
  return (
    <nav aria-label="Ponto" className="flex flex-wrap gap-1">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }}
          className="min-h-11 whitespace-nowrap rounded-md border border-border px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:bg-primary aria-[current=page]:text-primary-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
