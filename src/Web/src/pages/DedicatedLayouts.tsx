import { Outlet } from '@tanstack/react-router'

/** PWA do operador (/fab): layout próprio, sem dock nem topbar, alvos grandes (esqueleto; Fase 3). */
export function ShopfloorLayout() {
  return (
    <main id="conteudo" className="min-h-dvh bg-background p-4 text-base">
      <h1 className="text-2xl font-bold">App do operador</h1>
      <p className="mt-2 text-muted-foreground">A fila de etapas chega na Fase 3.</p>
      <Outlet />
    </main>
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
