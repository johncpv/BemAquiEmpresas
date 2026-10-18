import { Outlet, useRouterState } from '@tanstack/react-router'
import { useState } from 'react'
import { findModuleBySlug } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { CommandPalette } from './CommandPalette'
import { Dock } from './Dock'
import { Topbar } from './Topbar'

/** Shell do backoffice (/app): pular para o conteúdo, topbar, conteúdo e dock (FND-09b). */
export function AppShell() {
  const { areas } = useNavigation()
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const [, , slug, tabSlug] = pathname.split('/')
  const found = slug ? findModuleBySlug(areas, slug) : undefined
  const tab = found?.module.tabs.find((t) => t.slug === tabSlug)
  const [palette, setPalette] = useState(false)

  return (
    <div className="min-h-dvh pb-28">
      <a href="#conteudo" className="sr-only z-50 rounded-md bg-primary px-3 py-2 text-primary-foreground focus:not-sr-only focus:fixed focus:left-3 focus:top-3">
        Pular para o conteúdo
      </a>
      <Topbar area={found?.area} module={found?.module} tab={tab} onSearch={() => setPalette(true)} />
      <main id="conteudo" className="mx-auto max-w-6xl px-4 py-6 sm:px-6">
        <Outlet />
      </main>
      <Dock activeAreaId={found?.area.id} />
      <CommandPalette open={palette} onOpenChange={setPalette} />
    </div>
  )
}
