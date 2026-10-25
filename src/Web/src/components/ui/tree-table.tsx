import { ChevronDown, ChevronRight } from 'lucide-react'
import { type KeyboardEvent, type ReactNode, useMemo, useRef, useState } from 'react'
import { normalizeText } from '@/lib/text'
import { cn } from '@/lib/utils'

export interface TreeNode {
  id: string
  label: string
  children?: TreeNode[]
}

export interface TreeColumn<T> {
  header: ReactNode
  cell: (node: T) => ReactNode
  numeric?: boolean
  /** Chave da coluna; sem ela, o cabeçalho em texto. */
  key?: string
  /** Rótulo acessível do cabeçalho quando ele não é texto. */
  title?: string
  /** Classe da célula e do cabeçalho, ex.: "hidden sm:table-cell" para a coluna sumir a 400 px (EST-04). */
  className?: string
}

interface Flat<T extends TreeNode> {
  node: T
  level: number
  path: string[]
  parentId?: string
  hasChildren: boolean
}

function flatten<T extends TreeNode>(nodes: T[], open: Set<string>, level = 1, path: string[] = [], all = false, parentId?: string): Flat<T>[] {
  return nodes.flatMap((node) => {
    const kids = (node.children ?? []) as T[]
    const row: Flat<T> = { node, level, path, parentId, hasChildren: kids.length > 0 }
    return [row, ...(kids.length > 0 && (all || open.has(node.id)) ? flatten(kids, open, level + 1, [...path, node.label], all, node.id) : [])]
  })
}

function allIds(nodes: TreeNode[]): string[] {
  return nodes.flatMap((n) => [n.id, ...allIds(n.children ?? [])])
}

export interface TreeTableProps<T extends TreeNode> {
  nodes: T[]
  label: string
  columns?: TreeColumn<T>[]
  renderLabel?: (node: T) => ReactNode
  /** Cabeçalho da coluna de nomes (padrão "Nome"). */
  labelHeader?: ReactNode
  /** Busca no topo (padrão: ligada). */
  searchable?: boolean
  /** Grupos abertos no começo: só as raízes (padrão), todos ou nenhum. */
  defaultExpanded?: 'roots' | 'all' | 'none'
  /**
   * Tabela analítica (D18, FIN-08 CA 12): coluna de nomes fixa na rolagem horizontal e navegação por célula. Setas movem a célula
   * ativa; Home/End vão ao início e ao fim da linha; na coluna de nomes, direita e esquerda abrem e fecham o grupo; Enter abre o
   * drill-down (`onActivateCell`).
   */
  cellNavigation?: boolean
  /** Coluna de nomes fixa na rolagem horizontal. */
  stickyLabel?: boolean
  /** Enter ou clique numa célula de valor (coluna a partir de 0, sem contar a de nomes). */
  onActivateCell?: (node: T, columnIndex: number) => void
  /** A célula abre o drill-down? Padrão: toda célula de valor. */
  canActivate?: (node: T, columnIndex: number) => boolean
  rowClassName?: (node: T) => string | undefined
  /** Classe da célula de valor (ex.: tom crítico só em resultado negativo). */
  cellClassName?: (node: T, columnIndex: number) => string | undefined
}

/**
 * Árvore em tabela (FND-09d CA 11): plano de contas, centros de custo, categorias. role="treegrid" com aria-level e
 * aria-expanded; setas para cima e para baixo movem, direita expande, esquerda recolhe ou sobe ao pai. A busca mostra
 * o caminho dos pais. Com <c>cellNavigation</c> (D18), é a tabela analítica do DRE, da ocupação (PLN-04) e do espelho de ponto:
 * coluna de nomes fixa, célula ativa com foco visível, aria-rowindex e aria-colindex.
 */
