import { Link } from '@tanstack/react-router'
import { useCan } from '@/pages/use-can'

const TABS = [
  { to: '/app/rh/funcionarios', label: 'Funcionários' },
  { to: '/app/rh/cargos', label: 'Cargos' },
  { to: '/app/rh/jornadas', label: 'Jornadas' },
  { to: '/app/rh/sindicatos', label: 'Sindicatos' },
  { to: '/app/rh/ponto/marcacoes', label: 'Ponto' },
  { to: '/app/rh/ferias', label: 'Férias' },
  { to: '/app/rh/folha', label: 'Folha' },
  { to: '/app/rh/rubricas', label: 'Rubricas' },
  { to: '/app/rh/tabelas-folha', label: 'Tabelas da folha' },
] as const

/** Abas do RH (CAD-04a): cada aba é uma rota própria (FND-09b CA 8). "Exportar para a contadora" só com hr.view_sensitive (RH-03 CA 8). */
export function HrTabs() {
  const canSensitive = useCan('hr.view_sensitive')
  const tabs = canSensitive ? [...TABS, { to: '/app/rh/exportar-contadora', label: 'Exportar para a contadora' } as const] : TABS
  return (
    <nav aria-label="RH" className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
      {tabs.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
