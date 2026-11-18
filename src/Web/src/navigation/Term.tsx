import type { ReactNode } from 'react'
import { useTerm } from './vocabulary'

/** Componente de texto: <Term k="area.factory">Fábrica</Term>. Chave desconhecida mantém o termo padrão (CA 4). */
export function Term({ k, children }: { k: string; children: string }): ReactNode {
  return useTerm()(k, children)
}
