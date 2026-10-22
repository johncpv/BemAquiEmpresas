import * as Dialog from '@radix-ui/react-dialog'
import * as Popover from '@radix-ui/react-popover'
import { CheckCircle2, CircleHelp, Delete, XCircle } from 'lucide-react'
import { type ButtonHTMLAttributes, type ReactNode, useEffect, useRef, useState } from 'react'
import { ApiError } from '@/api/http'
import { dismissMessage, type Message, subscribeMessages } from '@/components/ui/messages'
import { cn } from '@/lib/utils'

/**
 * Kit "chão de fábrica" (PRD-03a, padrão 1 da Fase 3): alvos de 56 × 56 px com 8 px entre eles e 16 px entre ações opostas,
 * texto principal de 18 px, nenhum gesto fino (só toque simples), mensagens numa região própria (aria-live), confirmação em folha
 * de tela cheia e cabeçalho com "Ajuda". Usado pelo /fab e, depois, pela PRD-03b, PRD-05 e EST-05.
 */
export const FLOOR_TARGET_PX = 56

type Tone = 'primary' | 'secondary' | 'ok' | 'danger'

const tones: Record<Tone, string> = {
  primary: 'bg-primary text-primary-foreground',
  secondary: 'border-2 border-input bg-card text-foreground',
  ok: 'bg-ok text-success-foreground',
  danger: 'bg-crit text-critical-foreground',
}

/** Botão grande (≥ 56 × 56 px, 18 px): toque simples, sem arrastar nem toque longo. */
export function FloorButton({ tone = 'primary', className, children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { tone?: Tone }) {
  return (
    <button type="button" {...props} data-floor-target=""
      className={cn('inline-flex min-h-[56px] min-w-[56px] select-none items-center justify-center gap-2 rounded-md px-5 text-[18px] font-semibold leading-tight disabled:opacity-60', tones[tone], className)}>
      {children}
    </button>
  )
}

/** Cabeçalho da tela dedicada: título, linha de contexto e o botão "Ajuda" (no /fab a ajuda não abre sozinha). */
export function FloorHeader({ title, subtitle, onHelp, children }: { title: ReactNode; subtitle?: ReactNode; onHelp: () => void; children?: ReactNode }) {
  return (
    <header className="sticky top-0 z-10 border-b border-border bg-background/95 backdrop-blur">
      <div className="mx-auto flex max-w-3xl items-center gap-[8px] px-4 py-2">
        <div className="min-w-0 flex-1">
          {subtitle ? <p className="truncate text-sm text-muted-foreground">{subtitle}</p> : null}
          <p className="truncate font-display text-[22px] font-bold font-wider">{title}</p>
        </div>
        {children}
        <FloorButton tone="secondary" onClick={onHelp} aria-label="Ajuda desta tela" className="px-3">
          <CircleHelp aria-hidden="true" className="size-6" />
          <span className="max-sm:sr-only">Ajuda</span>
        </FloorButton>
      </div>
    </header>
  )
}

/** Região de mensagens própria do kit (ADR 0021): sucesso em status, erro em alert, texto de 18 px, sempre visível no topo. */
export function FloorMessages() {
  const [items, setItems] = useState<Message[]>([])
  useEffect(() => subscribeMessages(setItems), [])
  useEffect(() => {
    const timers = items.filter((m) => m.tone === 'success').map((m) => setTimeout(() => dismissMessage(m.id), 6000))
    return () => timers.forEach(clearTimeout)
  }, [items])
  const item = (m: Message) => (
    <div key={m.id} className={cn('flex items-center gap-3 rounded-md border px-4 py-3 text-[18px] shadow-md', m.tone === 'error' ? 'border-crit-soft bg-crit-soft text-crit-ink' : 'border-ok-soft bg-ok-soft text-ok-ink')}>
      {m.tone === 'error' ? <XCircle aria-hidden="true" className="size-6 shrink-0" /> : <CheckCircle2 aria-hidden="true" className="size-6 shrink-0" />}
      <p className="flex-1 font-semibold">{m.text}</p>
      <button type="button" onClick={() => dismissMessage(m.id)} className="min-h-[56px] min-w-[56px] rounded-md text-base font-semibold underline">Fechar</button>
    </div>
  )
  return (
    <div data-floor-messages="" className="pointer-events-none sticky top-[76px] z-20 mx-auto flex max-w-3xl flex-col gap-2 px-4 empty:hidden [&>*]:pointer-events-auto">
      <div role="alert" className="flex flex-col gap-2 empty:hidden">{items.filter((m) => m.tone === 'error').map(item)}</div>
      <div role="status" aria-live="polite" className="flex flex-col gap-2 empty:hidden">{items.filter((m) => m.tone === 'success').map(item)}</div>
    </div>
  )
}

/** Folha de tela cheia (toda escolha e toda confirmação do kit): título grande, conteúdo rolável e as ações embaixo. */
export function FloorSheet({ open, onOpenChange, title, description, children, actions, role = 'dialog' }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: ReactNode
  children?: ReactNode
  actions?: ReactNode
  role?: 'dialog' | 'alertdialog'
}) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/40" />
        <Dialog.Content role={role} className="fixed inset-0 z-50 flex flex-col bg-background text-foreground">
          <div className="mx-auto flex w-full max-w-3xl flex-1 flex-col overflow-hidden">
            <div className="flex-1 overflow-y-auto px-4 pt-6 pb-4">
              <Dialog.Title className="font-display text-[26px] font-bold font-wider">{title}</Dialog.Title>
              {description ? <Dialog.Description className="mt-2 text-[18px] text-muted-foreground">{description}</Dialog.Description> : <Dialog.Description className="sr-only">{title}</Dialog.Description>}
              {children ? <div className="mt-5 space-y-4 text-[18px]">{children}</div> : null}
            </div>
            {actions ? <div className="border-t border-border px-4 py-4 pb-[calc(16px+env(safe-area-inset-bottom,0px))]">{actions}</div> : null}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

