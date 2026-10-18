import * as Dialog from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { type ReactNode, useState } from 'react'

interface SidePanelProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  /** Alteração não salva: fechar pergunta "Salvar" ou "Descartar" (FND-09d CA 21). */
  dirty?: boolean
  onSave?: () => Promise<boolean>
  children: ReactNode
  footer?: ReactNode
}

/**
 * Painel lateral (molde para cadastro simples, FND-09d CA 2 e 21): à direita no desktop e em tela cheia a 400 px;
 * prende o foco e devolve ao fechar (Radix); Esc, clique fora e "Fechar" passam pela guarda de alteração não salva.
 */
export function SidePanel({ open, onOpenChange, title, description, dirty, onSave, children, footer }: SidePanelProps) {
  const [asking, setAsking] = useState(false)
  const requestClose = (next: boolean) => {
    if (!next && dirty) {
      setAsking(true)
      return
    }
    onOpenChange(next)
  }

  return (
    <Dialog.Root open={open} onOpenChange={requestClose}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content {...(description ? {} : { "aria-describedby": undefined })} className="fixed inset-0 z-50 flex flex-col bg-popover text-popover-foreground shadow-xl sm:inset-y-0 sm:left-auto sm:right-0 sm:w-[min(480px,100vw)] sm:border-l sm:border-border">
          <div className="flex items-start justify-between gap-3 border-b border-border px-4 py-3">
            <div>
              <Dialog.Title className="text-base font-semibold">{title}</Dialog.Title>
              {description ? <Dialog.Description className="text-sm text-muted-foreground">{description}</Dialog.Description> : null}
            </div>
            <Dialog.Close aria-label="Fechar" className="grid size-8 place-items-center rounded-md hover:bg-muted">
              <X aria-hidden="true" className="size-4" />
            </Dialog.Close>
          </div>
          <div className="flex-1 overflow-y-auto p-4">{children}</div>
          {footer ? <div className="flex flex-wrap justify-end gap-2 border-t border-border px-4 py-3">{footer}</div> : null}

          {asking ? (
            <div role="alertdialog" aria-labelledby="painel-guarda" className="absolute inset-x-4 top-20 rounded-md border border-border bg-popover p-4 shadow-lg">
              <p id="painel-guarda" className="font-semibold">Há alterações não salvas</p>
              <p className="mt-1 text-sm text-muted-foreground">Salve antes de fechar ou descarte o que mudou.</p>
              <div className="mt-4 flex justify-end gap-2">
                <button type="button" onClick={() => setAsking(false)} className="rounded-md px-3 py-2 text-sm font-semibold">Voltar</button>
                <button type="button" onClick={() => { setAsking(false); onOpenChange(false) }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Descartar</button>
                {onSave ? (
                  <button type="button" onClick={async () => { if (await onSave()) { setAsking(false); onOpenChange(false) } else setAsking(false) }} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar</button>
                ) : null}
              </div>
            </div>
          ) : null}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
