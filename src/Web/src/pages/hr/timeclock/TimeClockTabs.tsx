import { Link } from '@tanstack/react-router'
import { useScrollStrip } from '@/hooks/useScrollStrip'

const TABS = [
  { to: '/app/rh/ponto/marcacoes', label: 'Marcações' },
  { to: '/app/rh/ponto/ajustes', label: 'Ajustes para aprovar' },
  { to: '/app/rh/ponto/espelho', label: 'Espelho' },
] as const

/**
 * Sub-abas do RH → Ponto (RH-01a, RH-01b e RH-01c): marcações, ajustes e o espelho; cada uma é uma rota própria, com a
 * própria ajuda. Ficam na página porque a barra do topo só tem a aba "Ponto". Faixa sem barra de rolagem nativa.
 */
export function TimeClockTabs() {
  const strip = useScrollStrip<HTMLElement>()
  return (
    <nav ref={strip} aria-label="Ponto" className="scrollbar-none fade-x flex gap-1 overflow-x-auto">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }}
          className="min-h-11 shrink-0 whitespace-nowrap rounded-md border border-border px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:bg-primary aria-[current=page]:text-primary-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
