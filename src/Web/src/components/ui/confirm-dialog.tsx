import * as Dialog from '@radix-ui/react-dialog'
import { type ReactNode, useState } from 'react'
import { ApiError } from '@/api/http'
import { cn } from '@/lib/utils'
import { DialogMessage } from './dialog-message'
import { type NextStep, toastDone } from './toast'

interface ConfirmDialogProps {
  /** Verbo da ação, repetido no botão de confirmar (ex.: "Desativar"). */
  verb: string
  /** Particípio para o toast de sucesso (ex.: "Desativado"). */
  done: string
  title: string
  description?: ReactNode
  destructive?: boolean
  onConfirm: () => Promise<unknown>
  nextStep?: NextStep
  /** Sem trigger, o diálogo é aberto de fora por open/onOpenChange. */
  trigger?: ReactNode
  open?: boolean
  onOpenChange?: (open: boolean) => void
  /**
   * Ação alternativa quando o servidor recusa (FND-09d CA 17), ex.: excluir recusado por uso oferece "Inativar".
   * `when` filtra pelo código do erro; sem ele, a alternativa aparece em qualquer recusa.
   */
  recovery?: { verb: string; done?: string; onSelect: () => Promise<unknown>; when?: (code: string | undefined) => boolean }
  /** Campos do diálogo (ex.: vigência e motivo do reajuste), entre a descrição e o erro. */
  children?: ReactNode
  /** Chamado ao abrir: o chamador reinicia os campos. */
  onOpen?: () => void
}

/** Confirmação com o mesmo verbo da ação (FND-09c CA 11); erro do servidor fica no diálogo, com o motivo. */
export function ConfirmDialog({ verb, done, title, description, destructive, onConfirm, nextStep, trigger, recovery, children, onOpen, open: controlled, onOpenChange }: ConfirmDialogProps) {
  const [own, setOwn] = useState(false)
  const open = controlled ?? own
  const setOpen = (o: boolean) => { setOwn(o); onOpenChange?.(o) }
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [errorCode, setErrorCode] = useState<string | undefined>()

  const confirm = async () => {
    setBusy(true)
    setError(null)
    try {
      await onConfirm()
      setOpen(false)
      toastDone(done, nextStep)
    } catch (e) {
      setError(e instanceof ApiError || e instanceof DialogMessage ? e.message : 'Não foi possível concluir. Tente de novo.')
      setErrorCode(e instanceof ApiError ? e.code : undefined)
    } finally {
      setBusy(false)
    }
  }

  const recover = async () => {
    if (!recovery) return
    setBusy(true)
    try {
      await recovery.onSelect()
      setOpen(false)
      if (recovery.done) toastDone(recovery.done)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível concluir. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={(o) => { setOpen(o); setError(null); if (o) onOpen?.() }}>
      {trigger ? <Dialog.Trigger asChild>{trigger}</Dialog.Trigger> : null}
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content role="alertdialog" className={cn('fixed left-1/2 z-50 max-h-[calc(100dvh-32px)] w-[min(420px,calc(100vw-32px))] -translate-x-1/2 overflow-y-auto rounded-md', children ? 'top-1/2 -translate-y-1/2' : 'top-1/3', ' border border-border bg-popover p-5 text-popover-foreground shadow-lg')}>
          <Dialog.Title className="text-base font-semibold">{title}</Dialog.Title>
          {description ? <Dialog.Description className="mt-1 text-sm text-muted-foreground">{description}</Dialog.Description> : null}
          {children ? <div className="mt-4 space-y-3">{children}</div> : null}
          {error ? <p role="alert" className="mt-3 text-sm text-crit-ink">{error}</p> : null}
          {error && recovery && (recovery.when?.(errorCode) ?? true) ? (
            <button type="button" disabled={busy} onClick={recover} className="mt-2 rounded-md border border-border px-3 py-1.5 text-sm font-semibold">
              {recovery.verb}
            </button>
          ) : null}
          <div className="mt-5 flex justify-end gap-2">
            <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Dialog.Close>
            <button
              type="button"
              disabled={busy}
              onClick={confirm}
              className={cn('rounded-md px-3 py-2 text-sm font-semibold disabled:opacity-60', destructive ? 'bg-crit text-critical-foreground' : 'bg-primary text-primary-foreground')}
            >
              {verb}
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
