import { useRouterState } from '@tanstack/react-router'
import { useEffect, useRef, type ReactNode } from 'react'
import { useNavigation } from '@/navigation/navigation-context'

// Endereço da primeira carga: nela o foco fica no começo da página, para o primeiro Tab ser "Pular para o conteúdo".
let firstHref: string | undefined

interface PageHeadingProps {
  title: string
  documentTitle?: string
  count?: number
  /** Uma ou duas linhas sobre a tela, abaixo do título, com no máximo ~65 caracteres por linha (max-w-prose). */
  description?: ReactNode
  /** Ações da tela, à direita do título (no celular, abaixo dele). Um só primário. */
  actions?: ReactNode
  /** Chips de estado e dados curtos, logo abaixo do título. */
  meta?: ReactNode
  children?: ReactNode
}

/**
 * Cabeçalho da tela (o "PageHeader" do padrão de layout, ADR 0044). Título (FND-09b CA 11): document.title
 * "{Tela} · {Tenant} · BemAqui Empresas" e, na troca de rota, foco no h1 (o leitor de tela anuncia a tela nova). Na primeira
 * carga o foco não é movido (CA 12). Com <c>count</c> maior que zero, a aba mostra a contagem antes ("(4) Início", DSH-04 CA 7);
 * mudar a contagem não mexe no foco. `documentTitle`: nome da tela na aba quando o h1 é outro (ex.: a saudação do Painel do
 * proprietário, DSH-01a). `description`, `actions` e `meta` são opcionais; `children` continua para o que a tela já põe abaixo.
 */
export function PageHeading({ title, documentTitle, count, description, actions, meta, children }: PageHeadingProps) {
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

  const heading = (
    <h1 ref={ref} tabIndex={-1} className="text-[28px] font-bold tracking-tight outline-none">
      {title}
    </h1>
  )

  return (
    <div className="mb-5">
      {actions || meta ? (
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            {heading}
            {meta ? <div className="mt-2 flex flex-wrap items-center gap-2">{meta}</div> : null}
          </div>
          {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
        </div>
      ) : heading}
      {description ? <p className="mt-2 max-w-prose text-muted-foreground">{description}</p> : null}
      {children}
    </div>
  )
}
