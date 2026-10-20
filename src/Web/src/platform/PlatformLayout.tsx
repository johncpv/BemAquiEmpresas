import { Link, Outlet, useNavigate } from '@tanstack/react-router'
import { platformLogout } from '@/api/generated/bemaqui'
import { MessageRegion } from '@/components/ui/message-region'
import { ThemeToggle } from '@/theme/ThemeToggle'

/** Superfície Plataforma (FND-13/03b): layout próprio, sem o dock do tenant. */
export function PlatformLayout() {
  const navigate = useNavigate()
  const link = 'rounded-sm px-2 py-1 text-sm font-medium text-muted-foreground hover:text-foreground aria-[current=page]:text-foreground aria-[current=page]:underline'
  return (
    <div className="min-h-dvh">
      <a href="#conteudo" className="sr-only z-50 rounded-md bg-primary px-3 py-2 text-primary-foreground focus:not-sr-only focus:fixed focus:left-3 focus:top-3">Pular para o conteúdo</a>
      <header className="sticky top-0 z-10 border-b border-border bg-background/90 backdrop-blur">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-3 px-4 py-3 sm:px-6">
          <span aria-hidden="true" className="grid size-[30px] place-items-center rounded-[7px] bg-rail font-display text-sm font-extrabold text-signal font-widest">B</span>
          <b className="font-display text-[15px] font-bold font-wider">BemAqui Plataforma</b>
          <nav aria-label="Plataforma" className="flex gap-1 border-l border-border pl-3">
            <Link to="/plataforma/tenants" className={link} activeProps={{ 'aria-current': 'page' }}>Tenants</Link>
            <a href="/hangfire" className={link}>Jobs</a>
          </nav>
          <div className="ml-auto flex items-center gap-2">
            <ThemeToggle />
            <button type="button" onClick={async () => { await platformLogout().catch(() => {}); await navigate({ to: '/plataforma/entrar' }) }} className="rounded-sm border border-border px-2.5 py-1 text-sm font-semibold">Sair</button>
          </div>
        </div>
      </header>
      <main id="conteudo" className="mx-auto max-w-6xl px-4 py-6 sm:px-6">
        <MessageRegion />
        <Outlet />
      </main>
    </div>
  )
}
