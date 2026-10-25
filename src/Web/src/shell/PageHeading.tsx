import { useRouterState } from '@tanstack/react-router'
import { useEffect, useRef, type ReactNode } from 'react'
import { useNavigation } from '@/navigation/navigation-context'

// Endereço da primeira carga: nela o foco fica no começo da página, para o primeiro Tab ser "Pular para o conteúdo".
let firstHref: string | undefined

/**
 * Título da tela (FND-09b CA 11): document.title "{Tela} · {Tenant} · BemAqui Empresas" e, na troca de rota, foco no
 * h1 (o leitor de tela anuncia a tela nova). Na primeira carga o foco não é movido (CA 12). Com <c>count</c> maior que zero, a aba
 * mostra a contagem antes ("(4) Início", DSH-04 CA 7); mudar a contagem não mexe no foco. `documentTitle`: nome da tela na aba
 * quando o h1 é outro (ex.: a saudação do Painel do proprietário, DSH-01a).
 */
export function PageHeading({ title, documentTitle, count, children }: { title: string; documentTitle?: string; count?: number; children?: ReactNode }) {
  const { tenantName } = useNavigation()
  const href = useRouterState({ select: (s) => s.location.href })
  const ref = useRef<HTMLHeadingElement>(null)
  const tabTitle = documentTitle ?? title

  useEffect(() => {
    document.title = `${count ? `(${count}) ` : ''}${tabTitle} · ${tenantName} · BemAqui Empresas`
  }, [tabTitle, tenantName, count])

  useEffect(() => {
    firstHref ??= href
    if (href !== firstHref) ref.current?.focus()
  }, [href])

  return (
    <div className="mb-5">
      <h1 ref={ref} tabIndex={-1} className="text-[28px] font-bold tracking-tight outline-none">
        {title}
      </h1>
      {children}
    </div>
  )
}
