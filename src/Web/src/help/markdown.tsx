import type { ReactNode } from 'react'
import { renderInline } from './inline'

export function Markdown({ text }: { text: string }) {
  const blocks: ReactNode[] = []
  let list: { ordered: boolean; items: string[] } | null = null
  let paragraph: string[] = []
  const flush = () => {
    if (paragraph.length) blocks.push(<p key={blocks.length}>{renderInline(paragraph.join(' '))}</p>)
    paragraph = []
    if (list) {
      const items = list.items.map((item, i) => <li key={i}>{renderInline(item)}</li>)
      blocks.push(list.ordered ? <ol key={blocks.length} className="list-decimal space-y-1 pl-5">{items}</ol> : <ul key={blocks.length} className="list-disc space-y-1 pl-5">{items}</ul>)
      list = null
    }
  }

  for (const raw of text.split('\n')) {
    const line = raw.trim()
    const bullet = /^[-*] (.*)$/.exec(line)
    const numbered = /^\d+\. (.*)$/.exec(line)
    if (bullet || numbered) {
      if (paragraph.length) {
        blocks.push(<p key={blocks.length}>{renderInline(paragraph.join(' '))}</p>)
        paragraph = []
      }

      const ordered = !!numbered
      if (list && list.ordered !== ordered) flush()
      list ??= { ordered, items: [] }
      list.items.push((bullet ?? numbered)![1])
    } else if (!line) {
      flush()
    } else {
      if (list) flush()
      paragraph.push(line)
    }
  }

  flush()
  return <div className="space-y-2 text-sm leading-relaxed">{blocks}</div>
}
