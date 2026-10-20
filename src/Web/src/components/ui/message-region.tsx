import { CheckCircle2, X, XCircle } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { cn } from '@/lib/utils'
import { dismissMessage, type Message, subscribeMessages } from './messages'

/** Tempo mínimo de uma mensagem de sucesso na tela (ADR 0021). */
export const SUCCESS_MS = 5000

/**
 * Região de mensagens no topo do conteúdo (ADR 0021): abaixo da barra superior e fixa ao rolar, nunca atrás do dock;
 * sucesso em role="status", erro em role="alert". As duas regiões existem desde o início, para o leitor de tela anunciar.
 */
export function MessageRegion() {
  const [items, setItems] = useState<Message[]>([])
  useEffect(() => subscribeMessages(setItems), [])
  const success = items.filter((m) => m.tone === 'success')
  const errors = items.filter((m) => m.tone === 'error')
  return (
    <div className="pointer-events-none sticky top-16 z-20 mx-auto flex max-w-xl flex-col gap-2 empty:hidden" data-messages>
      <div role="alert" className="flex flex-col gap-2 empty:hidden">
        {errors.map((m) => <MessageItem key={m.id} message={m} />)}
      </div>
      <div role="status" aria-live="polite" className="flex flex-col gap-2 empty:hidden">
        {success.map((m) => <MessageItem key={m.id} message={m} />)}
      </div>
    </div>
  )
}

function MessageItem({ message }: { message: Message }) {
  const [held, setHeld] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (message.tone !== 'success' || held) return
    const t = setTimeout(() => dismissMessage(message.id), SUCCESS_MS)
    return () => clearTimeout(t)
  }, [message, held])
  const error = message.tone === 'error'
  const Icon = error ? XCircle : CheckCircle2
  return (
    <div
      ref={ref}
      onPointerEnter={() => setHeld(true)}
      onPointerLeave={() => setHeld(false)}
      onFocus={() => setHeld(true)}
      onBlur={(e) => { if (!ref.current?.contains(e.relatedTarget as Node | null)) setHeld(false) }}
      className={cn('pointer-events-auto flex items-start gap-2 rounded-md border px-3 py-2 text-sm shadow-md', error ? 'border-crit-soft bg-crit-soft text-crit-ink' : 'border-border bg-popover text-popover-foreground')}
    >
      <Icon aria-hidden="true" className={cn('mt-0.5 size-4 shrink-0', error ? 'text-crit-ink' : 'text-ok-ink')} />
      <p className="flex-1">{message.text}</p>
      {message.retry ? (
        <button type="button" onClick={() => { dismissMessage(message.id); message.retry?.() }} className="rounded-sm px-1.5 text-sm font-semibold underline">Tentar de novo</button>
      ) : null}
      <button type="button" onClick={() => dismissMessage(message.id)} aria-label="Fechar" className="grid size-6 place-items-center rounded-sm">
        <X aria-hidden="true" className="size-4" />
      </button>
    </div>
  )
}
