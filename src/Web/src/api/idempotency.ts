import { useCallback, useState } from 'react'

export const IDEMPOTENCY_HEADER = 'Idempotency-Key'

/**
 * Chave de idempotência por abertura de formulário ou diálogo (FND-09e CA 15, ADR 0019): o clique duplo e o reenvio
 * depois de um timeout mandam a mesma chave, e o servidor devolve o resultado original. Depois do sucesso, `renew`
 * gera outra chave para o próximo envio.
 */
export function useIdempotencyKey() {
  const [key, setKey] = useState(() => crypto.randomUUID())
  const renew = useCallback(() => setKey(crypto.randomUUID()), [])
  /** Opções do cliente gerado (segundo argumento das funções do Orval) com o cabeçalho. */
  const options = { headers: { [IDEMPOTENCY_HEADER]: key } }
  return { key, renew, options }
}
