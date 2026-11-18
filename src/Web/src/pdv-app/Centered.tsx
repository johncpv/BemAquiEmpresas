import type { ReactNode } from 'react'

/** Moldura das telas de estado do caixa (navegador, outra janela, erro): centralizada, superfície "chão". */
export function Centered({ children }: { children: ReactNode }) {
  return <main id="conteudo" data-surface="floor" className="mx-auto flex min-h-dvh max-w-2xl items-center bg-background p-4 text-foreground"><div className="w-full">{children}</div></main>
}