/**
 * Confirmação em folha de tela cheia (padrão 4): título com o verbo e o efeito ("Finalizar etapa · 2,5 h apontadas"), antes →
 * depois, "Voltar" e o verbo, com 16 px entre as duas ações; erro do servidor dentro da folha.
 */
export function FloorConfirm({ open, onOpenChange, title, verb, impact, onConfirm, tone = 'primary' }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  verb: string
  impact: ReactNode
  onConfirm: () => Promise<unknown>
  tone?: Tone
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const alertRef = useRef<HTMLDivElement>(null)
  useEffect(() => { if (error) alertRef.current?.focus() }, [error])
  const run = async () => {
    setBusy(true)
    setError(null)
    try {
      await onConfirm()
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível concluir. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <FloorSheet role="alertdialog" open={open} onOpenChange={(o) => { setError(null); onOpenChange(o) }} title={title}
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => onOpenChange(false)} disabled={busy}>Voltar</FloorButton>
          <FloorButton tone={tone} className="flex-1" onClick={() => void run()} disabled={busy}>{busy ? 'Gravando…' : verb}</FloorButton>
        </div>
      )}>
      <div role="group" aria-label="O que muda" className="space-y-2 rounded-md border border-border bg-card p-4 text-[18px] tabular-nums">{impact}</div>
      {error ? <div ref={alertRef} tabIndex={-1} role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] text-crit-ink outline-none">{error}</div> : null}
    </FloorSheet>
  )
}

