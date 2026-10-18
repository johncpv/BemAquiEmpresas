import { Link } from '@tanstack/react-router'
import { CircleHelp, Search } from 'lucide-react'
import { MOBILE, useMediaQuery } from '@/hooks/useMediaQuery'
import type { NavArea, NavModule, Tab } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { ThemeToggle } from '@/theme/ThemeToggle'
import { NotificationsBell } from './NotificationsBell'
import { UserMenu } from './UserMenu'

interface TopbarProps {
  area?: NavArea
  module?: NavModule
  tab?: Tab
  onSearch: () => void
  onHelp?: () => void
}

/**
 * Topbar (FND-09b CA 6 a 8): marca do tenant, trilha, busca com Ctrl K, Ajuda (slot da FND-14), sino, tema e usuário.
 * A 400px a busca vira ícone e o tema vai para o menu do usuário. Sub-telas viram abas com rota própria.
 */
export function Topbar({ area, module, tab, onSearch, onHelp }: TopbarProps) {
  const { tenantName } = useNavigation()
  const mobile = useMediaQuery(MOBILE)
  const iconButton = 'grid size-9 place-items-center rounded-sm border border-border bg-card'

  return (
    <header className="sticky top-0 z-10 border-b border-border bg-background/90 backdrop-blur">
      <div className="flex items-center gap-3 px-4 py-3 sm:gap-3.5 sm:px-6">
        <Link to="/app" className="flex items-center gap-2.5">
          <span aria-hidden="true" className="grid size-[34px] place-items-center rounded-[8px] bg-signal font-display text-[15px] font-extrabold text-signal-foreground font-widest">
            {tenantName.charAt(0).toUpperCase()}
          </span>
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
          <UserMenu name="Usuário" showTheme={mobile} />
        </div>
      </div>

      {module && module.tabs.length > 0 ? (
        <nav aria-label={`Telas de ${module.name}`} className="flex gap-1 overflow-x-auto px-4 sm:px-6">
          {module.tabs.map((t) => (
            <Link
              key={t.slug}
              to="/app/$module/$tab"
              params={{ module: module.slug, tab: t.slug }}
              aria-current={t.slug === tab?.slug ? 'page' : undefined}
              className="border-b-2 border-transparent px-3 pb-2 pt-1 text-sm font-medium text-muted-foreground aria-[current=page]:border-signal aria-[current=page]:text-foreground"
            >
              {t.label}
            </Link>
          ))}
        </nav>
      ) : null}
    </header>
  )
}
