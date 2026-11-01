import { Outlet, useRouterState } from '@tanstack/react-router'
import { useEffect } from 'react'
import { FloorMessages } from '@/components/shopfloor/kit'
import { FloorHelp } from '@/help/FloorHelp'
import { applyDeviceTheme, useFloorDevice } from '@/pages/shopfloor/floor-session'
import { IdleGuard } from '@/pages/shopfloor/FloorSession'
import { OfflineGate } from '@/pages/shopfloor/OfflineGate'
import { registerFloorServiceWorker } from '@/pwa/register'
import { TIME_CLOCK_OFFLINE } from './hr/timeclock/time-clock'
import { ModuleGate, ReleasedGate } from './ModuleGate'

/**
 * App do operador (/fab, PRD-03a): layout próprio, sem dock nem topbar do backoffice, com o kit "chão de fábrica". As três
 * camadas valem como no backoffice (módulo shopfloor liberado e no grupo); o cabeçalho de cada tela traz o botão "Ajuda". A
 * qualidade no tablet (/fab/qualidade, PRD-05) é do módulo production: o inspetor não precisa do app do operador. PRD-03b: tema do
 * aparelho registrado, saída por inatividade na sessão por PIN, "Sem conexão" sem rede e o service worker só do shell do /fab.
 */
export function ShopfloorLayout() {
  const quality = useRouterState({ select: (s) => s.location.pathname.startsWith('/fab/qualidade') })
  // PAT-01b e PAT-03a: a ficha do bem pela leitura da etiqueta (/fab/bens) e a OS de manutenção no celular (/fab/manutencao) são do módulo
  // assets, não do app do operador.
  const asset = useRouterState({ select: (s) => s.location.pathname.startsWith('/fab/bens') || s.location.pathname.startsWith('/fab/manutencao') })
  // RH-01a: "Meu ponto" (/fab/ponto) é autoatendimento do RH (ADR 0035): basta o módulo hr liberado; o vínculo vem da API.
  const timeClock = useRouterState({ select: (s) => s.location.pathname.startsWith('/fab/ponto') })
  const device = useFloorDevice()
  useEffect(() => applyDeviceTheme(device.data), [device.data])
  useEffect(() => registerFloorServiceWorker(), [])
  return (
    <FloorHelp>
      <main id="conteudo" className="min-h-dvh bg-background pb-8 text-[18px] text-foreground">
        {timeClock ? (
          <ReleasedGate code="hr">
            <FloorMessages />
            <Outlet />
          </ReleasedGate>
        ) : (
          <ModuleGate code={asset ? 'assets' : quality ? 'production' : 'shopfloor'}>
            <FloorMessages />
            <Outlet />
          </ModuleGate>
        )}
      </main>
      <IdleGuard />
      <OfflineGate message={timeClock ? TIME_CLOCK_OFFLINE : undefined} />
    </FloorHelp>
  )
}

/**
 * Painel da TV (/tv, PNL-01a CA 5 e 18, ADR 0030): sempre escuro (ignora o tema do perfil), sem dock nem barra do backoffice, letra de
 * leitura a distância e nenhum Referer (o link da tela leva o token no fragmento). Uma coluna a 400 px; não é pensado para celular.
 */
export function TvLayout() {
  return (
    <main id="conteudo" data-surface="tv" className="theme-tv min-h-dvh overflow-x-hidden p-4 sm:p-8">
      <meta name="referrer" content="no-referrer" />
      <Outlet />
    </main>
  )
}