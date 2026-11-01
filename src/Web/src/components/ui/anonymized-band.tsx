import { formatInstantDate } from '@/lib/format'

/**
 * Faixa do cadastro anonimizado a pedido do titular (LGPD-01b): "Cadastro anonimizado em {data} (pedido {número})", no terceiro, no
 * funcionário e no usuário. A tela esconde as ações de edição; a API recusa com 409.
 */
export function AnonymizedBand({ at, request }: { at?: string | null; request?: string | null }) {
  if (!at) return null
  return (
    <p role="status" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">
      <b className="font-semibold">Cadastro anonimizado em {formatInstantDate(at)}{request ? ` (pedido ${request})` : ''}.</b>{' '}
      Os documentos com movimento continuam, com o marcador no lugar do dado pessoal. O cadastro não pode ser alterado.
    </p>
  )
}
