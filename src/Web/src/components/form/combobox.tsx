import * as Popover from '@radix-ui/react-popover'
import { useQuery } from '@tanstack/react-query'
import { Command } from 'cmdk'
import { ChevronsUpDown, Plus } from 'lucide-react'
import { forwardRef, useEffect, useState } from 'react'
import { cn } from '@/lib/utils'

export interface ComboOption {
  id: string
  label: string
  hint?: string
}

interface ComboboxProps {
  /** Chave da consulta (TanStack Query); o termo entra no fim. */
  queryKey: readonly unknown[]
  search: (term: string) => Promise<ComboOption[]>
  value?: string | null
  /** Rótulo do valor atual, para mostrar sem buscar de novo. */
  selected?: ComboOption | null
  onChange: (option: ComboOption | null) => void
  placeholder?: string
  /** "Criar '{termo}'" no fim da lista (FND-09d CA 7): o chamador abre a criação rápida e seleciona o novo. */
  onCreate?: (term: string) => void
  createLabel?: string
  disabled?: boolean
  id?: string
  'aria-label'?: string
  'aria-invalid'?: boolean
  'aria-describedby'?: string
  'aria-required'?: boolean
}

/**
 * Seleção com busca no servidor (FND-09d CA 7): debounce de 250 ms, carregando, vazio e erro; setas, Enter e Esc.
 */
export const Combobox = forwardRef<HTMLButtonElement, ComboboxProps>(function Combobox(
  { queryKey, search, selected, onChange, placeholder = "Selecionar", onCreate, createLabel = "Criar", disabled, value: _value, ...aria },
  ref,
) {
  const [open, setOpen] = useState(false)
  const [term, setTerm] = useState('')
  const [debounced, setDebounced] = useState('')
  useEffect(() => {
    const t = setTimeout(() => setDebounced(term.trim()), 250)
    return () => clearTimeout(t)
  }, [term])
  const results = useQuery({ queryKey: [...queryKey, debounced], queryFn: () => search(debounced), enabled: open })

  return (
    <Popover.Root open={open} onOpenChange={(o) => { setOpen(o); if (!o) setTerm('') }}>
      <Popover.Trigger asChild>
        <button
          ref={ref}
          type="button"
          role="combobox"
          aria-expanded={open}
          disabled={disabled}
          {...aria}
          className="flex w-full items-center justify-between gap-2 rounded-md border border-input bg-card px-3 py-2 text-left text-sm aria-[invalid=true]:border-crit disabled:opacity-60"
        >
          <span className={cn('truncate', !selected && 'text-muted-foreground')}>{selected?.label ?? placeholder}</span>
          <ChevronsUpDown aria-hidden="true" className="size-4 shrink-0 text-muted-foreground" />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content align="start" sideOffset={4} className="z-50 w-[var(--radix-popover-trigger-width)] min-w-64 rounded-md border border-border bg-popover text-popover-foreground shadow-lg">
          <Command shouldFilter={false} label={placeholder}>
            <Command.Input value={term} onValueChange={setTerm} placeholder="Digite para buscar" className="w-full border-b border-border bg-transparent px-3 py-2 text-sm outline-none" />
            <Command.List className="max-h-64 overflow-y-auto p-1">
              {results.isPending ? <Command.Loading><p className="px-2 py-2 text-sm text-muted-foreground">Buscando…</p></Command.Loading> : null}
              {results.isError ? <p role="alert" className="px-2 py-2 text-sm text-crit-ink">Não foi possível buscar. Tente de novo.</p> : null}
              {results.isSuccess && results.data.length === 0 ? <p className="px-2 py-2 text-sm text-muted-foreground">Nada encontrado{debounced ? ` para "${debounced}"` : ''}.</p> : null}
              {(results.data ?? []).map((o) => (
                <Command.Item key={o.id} value={o.id} onSelect={() => { onChange(o); setOpen(false) }} className="flex cursor-pointer flex-col rounded-sm px-2 py-1.5 text-sm aria-selected:bg-muted">
                  <span>{o.label}</span>
                  {o.hint ? <span className="text-xs text-muted-foreground">{o.hint}</span> : null}
                </Command.Item>
              ))}
              {onCreate && debounced ? (
                <Command.Item value={`__criar__${debounced}`} onSelect={() => { setOpen(false); onCreate(debounced) }} className="flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-sm font-semibold text-primary aria-selected:bg-muted">
                  <Plus aria-hidden="true" className="size-4" />
                  {createLabel} "{debounced}"
                </Command.Item>
              ) : null}
            </Command.List>
          </Command>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
})
