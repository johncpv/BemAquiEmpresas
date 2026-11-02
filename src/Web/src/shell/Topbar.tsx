import { Link } from '@tanstack/react-router'
import { CircleHelp, Search } from 'lucide-react'
import { MOBILE, useMediaQuery } from '@/hooks/useMediaQuery'
import { useScrollStrip } from '@/hooks/useScrollStrip'
import type { NavArea, NavModule, Tab } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { ThemeToggle } from '@/theme/ThemeToggle'
import { NotificationsBell } from './NotificationsBell'
import { UserMenu } from './UserMenu'
import { TenantMark } from './TenantMark'

interface TopbarProps {
  area?: NavArea
  module?: NavModule
  tab?: Tab
  onSearch: () => void
  onHelp?: () => void
}

/**
 * Topbar (FND-09b CA 6 a 8): marca do tenant, trilha, busca com Ctrl K, Ajuda (slot da FND-14), sino, tema e usuário.
 * A 400px a busca vira ícone e o tema vai para o menu do usuário. Sub-telas viram abas com rota própria, só aqui:
 * a página não repete as abas do módulo.
 */
export function Topbar({ area, module, tab, onSearch, onHelp }: TopbarProps) {
  const { tenantName, tenantLogo } = useNavigation()
  const mobile = useMediaQuery(MOBILE)
  const iconButton = 'grid size-9 place-items-center rounded-sm border border-border bg-card'

  return (
    <header className="sticky top-0 z-10 border-b border-border bg-background/90 backdrop-blur">
      <div className="flex items-center gap-3 px-4 py-3 sm:gap-3.5 sm:px-6">
        <Link to="/app" className="flex items-center gap-2.5">
          <TenantMark name={tenantName} logoUrl={tenantLogo} />
          <b className={mobile ? 'sr-only' : 'font-display text-[15px] font-bold font-wider'}>{tenantName}</b>
        </Link>

        <nav aria-label="Trilha" className="min-w-0 truncate border-l border-border pl-3.5 text-[13px] text-muted-foreground">
          {area ? <span>{area.name}</span> : <b className="font-semibold text-foreground">Início</b>}
          {module ? <> › <b className="font-semibold text-foreground">{module.name}</b></> : null}
          {tab ? <> › {tab.label}</> : null}
        </nav>

        <div className="ml-auto flex items-center gap-2">
          {mobile ? (
            <button type="button" className={iconButton} onClick={onSearch} aria-label="Buscar tela (Ctrl K)">
              <Search aria-hidden="true" className="size-[18px]" strokeWidth={1.7} />
            </button>
          ) : (
            <button type="button" onClick={onSearch} className="flex w-[min(320px,40vw)] items-center gap-2 rounded-sm border border-border bg-card px-2.5 py-[7px] text-muted-foreground">
              <Search aria-hidden="true" className="size-4" strokeWidth={1.7} />
              <span className="flex-1 text-left text-sm">Buscar tela ou módulo</span>
              <kbd className="rounded border border-border px-1.5 text-[11px]">Ctrl K</kbd>
            </button>
          )}
          <button type="button" className={iconButton} onClick={onHelp} aria-label="Ajuda desta tela">
            <CircleHelp aria-hidden="true" className="size-[18px]" strokeWidth={1.7} />
          </button>
          <NotificationsBell />
          {mobile ? null : <ThemeToggle />}
          <UserMenu showTheme={mobile} />
        </div>
      </div>

      {module && module.tabs.length > 0 ? <ModuleTabs module={module} current={tab?.slug} /> : null}
    </header>
  )
}

/** Abas seguidas do mesmo grupo viram um bloco; aba sem grupo fica num bloco sem rótulo. */
function groupTabs(tabs: readonly Tab[]) {
  const groups: { label?: string; tabs: Tab[] }[] = []
  for (const t of tabs) {
    const last = groups.at(-1)
    if (last && last.label === t.group) last.tabs.push(t)
    else groups.push({ label: t.group, tabs: [t] })
  }
  return groups
}

/**
 * A única navegação das sub-telas do módulo (cada aba é uma rota própria, FND-09b CA 8). Quando o módulo agrupa as
 * abas (FND-09e CA 12, D12: "Caixa e pagamentos" e "Configuração financeira"; Estoque: "Operação" e "Cadastros"),
 * cada grupo ganha um rótulo discreto e um separador. A faixa rola na horizontal sem barra nativa, com a borda
 * esmaecida quando há mais abas daquele lado, e começa com a aba ativa à vista.
 */
function ModuleTabs({ module, current }: { module: NavModule; current?: string }) {
  // A aba ativa sempre à vista ao carregar e a cada troca de tela (a faixa rola sem barra nativa).
  const strip = useScrollStrip<HTMLElement>(current)
  return (
    <nav ref={strip} aria-label={`Telas de ${module.name}`} className="scrollbar-none fade-x flex items-end overflow-x-auto px-4 sm:px-6">
      {groupTabs(module.tabs).map((g, i) => {
        const links = g.tabs.map((t) => (
          <Link
            key={t.slug}
            to="/app/$module/$tab"
            params={{ module: module.slug, tab: t.slug }}
            aria-current={t.slug === current ? 'page' : undefined}
            className="shrink-0 whitespace-nowrap border-b-2 border-transparent px-3 pb-2 pt-1 text-sm font-medium text-muted-foreground hover:text-foreground aria-[current=page]:border-signal aria-[current=page]:text-foreground"
          >
            {t.label}
          </Link>
        ))
        return (
          <div key={`${i}-${g.label ?? ''}`} role={g.label ? 'group' : undefined} aria-label={g.label} className="flex shrink-0 items-end">
            {i > 0 ? <span aria-hidden="true" className="mx-2 mb-2.5 h-4 w-px shrink-0 bg-border" /> : null}
            {g.label ? (
              <span aria-hidden="true" className="shrink-0 whitespace-nowrap pb-2.5 pl-1 pr-1.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                {g.label}
              </span>
            ) : null}
            {links}
          </div>
        )
      })}
    </nav>
  )
}
