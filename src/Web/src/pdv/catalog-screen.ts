/**
 * Estados da tela inicial do /pdv quanto ao catálogo (OFF-01, "Telas e estados"). Local-first: com catálogo guardado a tela abre na hora e o
 * delta roda por trás; só a primeira abertura espera a foto.
 * - carregando: "Preparando o caixa: baixando produtos", com o progresso (só na primeira vez, sem catálogo guardado);
 * - vazio: "Nenhum produto disponível para esta loja." com o que fazer;
 * - erro de primeira carga sem rede: "A primeira abertura precisa de internet.";
 * - erro com rede (servidor fora, versão do catálogo nova): a mensagem e "Tentar de novo".
 */
export const CATALOG_LOADING_TITLE = 'Preparando o caixa: baixando produtos'
export const CATALOG_EMPTY_TITLE = 'Nenhum produto disponível para esta loja.'
export const CATALOG_EMPTY_HINT = 'Cadastre os produtos e o preço de venda na retaguarda (Catálogo de produtos) e confira se eles estão ativos para esta loja. Depois, toque em Conferir de novo.'
export const FIRST_OPEN_OFFLINE_TITLE = 'A primeira abertura precisa de internet.'
export const FIRST_OPEN_OFFLINE_HINT = 'Este caixa ainda não baixou os produtos da loja. Ligue o Wi-Fi ou o cabo de rede e toque em Tentar de novo. Depois da primeira vez, ele abre e vende sem internet.'

export type CatalogScreen =
  | { kind: 'ready' }
  | { kind: 'loading'; percent: number }
  | { kind: 'empty' }
  | { kind: 'offline-first' }
  | { kind: 'error'; message: string }

export interface CatalogScreenInput {
  /** Há fonte de catálogo ligada? (sem ela, a etapa não existe e a tela segue) */
  enabled: boolean
  /** Catálogo guardado no aparelho (null: nunca baixou). */
  itemCount: number | null
  online: boolean
  syncing: boolean
  /** 0 a 1, enquanto baixa a foto. */
  progress: number
  error: string | null
}

export function catalogScreen(input: CatalogScreenInput): CatalogScreen {
  if (!input.enabled) return { kind: 'ready' }
  if (input.itemCount !== null) return input.itemCount === 0 && !input.syncing ? { kind: 'empty' } : { kind: 'ready' }
  if (input.syncing) return { kind: 'loading', percent: Math.round(Math.min(Math.max(input.progress, 0), 1) * 100) }
  if (!input.online) return { kind: 'offline-first' }
  if (input.error) return { kind: 'error', message: input.error }
  return { kind: 'loading', percent: 0 }
}
