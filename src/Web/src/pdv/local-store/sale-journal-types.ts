/**
 * O que a tela de venda grava no aparelho (PDV-02), pela porta `LocalStore`:
 * - o **diário da venda em andamento**: cada ação (item lançado, quantidade, remoção, espera, descarte, cliente) é um evento gravado antes de a
 *   tela confirmar; reabrir o caixa depois de uma queda refaz a venda a partir dele (CA 18). Quando a venda termina (concluída na PDV-03, ou
 *   descartada), os eventos dela saem do diário e viram **uma** entrada da fila de saída, junto com o número da venda;
 * - o **documento do consumidor** (CPF ou CNPJ, PP-26): cifrado, num registro à parte, fora dos eventos e da fila; o evento leva só o id do
 *   registro. Pode ser apagado a qualquer hora.
 */
export interface SaleJournalEvent {
  /** Guid v7 do aparelho. */
  id: string
  saleId: string
  type: string
  /** Instante (ISO) da ação. */
  at: string
  data: unknown
}

/** CPF ou CNPJ cifrado (AES-GCM, chave do aparelho): nunca em claro no banco, no log, na URL ou na fila. */
export interface CustomerDocumentRecord {
  id: string
  saleId: string
  kind: 'cpf' | 'cnpj'
  /** Vetor de inicialização e texto cifrado, em base64. */
  iv: string
  cipher: string
  createdAt: string
}

/** A entrada da fila de saída que fecha a venda, montada com o número atribuído na hora (`{loja}-{caixa}-{seq}`). */
export interface ClosedSale {
  kind: string
  payload: unknown
  at: string
}
