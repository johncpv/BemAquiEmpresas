import type { HelpDocument } from '@/api/generated/model'

/** Padrão da rota do TanStack Router ("/app/" vira "/app") para casar com o cabeçalho "rota" da ajuda. */
export function normalizeRoute(routeId: string): string {
  return routeId.length > 1 && routeId.endsWith('/') ? routeId.slice(0, -1) : routeId
}

/**
 * Ajuda da tela (FND-14 CA 4): mesma rota e, se a tela informou a aba, a ajuda dessa aba; sem aba, a de padrao: true
 * da rota (padrão NSHome).
 */
export function resolveHelp(docs: readonly HelpDocument[], route: string, tab?: string | null): HelpDocument | undefined {
  const sameRoute = docs.filter((d) => d.route === route)
  return (tab ? sameRoute.find((d) => d.tab === tab) : undefined) ?? sameRoute.find((d) => d.isDefault) ?? sameRoute.find((d) => !d.tab) ?? sameRoute[0]
}

export interface HelpHit {
  doc: HelpDocument
  section: string
  excerpt: string
}

/** Busca (CA 5): seções que contêm o termo, com a tela atual primeiro. */
export function searchHelp(docs: readonly HelpDocument[], term: string, currentKey?: string): HelpHit[] {
  const needle = term.trim().toLocaleLowerCase('pt-BR')
  if (!needle) return []
  const hits = docs.flatMap((doc) =>
    doc.sections
      .filter((s) => `${doc.title} ${s.title} ${s.body}`.toLocaleLowerCase('pt-BR').includes(needle))
      .map((s) => {
        const at = s.body.toLocaleLowerCase('pt-BR').indexOf(needle)
        return { doc, section: s.title, excerpt: at < 0 ? s.body.slice(0, 120) : s.body.slice(Math.max(0, at - 40), at + 80) }
      }),
  )
  return hits.sort((a, b) => Number(b.doc.key === currentKey) - Number(a.doc.key === currentKey))
}

/** Perguntas das "Perguntas frequentes" da tela atual, para começar sem digitar (propositivo). */
export function suggestedQuestions(doc?: HelpDocument): string[] {
  const faq = doc?.sections.find((s) => s.title === 'Perguntas frequentes')?.body ?? ''
  return [...faq.matchAll(/\*\*([^*]+\?)\*\*/g)].map((m) => m[1].trim()).slice(0, 3)
}
