import { Link } from '@tanstack/react-router'

const TABS = [
  { to: '/app/configuracoes/seguranca', label: 'Segurança' },
  { to: '/app/configuracoes/ia', label: 'Inteligência artificial' },
] as const

/** Abas de Configurações: cada aba é uma rota própria (FND-09b CA 8). */
export function SettingsTabs() {
  return (
    <nav aria-label="Configurações" className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
      {TABS.map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
