import { useEffect } from 'react'
import { Skeleton } from '@/components/ui/states'

/**
 * /pdv dentro do backoffice (OFF-01 CA 1): o caixa é um app à parte, com build e service worker próprios em /pdv/. Quem chega aqui pela navegação
 * do backoffice (ou pelo /pdv sem a barra, no servidor de desenvolvimento) vai para lá com uma carga de página inteira, levando a query
 * (?registrar=1 da volta do login). Em produção o nginx já manda /pdv para /pdv/ antes de chegar ao backoffice.
 */
export function PdvAppRedirect() {
  const already = window.location.pathname.startsWith('/pdv/')
  useEffect(() => {
    if (!already) window.location.replace('/pdv/' + window.location.search)
  }, [already])

  if (already) {
    return (
      <main id="conteudo" className="mx-auto max-w-2xl p-4">
        <p>O caixa abre em <a className="font-semibold underline" href="/pdv/">/pdv/</a>.</p>
      </main>
    )
  }

  return <Skeleton className="m-4 h-40" />
}
