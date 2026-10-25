import { ChevronDown, ChevronRight } from 'lucide-react'
import { type KeyboardEvent, type ReactNode, useMemo, useRef, useState } from 'react'
import { normalizeText } from '@/lib/text'
import { cn } from '@/lib/utils'

export interface TreeNode {
  id: string
  label: string
  children?: TreeNode[]
}

interface Flat<T extends TreeNode> {
  node: T
  level: number
  path: string[]
  hasChildren: boolean
}

function flatten<T extends TreeNode>(nodes: T[], open: Set<string>, level = 1, path: string[] = [], all = false): Flat<T>[] {
  return nodes.flatMap((node) => {
    const kids = (node.children ?? []) as T[]
    const row: Flat<T> = { node, level, path, hasChildren: kids.length > 0 }
    return [row, ...(kids.length > 0 && (all || open.has(node.id)) ? flatten(kids, open, level + 1, [...path, node.label], all) : [])]
  })
}

function allIds(nodes: TreeNode[]): string[] {
  return nodes.flatMap((n) => [n.id, ...allIds(n.children ?? [])])
}

export interface TreeColumn<T> {
  header: string
  cell: (node: T) => ReactNode
  numeric?: boolean
  /** Classe da célula e do cabeçalho, ex.: "hidden sm:table-cell" para a coluna sumir a 400 px (o rótulo mostra o resumo). */
  className?: string
}

/**
 * Árvore em tabela (FND-09d CA 11): plano de contas, centros de custo, categorias, rastreabilidade (EST-04). role="treegrid" com
 * aria-level e aria-expanded; setas para cima e para baixo movem, direita expande, esquerda recolhe ou sobe ao pai. A busca mostra
 * o caminho dos pais. <c>searchable</c> falso tira a busca interna; <c>expandAll</c> abre todos os níveis de início.
 */
export function TreeTable<T extends TreeNode>({ nodes, label, columns, renderLabel, searchable = true, expandAll = false, nameHeader = 'Nome' }: {
  nodes: T[]; label: string; columns?: TreeColumn<T>[]; renderLabel?: (node: T) => ReactNode; searchable?: boolean; expandAll?: boolean; nameHeader?: string
}) {
  const [open, setOpen] = useState<Set<string>>(() => new Set(expandAll ? allIds(nodes) : nodes.map((n) => n.id)))
  const [term, setTerm] = useState('')
  const [focus, setFocus] = useState<string | null>(null)
  const refs = useRef(new Map<string, HTMLTableRowElement>())
  const q = normalizeText(term.trim())
  const rows = useMemo(() => {
    const flat = flatten(nodes, open, 1, [], q.length > 0)
    return q ? flat.filter((r) => normalizeText(r.node.label).includes(q)) : flat
  }, [nodes, open, q])
  const current = focus && rows.some((r) => r.node.id === focus) ? focus : rows[0]?.node.id

  const go = (id: string | undefined) => {
    if (!id) return
    setFocus(id)
    refs.current.get(id)?.focus()
  }
  const toggle = (id: string, value: boolean) => setOpen((s) => { const n = new Set(s); if (value) n.add(id); else n.delete(id); return n })
  const keys = (event: KeyboardEvent, index: number, row: Flat<T>) => {
    const id = row.node.id
    if (event.key === 'ArrowDown') go(rows[index + 1]?.node.id)
    else if (event.key === 'ArrowUp') go(rows[index - 1]?.node.id)
    else if (event.key === 'ArrowRight' && row.hasChildren) { if (!open.has(id)) toggle(id, true); else go(rows[index + 1]?.node.id) }
    else if (event.key === 'ArrowLeft') {
      if (row.hasChildren && open.has(id)) toggle(id, false)
      else go(rows.slice(0, index).reverse().find((r) => r.level < row.level)?.node.id)
    } else if (event.key === 'Home') go(rows[0]?.node.id)
    else if (event.key === 'End') go(rows[rows.length - 1]?.node.id)
    else return
    event.preventDefault()
  }

  return (
    <div className="rounded-md border border-border bg-card">
      {searchable ? (
        <div className="border-b border-border p-2">
          <input type="search" value={term} onChange={(e) => setTerm(e.target.value)} placeholder={`Buscar em ${label.toLowerCase()}`} aria-label={`Buscar em ${label.toLowerCase()}`} className="w-full bg-transparent px-1 py-1 text-sm outline-none" />
        </div>
      ) : null}
      <table role="treegrid" aria-label={label} className="w-full border-collapse text-sm">
        <thead className="border-b border-border text-xs text-muted-foreground">
          <tr>
            <th scope="col" className="px-3 py-2 text-left font-semibold">{nameHeader}</th>
            {columns?.map((c) => <th key={c.header} scope="col" className={cn(c.numeric ? 'px-3 py-2 text-right font-semibold' : 'px-3 py-2 text-left font-semibold', c.className)}>{c.header}</th>)}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr><td colSpan={1 + (columns?.length ?? 0)} className="px-3 py-4 text-sm text-muted-foreground">Nada encontrado para "{term}".</td></tr>
          ) : rows.map((row, i) => (
            <tr
              key={row.node.id}
              ref={(el) => { if (el) refs.current.set(row.node.id, el); else refs.current.delete(row.node.id) }}
              aria-level={row.level}
              aria-expanded={row.hasChildren ? open.has(row.node.id) || q.length > 0 : undefined}
              tabIndex={row.node.id === current ? 0 : -1}
              onFocus={() => setFocus(row.node.id)}
              onKeyDown={(e) => keys(e, i, row)}
              className="border-b border-border last:border-0 focus-visible:bg-muted focus-visible:outline-none"
            >
              <td className="px-3 py-1.5" style={{ paddingLeft: `${0.75 + Math.min(row.level - 1, 6) * 1.25}rem` }}>
                <span className="inline-flex items-center gap-1">
                  {row.hasChildren ? (
                    <button type="button" tabIndex={-1} aria-hidden="true" onClick={() => toggle(row.node.id, !open.has(row.node.id))} className="grid size-5 place-items-center rounded-sm hover:bg-muted">
                      {open.has(row.node.id) || q ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
                    </button>
                  ) : <span className="size-5" />}
                  <span>
                    {renderLabel ? renderLabel(row.node) : row.node.label}
                    {q && row.path.length > 0 ? <span className="block text-xs text-muted-foreground">{row.path.join(' › ')}</span> : null}
                  </span>
                </span>
              </td>
              {columns?.map((c) => <td key={c.header} className={cn(c.numeric ? 'px-3 py-1.5 text-right tabular-nums' : 'px-3 py-1.5', c.className)}>{c.cell(row.node)}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
