import * as Dialog from '@radix-ui/react-dialog'
import * as Popover from '@radix-ui/react-popover'
import { type ComponentPropsWithRef, useState } from 'react'
import { MOBILE, useMediaQuery } from '@/hooks/useMediaQuery'
import { cn } from '@/lib/utils'
import type { NavArea } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { AreaMenu } from './AreaMenu'

const item =
  'relative flex min-w-[68px] flex-col items-center justify-center gap-1 rounded-[11px] px-2 pb-[7px] pt-2 text-[11.5px] font-semibold whitespace-nowrap text-rail-dim hover:bg-rail-hi hover:text-rail-ink focus-visible:outline-rail-ring data-[active=true]:bg-rail-hi data-[active=true]:text-rail-ink'

/** Repassa ref e props: é o Trigger (asChild) do Popover e do Dialog do Radix. */
function AreaButton({ area, active, ...props }: { area: NavArea; active: boolean } & ComponentPropsWithRef<'button'>) {
  return (
    <button type="button" {...props} className={item} data-active={active} aria-current={active ? 'page' : undefined}>
      {active ? <span aria-hidden="true" className="absolute left-1/2 top-[3px] h-[3px] w-[18px] -translate-x-1/2 rounded-[3px] bg-signal" /> : null}
      <area.icon aria-hidden="true" className="size-[21px]" strokeWidth={1.7} />
      {area.short}
    </button>
  )
}

/** Dock fixo das 7 áreas (FND-09b CA 1 a 5, ADR 0005 D8). No celular encosta na borda e rola na horizontal. */
export function Dock({ activeAreaId }: { activeAreaId?: string }) {
  const { areas, status, retry } = useNavigation()
  const mobile = useMediaQuery(MOBILE)
  const [open, setOpen] = useState<string | null>(null)

  return (
    <div
      className={cn(
        'pointer-events-none fixed inset-x-0 bottom-0 z-20 flex justify-center',
        mobile ? 'pb-[env(safe-area-inset-bottom,0px)]' : 'px-3 pb-[calc(12px+env(safe-area-inset-bottom,0px))]',
      )}
    >
      <nav
        aria-label="Navegação principal"
        className={cn(
          'pointer-events-auto flex max-w-full gap-0.5 overflow-x-auto bg-rail p-1.5 text-rail-ink shadow-lg [scrollbar-width:none] [&::-webkit-scrollbar]:hidden',
          mobile ? 'w-full rounded-none' : 'rounded-2xl',
        )}
      >
        {status === 'loading' ? (
          Array.from({ length: 7 }, (_, i) => <span key={i} aria-hidden="true" className="h-[52px] w-[68px] animate-pulse rounded-[11px] bg-rail-hi" />)
        ) : status === 'error' ? (
          <span role="alert" className="flex items-center gap-3 px-3 py-2 text-xs">
            Não foi possível carregar o menu.
            <button type="button" onClick={retry} className="font-semibold text-rail-ring">Tentar de novo</button>
          </span>
        ) : areas.map((area) =>
          mobile ? (
            <Dialog.Root key={area.id} open={open === area.id} onOpenChange={(o) => setOpen(o ? area.id : null)}>
              <Dialog.Trigger asChild>
                <AreaButton area={area} active={area.id === activeAreaId} />
              </Dialog.Trigger>
              <Dialog.Portal>
                <Dialog.Overlay className="fixed inset-0 z-30 bg-foreground/30" />
                <Dialog.Content className="fixed inset-x-0 bottom-0 z-40 rounded-t-2xl border-t border-border bg-popover p-3 pb-[calc(16px+env(safe-area-inset-bottom,0px))] text-popover-foreground">
                  <Dialog.Title className="sr-only">{area.name}</Dialog.Title>
                  <Dialog.Description className="sr-only">Módulos da área {area.name}</Dialog.Description>
                  <AreaMenu area={area} onNavigate={() => setOpen(null)} />
                </Dialog.Content>
              </Dialog.Portal>
            </Dialog.Root>
          ) : (
            <Popover.Root key={area.id} open={open === area.id} onOpenChange={(o) => setOpen(o ? area.id : null)}>
              <Popover.Trigger asChild>
                <AreaButton area={area} active={area.id === activeAreaId} />
              </Popover.Trigger>
              <Popover.Portal>
                <Popover.Content side="top" sideOffset={10} className="z-40 w-64 rounded-md border border-border bg-popover p-2 text-popover-foreground shadow-lg" aria-label={area.name}>
                  <AreaMenu area={area} onNavigate={() => setOpen(null)} />
                </Popover.Content>
              </Popover.Portal>
            </Popover.Root>
          ),
        )}
      </nav>
    </div>
  )
}
