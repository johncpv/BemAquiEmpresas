import { useNavigate } from '@tanstack/react-router'
import { Command } from 'cmdk'
import { useEffect, useMemo, useState } from 'react'
import { useNavigation } from '@/navigation/navigation-context'
import { readRecent, rememberRecent } from '@/navigation/recent'

/** Ctrl K / ⌘ K (FND-09b CA 9): busca por módulo e sub-tela; os usados por último aparecem primeiro (propositivo). */
export function CommandPalette({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { areas } = useNavigation()
  const navigate = useNavigate()
  const [search, setSearch] = useState('')

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
      </Command.List>
    </Command.Dialog>
  )
}
