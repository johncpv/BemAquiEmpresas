import { useNavigate } from '@tanstack/react-router'
import { Command } from 'cmdk'
import { useEffect, useMemo, useState } from 'react'
import { useSearchItems, useSearchParties } from '@/api/generated/bemaqui'
import { useNavigation } from '@/navigation/navigation-context'
import { TYPE_LABELS } from '@/pages/inventory/item-labels'
import { ROLE_LABELS } from '@/pages/parties/labels'
import { readRecent, rememberRecent } from '@/navigation/recent'

/**
 * Ctrl K / ⌘ K (FND-09b CA 9): busca por módulo e sub-tela; os usados por último aparecem primeiro (propositivo).
 * Com parties.view, a partir de 3 letras também mostra até 5 terceiros (CAD-01a CA 19); com inventory.view, a partir de 2,
 * até 5 itens (CAD-02a CA 20). Sem a permissão, nada aparece (ADR 0010 item 9).
 */
export function CommandPalette({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { areas, all } = useNavigation()
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const parties = all.find((m) => m.code === 'parties')
  const canParties = !!parties && !parties.locked && parties.actions.includes('view')
  useEffect(() => {
    const t = setTimeout(() => setTerm(search.trim()), 300)
    return () => clearTimeout(t)
  }, [search])
  const hits = useSearchParties({ q: term }, { query: { enabled: open && canParties && term.length >= 3 } })
  const inventory = all.find((m) => m.code === 'inventory')
  const canItems = !!inventory && !inventory.locked && inventory.actions.includes('view')
  const items = useSearchItems({ q: term }, { query: { enabled: open && canItems && term.length >= 2 } })

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        onOpenChange(!open)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onOpenChange])

  const entries = useMemo(() => {
    const all = areas.flatMap((a) =>
      a.modules.flatMap((m) => [
        { path: m.path, label: m.locked ? `${m.name} (não liberado)` : m.name, hint: a.name },
        // Sub-telas só de módulo com acesso; o não liberado leva à tela padrão (FND-06d CA 5).
        ...(m.locked ? [] : m.tabs.map((t) => ({ path: `${m.path}/${t.slug}`, label: `${m.name} › ${t.label}`, hint: a.name }))),
      ]),
    )
    const recent = open ? readRecent() : []
    return [...all].sort((x, y) => {
      const rx = recent.indexOf(x.path)
      const ry = recent.indexOf(y.path)
      return (rx < 0 ? 99 : rx) - (ry < 0 ? 99 : ry)
    })
  }, [areas, open])

  const go = (path: string) => {
    rememberRecent(path)
    onOpenChange(false)
    setSearch('')
    void navigate({ to: path })
  }

  return (
    <Command.Dialog
      open={open}
      onOpenChange={onOpenChange}
      label="Ir para uma tela"
      overlayClassName="fixed inset-0 z-40 bg-foreground/30"
      contentClassName="fixed left-1/2 top-[15vh] z-50 w-[min(560px,calc(100vw-32px))] -translate-x-1/2 overflow-hidden rounded-md border border-border bg-popover text-popover-foreground shadow-lg"
    >
      <Command.Input value={search} onValueChange={setSearch} placeholder="Buscar tela ou módulo…" className="w-full border-b border-border bg-transparent px-4 py-3 text-sm outline-none" />
      <Command.List className="max-h-80 overflow-y-auto p-2">
        <Command.Empty className="px-2 py-6 text-center text-sm text-muted-foreground">Nenhum resultado para "{search}".</Command.Empty>
        {entries.map((e) => (
          <Command.Item key={e.path} value={`${e.label} ${e.hint}`} onSelect={() => go(e.path)} className="flex cursor-pointer items-center justify-between rounded-md px-2 py-2 text-sm data-[selected=true]:bg-muted">
            <span>{e.label}</span>
            <span className="text-xs text-muted-foreground">{e.hint}</span>
          </Command.Item>
        ))}
        {canParties && term.length >= 3 && hits.data?.length ? (
          <Command.Group heading="Terceiros" className="mt-1 border-t border-border pt-1 text-xs text-muted-foreground [&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1">
            {hits.data.map((h) => (
              <Command.Item key={h.id} value={`terceiro ${h.id} ${search}`} onSelect={() => go(`/app/cadastros/terceiros/${h.id}`)} className="flex cursor-pointer items-center justify-between gap-2 rounded-md px-2 py-2 text-sm text-foreground data-[selected=true]:bg-muted">
                <span className="min-w-0 truncate">{h.name}</span>
                <span className="shrink-0 text-xs text-muted-foreground">{[h.roles.map((r) => ROLE_LABELS[r]).join(', '), h.document].filter(Boolean).join(' · ')}</span>
              </Command.Item>
            ))}
          </Command.Group>
        ) : null}
        {canItems && term.length >= 2 && items.data?.length ? (
          <Command.Group heading="Itens" className="mt-1 border-t border-border pt-1 text-xs text-muted-foreground [&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1">
            {items.data.map((h) => (
              <Command.Item key={h.id} value={`item ${h.id} ${search}`} onSelect={() => go(`/app/estoque/itens/${h.id}`)} className="flex cursor-pointer items-center justify-between gap-2 rounded-md px-2 py-2 text-sm text-foreground data-[selected=true]:bg-muted">
                <span className="min-w-0 truncate"><span className="font-mono">{h.code}</span> {h.name}</span>
                <span className="shrink-0 text-xs text-muted-foreground">{TYPE_LABELS[h.type]}</span>
              </Command.Item>
            ))}
          </Command.Group>
        ) : null}
      </Command.List>
    </Command.Dialog>
  )
}
