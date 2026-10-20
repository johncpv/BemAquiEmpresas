import type { ReactNode } from 'react'
import { MessageRegion } from '@/components/ui/message-region'

/** Telas públicas da plataforma: marca do BemAqui (não há tenant) e um cartão. */
export function PlatformCard({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-6 px-4 py-10">
      <div className="flex items-center gap-2.5">
        <span aria-hidden="true" className="grid size-[34px] place-items-center rounded-[8px] bg-rail font-display text-[15px] font-extrabold text-signal font-widest">B</span>
        <b className="font-display text-[15px] font-bold font-wider">BemAqui Plataforma</b>
      </div>
      <div className="w-full max-w-sm"><MessageRegion /></div>
      <main id="conteudo" className="w-full max-w-sm rounded-md border border-border bg-card p-6 shadow-sm">
        <h1 className="mb-4 text-[22px] font-bold">{title}</h1>
        {children}
      </main>
    </div>
  )
}
