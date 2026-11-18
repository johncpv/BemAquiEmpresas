import { Outlet, useRouterState } from '@tanstack/react-router'
import { useState } from 'react'
import { FORM_BOUND_CLASS, PAGE_WIDTH_CLASS, usePageWidth } from '@/components/layout/page-width'
import { MessageRegion } from '@/components/ui/message-region'
import { AppHelp } from '@/help/AppHelp'
import { useHelp } from '@/help/help-context'
import { MOBILE, useMediaQuery } from '@/hooks/useMediaQuery'
import { areaOf, findModuleBySlug } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { CommandPalette } from './CommandPalette'
import { Dock } from './Dock'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'

/**
 * Shell do backoffice (/app, FND-09b): pular para o conteúdo, topbar, conteúdo e a navegação principal. No desktop e no
 * tablet, o menu lateral à esquerda, com a topbar e o conteúdo à direita; no celular, o dock na borda de baixo
 * (adendo de 18/11/2026 à ADR 0005 D8).
 */
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
  const mobile = useMediaQuery(MOBILE)
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const [, , slug, tabSlug] = pathname.split('/')
  const module = slug ? findModuleBySlug(all, slug) : undefined
  const area = module ? areaOf(areas, module) : undefined
  const tab = module && module.enabled && !module.locked ? module.tabs.find((t) => t.slug === tabSlug) : undefined
  const [palette, setPalette] = useState(false)
  const width = usePageWidth()

  const content = (
    <>
      <Topbar area={area} module={module && !module.locked && module.enabled ? module : undefined} tab={tab} onSearch={() => setPalette(true)} onHelp={() => help.setOpen(true)} />
      {/* O main é fluido; a largura vem do tipo da tela (staticData.width da rota, ADR 0044). */}
      <main id="conteudo" className="w-full px-4 py-6 sm:px-6 2xl:px-8">
        <div data-page-width={width} className={PAGE_WIDTH_CLASS[width]}>
          <MessageRegion />
          {width === 'form' ? <div className={FORM_BOUND_CLASS}><Outlet /></div> : <Outlet />}
        </div>
      </main>
    </>
  )

  return (
    // O espaço do dock (pb-28) só existe no celular; no desktop o menu fica ao lado do conteúdo.
    <div className={mobile ? 'min-h-dvh pb-28' : 'flex min-h-dvh'}>
      <a href="#conteudo" className="sr-only z-50 rounded-md bg-primary px-3 py-2 text-primary-foreground focus:not-sr-only focus:fixed focus:left-3 focus:top-3">
        Pular para o conteúdo
      </a>
      {mobile ? (
        <>
          {content}
          <Dock activeAreaId={area?.id} />
        </>
      ) : (
        <>
          <Sidebar activeAreaId={area?.id} activeModuleCode={module?.code} pathname={pathname} />
          <div className="min-w-0 flex-1">{content}</div>
        </>
      )}
      <CommandPalette open={palette} onOpenChange={setPalette} />
    </div>
  )
}
