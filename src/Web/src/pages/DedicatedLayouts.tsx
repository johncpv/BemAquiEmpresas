import { Outlet } from '@tanstack/react-router'
import { FloorMessages } from '@/components/shopfloor/kit'
import { FloorHelp } from '@/help/FloorHelp'
import { ModuleGate } from './ModuleGate'

/**
 * App do operador (/fab, PRD-03a): layout próprio, sem dock nem topbar do backoffice, com o kit "chão de fábrica". As três
 * camadas valem como no backoffice (módulo shopfloor liberado e no grupo); o cabeçalho de cada tela traz o botão "Ajuda".
 */
export function ShopfloorLayout() {
  return (
    <FloorHelp>
      <main id="conteudo" className="min-h-dvh bg-background pb-8 text-[18px] text-foreground">
        <ModuleGate code="shopfloor">
          <FloorMessages />
          <Outlet />
        </ModuleGate>
      </main>
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