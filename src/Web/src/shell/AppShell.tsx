import { Outlet, useRouterState } from '@tanstack/react-router'
import { useState } from 'react'
import { MessageRegion } from '@/components/ui/message-region'
import { AppHelp } from '@/help/AppHelp'
import { useHelp } from '@/help/help-context'
import { areaOf, findModuleBySlug } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { CommandPalette } from './CommandPalette'
import { Dock } from './Dock'
import { Topbar } from './Topbar'

/** Shell do backoffice (/app): pular para o conteúdo, topbar, conteúdo e dock (FND-09b). */
export function AppShell() {
  return (
    <AppHelp>
      <Shell />
    </AppHelp>
  )
}

function Shell() {
  const help = useHelp()
  const { areas, all } = useNavigation()
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const [, , slug, tabSlug] = pathname.split('/')
  const module = slug ? findModuleBySlug(all, slug) : undefined
  const area = module ? areaOf(areas, module) : undefined
  const tab = module && module.enabled && !module.locked ? module.tabs.find((t) => t.slug === tabSlug) : undefined
  const [palette, setPalette] = useState(false)

  return (
    <div className="min-h-dvh pb-28">
      <a href="#conteudo" className="sr-only z-50 rounded-md bg-primary px-3 py-2 text-primary-foreground focus:not-sr-only focus:fixed focus:left-3 focus:top-3">
        Pular para o conteúdo
      </a>
      <Topbar area={area} module={module && !module.locked && module.enabled ? module : undefined} tab={tab} onSearch={() => setPalette(true)} onHelp={() => help.setOpen(true)} />
      <main id="conteudo" className="mx-auto max-w-6xl px-4 py-6 sm:px-6">
        <MessageRegion />
        <Outlet />
      </main>
      <Dock activeAreaId={area?.id} />
      <CommandPalette open={palette} onOpenChange={setPalette} />
    </div>
  )
}
