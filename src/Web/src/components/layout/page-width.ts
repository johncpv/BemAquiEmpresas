import { useMatches } from '@tanstack/react-router'

/**
 * Largura da tela pelo tipo (ADR 0044, docs/ux/padrao-layout.md):
 * - `full`: quadro, Gantt, painel, DRE, fluxo de caixa e tabela com 8 ou mais colunas. Fluida até 1920 px;
 * - `default`: lista com filtros e detalhe com abas. Até 1536 px (`max-w-screen-2xl`), centralizada;
 * - `form`: cadastro e edição. O contêiner do `default`, com o conteúdo à esquerda e limitado à coluna do formulário
 *   (896 px) mais o painel lateral (320 px), que o `FormLayout` monta.
 */
export type PageWidth = 'full' | 'default' | 'form'

declare module '@tanstack/react-router' {
  interface StaticDataRouteOption {
    /** Tipo de tela; sem declarar, vale `default`. */
    width?: PageWidth
  }
}

/** Classe do contêiner da tela, aplicada pelo AppShell em volta do Outlet. */
export const PAGE_WIDTH_CLASS: Record<PageWidth, string> = {
  full: 'mx-auto w-full max-w-[1920px]',
  default: 'mx-auto w-full max-w-screen-2xl',
  form: 'mx-auto w-full max-w-screen-2xl',
}

/** Limite do conteúdo de uma tela `form` dentro do contêiner: 56rem do formulário + 1,5rem de espaço + 20rem do painel. */
export const FORM_BOUND_CLASS = 'max-w-[77.5rem]'

/** Largura da rota mais profunda que declara `staticData.width`; sem nenhuma, `default`. */
export function widthOfMatches(matches: readonly { staticData?: { width?: PageWidth } }[]): PageWidth {
  for (let i = matches.length - 1; i >= 0; i--) {
    const width = matches[i].staticData?.width
    if (width) return width
  }
  return 'default'
}

/** Largura da tela aberta (AppShell). */
export function usePageWidth(): PageWidth {
  return useMatches({ select: widthOfMatches })
}
