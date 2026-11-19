import type { LocalStore } from '@/pdv/local-store/local-store'

/**
 * Caixa aberto neste aparelho (PDV-02 CA 17). **Marcador provisório até a CX-01**, que traz a abertura de verdade (fundo de troco, sangria,
 * suprimento, o evento da sessão de caixa e o fechamento com conferência). Aqui só se marca, no aparelho, que o operador abriu o caixa para
 * vender; fechar pede antes para retomar ou descartar as vendas em espera (CA 12).
 */
export const CASH_SESSION_META = 'cash.session'

export interface LocalCashSession {
  openedAt: string
  operatorId: string | null
}

export const loadCashSession = (store: LocalStore) => store.getMeta<LocalCashSession>(CASH_SESSION_META).then((s) => s ?? null)
export const openCashSession = (store: LocalStore, operatorId: string | null, now = new Date()) =>
  store.setMeta(CASH_SESSION_META, { openedAt: now.toISOString(), operatorId } satisfies LocalCashSession)
export const closeCashSession = (store: LocalStore) => store.setMeta(CASH_SESSION_META, null)
