import { Link } from '@tanstack/react-router'

const TABS = [
  { to: '/app/producao/os', label: 'OS de fabricação' },
  { to: '/app/producao/kanban', label: 'Kanban' },
  { to: '/app/producao/apontamentos-abertos', label: 'Apontamentos abertos' },
  { to: '/app/producao/instalacoes', label: 'Instalações' },
  { to: '/app/producao/centros-de-trabalho', label: 'Centros de trabalho' },
  { to: '/app/producao/calendario', label: 'Calendário' },
  { to: '/app/producao/modelo-instalacao', label: 'Modelo da OS de instalação' },
] as const

/** Abas da Produção (CAD-03): cada aba é uma rota própria (FND-09b CA 8). */
export function ProductionTabs() {
  return (
    <nav aria-label="Produção" className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
