import type { ReactNode } from 'react'

/**
 * Markdown das ajudas (FND-14 CA 9) para elementos React: parágrafos, listas, **negrito** e `código`. Não há HTML cru
 * em nenhum caminho (nada de dangerouslySetInnerHTML): tag ou script no texto aparece escapado, como texto.
 */
export function renderInline(text: string): ReactNode[] {
  const parts: ReactNode[] = []
  const pattern = /(\*\*[^*]+\*\*|`[^`]+`)/g
  let last = 0
  for (const match of text.matchAll(pattern)) {
    if (match.index > last) parts.push(text.slice(last, match.index))
    const token = match[0]
    parts.push(token.startsWith('**') ? <strong key={match.index}>{token.slice(2, -2)}</strong> : <code key={match.index} className="rounded bg-muted px-1 text-xs">{token.slice(1, -1)}</code>)
    last = match.index + token.length
  }

  if (last < text.length) parts.push(text.slice(last))
  return parts
}
