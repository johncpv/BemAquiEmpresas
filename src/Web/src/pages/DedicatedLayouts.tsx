import { Outlet, useRouterState } from '@tanstack/react-router'
import { useEffect } from 'react'
import { FloorMessages } from '@/components/shopfloor/kit'
import { FloorHelp } from '@/help/FloorHelp'
import { applyDeviceTheme, useFloorDevice } from '@/pages/shopfloor/floor-session'
import { IdleGuard } from '@/pages/shopfloor/FloorSession'
import { OfflineGate } from '@/pages/shopfloor/OfflineGate'
import { registerFloorServiceWorker } from '@/pwa/register'
import { ModuleGate } from './ModuleGate'

/**
 * App do operador (/fab, PRD-03a): layout próprio, sem dock nem topbar do backoffice, com o kit "chão de fábrica". As três
 * camadas valem como no backoffice (módulo shopfloor liberado e no grupo); o cabeçalho de cada tela traz o botão "Ajuda". A
 * qualidade no tablet (/fab/qualidade, PRD-05) é do módulo production: o inspetor não precisa do app do operador. PRD-03b: tema do
 * aparelho registrado, saída por inatividade na sessão por PIN, "Sem conexão" sem rede e o service worker só do shell do /fab.
 */
export function ShopfloorLayout() {
  const quality = useRouterState({ select: (s) => s.location.pathname.startsWith('/fab/qualidade') })
  const device = useFloorDevice()
  useEffect(() => applyDeviceTheme(device.data), [device.data])
  useEffect(() => registerFloorServiceWorker(), [])
  return (
    <FloorHelp>
      <main id="conteudo" className="min-h-dvh bg-background pb-8 text-[18px] text-foreground">
        <ModuleGate code={quality ? 'production' : 'shopfloor'}>
          <FloorMessages />
          <Outlet />
        </ModuleGate>
      </main>
      <IdleGuard />
      <OfflineGate />
    </FloorHelp>
  )
}

/** Painel da TV (/tv/{token}): sempre escuro, fonte grande, sem login (esqueleto; Fase 4). */
export function TvLayout() {
  return (
    <main id="conteudo" className="theme-tv min-h-dvh p-8">
      <h1 className="text-[30px] font-extrabold font-wider">Painel da fábrica</h1>
      <p className="mt-2 text-muted-foreground">Os painéis chegam na Fase 4.</p>
      <Outlet />
    </main>
  )
}