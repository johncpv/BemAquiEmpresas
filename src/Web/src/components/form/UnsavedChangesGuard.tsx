import * as Dialog from '@radix-ui/react-dialog'
import { useBlocker } from '@tanstack/react-router'

/** Sair com alterações não salvas (FND-09c CA 10): pergunta com os verbos "Salvar" e "Descartar". */
export function UnsavedChangesGuard({ when, onSave }: { when: boolean; onSave: () => Promise<boolean> }) {
  const blocker = useBlocker({ shouldBlockFn: () => when, withResolver: true, enableBeforeUnload: when })
  if (blocker.status !== 'blocked') return null

  return (
    <Dialog.Root open onOpenChange={(open) => !open && blocker.reset()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className="fixed left-1/2 top-1/3 z-50 w-[min(420px,calc(100vw-32px))] -translate-x-1/2 rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg">
          <Dialog.Title className="text-base font-semibold">Há alterações não salvas</Dialog.Title>
          <Dialog.Description className="mt-1 text-sm text-muted-foreground">Salve antes de sair ou descarte o que mudou.</Dialog.Description>
          <div className="mt-5 flex justify-end gap-2">
            <button type="button" onClick={() => blocker.proceed()} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Descartar</button>
            <button type="button" onClick={async () => ((await onSave()) ? blocker.proceed() : blocker.reset())} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar</button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