/** Item do checklist (CA 13): a linha inteira é o alvo (≥ 56 px), com a caixa grande e o texto de 18 px. */
export function FloorCheck({ checked, label, onChange, disabled, detail }: { checked: boolean; label: string; onChange: (checked: boolean) => void; disabled?: boolean; detail?: string }) {
  return (
    <label data-floor-target="" className={cn('flex min-h-[56px] cursor-pointer items-center gap-4 rounded-md border-2 px-4 py-2 text-[18px]', checked ? 'border-ok bg-ok-soft' : 'border-input bg-card', disabled && 'cursor-default opacity-70')}>
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} className="size-8 shrink-0 accent-primary" />
      <span className="flex-1">
        <span className="block font-medium">{label}</span>
        {detail ? <span className="block text-sm text-muted-foreground">{detail}</span> : null}
      </span>
    </label>
  )
}

/** Teclado numérico grande (CA 3): só toque, dígitos de 56 px com 8 px entre eles; sem teclado do sistema. */
export function NumericKeypad({ value, onChange, max = 10, label }: { value: string; onChange: (value: string) => void; max?: number; label: string }) {
  const press = (digit: string) => { if (value.length < max) onChange(value + digit) }
  const key = 'min-h-[64px] text-[24px] tabular-nums'
  return (
    <div className="space-y-3">
      <output aria-label={label} aria-live="polite" className="block min-h-[64px] rounded-md border-2 border-input bg-card px-4 py-3 text-center font-display text-[30px] font-bold tabular-nums tracking-widest">
        {value || <span className="text-muted-foreground">—</span>}
      </output>
      <div role="group" aria-label="Teclado numérico" className="grid grid-cols-3 gap-[8px]">
        {['1', '2', '3', '4', '5', '6', '7', '8', '9'].map((d) => <FloorButton key={d} tone="secondary" className={key} onClick={() => press(d)}>{d}</FloorButton>)}
        <FloorButton tone="secondary" className="min-h-[64px] text-[18px]" onClick={() => onChange('')}>Limpar</FloorButton>
        <FloorButton tone="secondary" className={key} onClick={() => press('0')}>0</FloorButton>
        <FloorButton tone="secondary" className="min-h-[64px]" aria-label="Apagar o último número" onClick={() => onChange(value.slice(0, -1))}><Delete aria-hidden="true" className="size-7" /></FloorButton>
      </div>
    </div>
  )
}

/** Bloco fantasma grande enquanto carrega. */
export function FloorSkeleton({ rows = 3 }: { rows?: number }) {
  return (
    <div aria-hidden="true" className="space-y-3">
      {Array.from({ length: rows }, (_, i) => <div key={i} className="h-28 animate-pulse rounded-md bg-muted" />)}
    </div>
  )
}

/** Estado de tela inteira (vazio, erro, sem acesso) com texto de 18 px e a ação grande. */
export function FloorState({ title, message, action, tone = 'neutral' }: { title: string; message?: string; action?: ReactNode; tone?: 'neutral' | 'error' }) {
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={cn('flex flex-col items-center gap-3 rounded-md border-2 border-dashed px-6 py-10 text-center', tone === 'error' ? 'border-crit-soft bg-card' : 'border-border bg-card')}>
      <p className="text-[20px] font-semibold">{title}</p>
      {message ? <p className="max-w-lg text-[18px] text-muted-foreground">{message}</p> : null}
      {action ? <div className="mt-2">{action}</div> : null}
    </div>
  )
}
/** Dica do kit (FND-14 CA 7) com alvo de 56 px: toque simples abre, tocar fora fecha; o texto também vai no rótulo acessível. */
export function FloorDica({ nome, texto }: { nome: string; texto: string }) {
  const [open, setOpen] = useState(false)
  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger type="button" data-floor-target="" aria-label={`Dica: ${nome}. ${texto}`}
        className="inline-grid min-h-[56px] min-w-[56px] place-items-center rounded-md text-muted-foreground">
        <CircleHelp aria-hidden="true" className="size-6" />
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content side="top" sideOffset={4} className="z-50 max-w-xs rounded-md border border-border bg-popover p-4 text-[18px] text-popover-foreground shadow-lg">
          {texto}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}