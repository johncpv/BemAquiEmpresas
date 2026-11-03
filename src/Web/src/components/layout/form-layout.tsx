import type { ReactNode } from 'react'

/**
 * Layout de tela de formulário (ADR 0044): coluna de 896 px (max-w-4xl) alinhada à esquerda, com o título. Com `aside`, a partir de
 * 1280 px (xl) ganha um painel de 320 px à direita (resumo, próximo passo, pendências), preso no topo ao rolar. Abaixo de xl, o
 * painel vai para depois do formulário; no DOM ele vem sempre depois, então o leitor de tela lê o formulário primeiro.
 */
export function FormLayout({ aside, asideLabel, children }: { aside?: ReactNode; asideLabel?: string; children: ReactNode }) {
  if (!aside) return <div className="max-w-4xl">{children}</div>
  return (
    <div data-form-layout="aside" className="grid gap-6 xl:grid-cols-[minmax(0,56rem)_20rem] xl:items-start">
      <div className="min-w-0 max-w-4xl">{children}</div>
      <aside aria-label={asideLabel} className="min-w-0 max-w-4xl space-y-4 xl:sticky xl:top-24">
        {aside}
      </aside>
    </div>
  )
}
