import type { ReactNode } from 'react'
import { useGetTenantPublicContext } from '@/api/generated/bemaqui'
import { TenantMark } from '@/shell/TenantMark'
import { ThemeToggle } from '@/theme/ThemeToggle'

/** Telas sem dock (FND-04): marca do tenant (rota pública de contexto, ADR 0009) e o conteúdo num cartão. */
export function AuthLayout({ title, children }: { title: string; children: ReactNode }) {
  const tenant = useGetTenantPublicContext({ query: { retry: false, staleTime: Infinity } })
  const name = tenant.data?.name ?? 'BemAqui Empresas'
  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-6 px-4 py-10">
      <div className="flex items-center gap-2.5">
        <TenantMark name={name} logoUrl={tenant.data?.logoUrl} />
        <b className="font-display text-[15px] font-bold font-wider">{name}</b>
      </div>
      <main id="conteudo" className="w-full max-w-sm rounded-md border border-border bg-card p-6 shadow-sm">
        <h1 className="mb-4 text-[22px] font-bold">{title}</h1>
        {children}
      </main>
      <ThemeToggle />
    </div>
  )
}