export function TreeTable<T extends TreeNode>({
  nodes, label, columns = [], renderLabel, labelHeader = 'Nome', searchable = true, defaultExpanded = 'roots', cellNavigation = false, stickyLabel = false,
  onActivateCell, canActivate, rowClassName, cellClassName,
}: TreeTableProps<T>) {
  const [open, setOpen] = useState<Set<string>>(() => new Set(defaultExpanded === 'all' ? allIds(nodes) : defaultExpanded === 'roots' ? nodes.map((n) => n.id) : []))
  const [term, setTerm] = useState('')
  const [focus, setFocus] = useState<string | null>(null)
  const [col, setCol] = useState(0)
  const refs = useRef(new Map<string, HTMLElement>())
  const q = normalizeText(term.trim())
  const rows = useMemo(() => {
    const flat = flatten(nodes, open, 1, [], q.length > 0)
    return q ? flat.filter((r) => normalizeText(r.node.label).includes(q)) : flat
  }, [nodes, open, q])
  const current = focus && rows.some((r) => r.node.id === focus) ? focus : rows[0]?.node.id
  const lastCol = columns.length
  const cellKey = (id: string, c: number) => `${id}:${c}`
  const activatable = (node: T, c: number) => c > 0 && !!onActivateCell && (canActivate ? canActivate(node, c - 1) : true)

  const go = (id: string | undefined, c = 0) => {
    if (!id) return
    setFocus(id)
    setCol(c)
    // O elemento pode nascer só no próximo render (grupo recém-aberto): tenta de novo depois.
    const el = refs.current.get(cellNavigation ? cellKey(id, c) : id)
    if (el) el.focus()
    else requestAnimationFrame(() => refs.current.get(cellNavigation ? cellKey(id, c) : id)?.focus())
  }
  const toggle = (id: string, value: boolean) => setOpen((s) => { const n = new Set(s); if (value) n.add(id); else n.delete(id); return n })

  const rowKeys = (event: KeyboardEvent, index: number, row: Flat<T>) => {
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

  const cellKeys = (event: KeyboardEvent, index: number, row: Flat<T>, c: number) => {
    const id = row.node.id
    const isOpen = open.has(id) || q.length > 0
    if (event.key === 'ArrowDown') go(rows[index + 1]?.node.id ?? id, c)
    else if (event.key === 'ArrowUp') go(rows[index - 1]?.node.id ?? id, c)
    else if (event.key === 'ArrowRight') {
      if (c === 0 && row.hasChildren && !isOpen) toggle(id, true)
      else if (c < lastCol) go(id, c + 1)
    } else if (event.key === 'ArrowLeft') {
      if (c === 0) {
        if (row.hasChildren && isOpen) toggle(id, false)
        else go(rows.slice(0, index).reverse().find((r) => r.level < row.level)?.node.id, 0)
      } else go(id, c - 1)
    } else if (event.key === 'Home') go(event.ctrlKey ? rows[0]?.node.id : id, 0)
    else if (event.key === 'End') go(event.ctrlKey ? rows[rows.length - 1]?.node.id : id, lastCol)
    else if (event.key === 'Enter' || event.key === ' ') {
      if (c === 0 && row.hasChildren) toggle(id, !isOpen)
      else if (activatable(row.node, c)) onActivateCell?.(row.node, c - 1)
      else return
    } else return
    event.preventDefault()
  }

  const sticky = stickyLabel ? 'sticky left-0 z-10 bg-card' : ''
  const labelCell = (row: Flat<T>) => (
    <span className="inline-flex items-center gap-1">
      {row.hasChildren ? (
        <button type="button" tabIndex={-1} aria-hidden="true" onClick={() => toggle(row.node.id, !open.has(row.node.id))} className="grid size-5 shrink-0 place-items-center rounded-sm hover:bg-muted">
          {open.has(row.node.id) || q ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
        </button>
      ) : <span className="size-5 shrink-0" />}
      <span>
        {renderLabel ? renderLabel(row.node) : row.node.label}
        {q && row.path.length > 0 ? <span className="block text-xs text-muted-foreground">{row.path.join(' › ')}</span> : null}
      </span>
    </span>
  )
  const indent = (row: Flat<T>) => ({ paddingLeft: `${0.75 + Math.min(row.level - 1, 6) * 1.25}rem` })

  return (
    <div className="rounded-md border border-border bg-card">
      {searchable ? (
        <div className="border-b border-border p-2">
          <input type="search" value={term} onChange={(e) => setTerm(e.target.value)} placeholder={`Buscar em ${label.toLowerCase()}`} aria-label={`Buscar em ${label.toLowerCase()}`} className="w-full bg-transparent px-1 py-1 text-sm outline-none" />
        </div>
      ) : null}
      <div className={cellNavigation || stickyLabel ? 'overflow-x-auto' : undefined}>
        <table role="treegrid" aria-label={label} aria-rowcount={cellNavigation ? rows.length + 1 : undefined} aria-colcount={cellNavigation ? lastCol + 1 : undefined} className="w-full border-collapse text-sm">
          <thead className="border-b border-border text-xs text-muted-foreground">
            <tr aria-rowindex={cellNavigation ? 1 : undefined}>
              <th scope="col" aria-colindex={cellNavigation ? 1 : undefined} className={cn('px-3 py-2 text-left font-semibold', sticky)}>{labelHeader}</th>
              {columns.map((c, i) => (
                <th key={c.key ?? (typeof c.header === 'string' ? c.header : i)} scope="col" aria-colindex={cellNavigation ? i + 2 : undefined} aria-label={c.title}
                  className={cn(c.numeric ? 'whitespace-nowrap px-3 py-2 text-right font-semibold' : 'px-3 py-2 text-left font-semibold', c.className)}>{c.header}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={1 + columns.length} className="px-3 py-4 text-sm text-muted-foreground">Nada encontrado para "{term}".</td></tr>
            ) : cellNavigation ? rows.map((row, i) => {
              const active = (c: number) => row.node.id === current && c === col
              const register = (c: number) => (el: HTMLElement | null) => { const k = cellKey(row.node.id, c); if (el) refs.current.set(k, el); else refs.current.delete(k) }
              return (
                <tr key={row.node.id} aria-rowindex={i + 2} aria-level={row.level} aria-expanded={row.hasChildren ? open.has(row.node.id) || q.length > 0 : undefined}
                  className={cn('border-b border-border last:border-0', rowClassName?.(row.node))}>
                  <th scope="row" role="gridcell" aria-colindex={1} ref={register(0)} tabIndex={active(0) ? 0 : -1}
                    onFocus={() => { setFocus(row.node.id); setCol(0) }} onKeyDown={(e) => cellKeys(e, i, row, 0)}
                    className={cn('px-3 py-1.5 text-left font-normal focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ring', sticky)} style={indent(row)}>
                    {labelCell(row)}
                  </th>
                  {columns.map((c, ci) => {
                    const index = ci + 1
                    const can = activatable(row.node, index)
                    return (
                      <td key={c.key ?? (typeof c.header === 'string' ? c.header : ci)} role="gridcell" aria-colindex={index + 1} ref={register(index)} tabIndex={active(index) ? 0 : -1}
                        onFocus={() => { setFocus(row.node.id); setCol(index) }} onKeyDown={(e) => cellKeys(e, i, row, index)}
                        onClick={() => { go(row.node.id, index); if (can) onActivateCell?.(row.node, ci) }}
                        className={cn(c.numeric ? 'whitespace-nowrap px-3 py-1.5 text-right tabular-nums' : 'px-3 py-1.5', can && 'cursor-pointer hover:bg-muted',
                          'focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ring', c.className, cellClassName?.(row.node, ci))}>
                        {c.cell(row.node)}
                      </td>
                    )
                  })}
                </tr>
              )
            }) : rows.map((row, i) => (
              <tr
                key={row.node.id}
                ref={(el) => { if (el) refs.current.set(row.node.id, el); else refs.current.delete(row.node.id) }}
                aria-level={row.level}
                aria-expanded={row.hasChildren ? open.has(row.node.id) || q.length > 0 : undefined}
                tabIndex={row.node.id === current ? 0 : -1}
                onFocus={() => setFocus(row.node.id)}
                onKeyDown={(e) => rowKeys(e, i, row)}
                className={cn('border-b border-border last:border-0 focus-visible:bg-muted focus-visible:outline-none', rowClassName?.(row.node))}
              >
                <td className={cn('px-3 py-1.5', sticky)} style={indent(row)}>{labelCell(row)}</td>
                {columns.map((c, ci) => <td key={c.key ?? (typeof c.header === 'string' ? c.header : ci)} className={cn(c.numeric ? 'px-3 py-1.5 text-right tabular-nums' : 'px-3 py-1.5', c.className, cellClassName?.(row.node, ci))}>{c.cell(row.node)}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
