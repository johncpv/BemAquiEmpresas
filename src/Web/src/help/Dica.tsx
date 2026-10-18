import * as Popover from '@radix-ui/react-popover'
import { useId, useState } from 'react'

/**
 * Explicação curta ao lado do que confunde (FND-14 CA 7): abre no mouse, no foco (Tab) e no toque; Esc ou tocar fora
 * fecha. O texto também está sempre ligado ao "?" por aria-describedby, para o leitor de tela.
 */
/** `nome`: o campo explicado, para o leitor de tela distinguir várias dicas ("Dica: Regime tributário", FND-09d CA 20). */
export function Dica({ texto, nome }: { texto: string; nome?: string }) {
  const [open, setOpen] = useState(false)
  const id = useId()
  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger
        type="button"
        aria-label={nome ? `Dica: ${nome}` : 'Dica'}
        aria-describedby={id}
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        // Toque e clique sempre abrem (o toque também dispara foco e hover; sem isto o Radix alternaria e fecharia).
        onClick={(e) => {
          e.preventDefault()
          setOpen(true)
        }}
        // Alvo de 24 px (WCAG 2.5.8); o círculo visível continua com 16 px.
        className="ml-0.5 inline-grid size-6 place-items-center rounded-full align-middle text-muted-foreground"
      >
        <span aria-hidden="true" className="grid size-4 place-items-center rounded-full border border-input text-[10px] font-bold">?</span>
      </Popover.Trigger>
      <span id={id} className="sr-only">{texto}</span>
      <Popover.Portal>
        <Popover.Content side="top" sideOffset={6} onOpenAutoFocus={(e) => e.preventDefault()} className="z-50 max-w-64 rounded-md border border-border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-lg">
          {texto}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}
