import * as Popover from '@radix-ui/react-popover'
import { useNavigate } from '@tanstack/react-router'
import { Bell } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { formatDateTime } from '@/lib/format'
import { type NotificationItem, useNotificationsSource } from './notifications'

type State = { status: 'loading' } | { status: 'error' } | { status: 'ready'; items: NotificationItem[]; unread: number }

/** Sino (FND-09b CA 10): lista com título e horário de Brasília; clicar leva ao link e marca como lida. */
export function NotificationsBell() {
  const source = useNotificationsSource()
  const navigate = useNavigate()
  const [state, setState] = useState<State>({ status: 'loading' })

  const load = useCallback(() => {
    source.list().then(
      (r) => setState({ status: 'ready', ...r }),
      () => setState({ status: 'error' }),
    )
  }, [source])

  useEffect(load, [load])

  const unread = state.status === 'ready' ? state.unread : 0
  const open = async (n: NotificationItem) => {
    await source.markRead(n.id)
    load()
    await navigate({ to: n.link })
  }

  return (
    <Popover.Root onOpenChange={(o) => o && load()}>
      <Popover.Trigger
        className="relative grid size-9 place-items-center rounded-sm border border-border bg-card"
        aria-label={unread > 0 ? `${unread} notificações não lidas` : 'Notificações'}
      >
        <Bell aria-hidden="true" className="size-[18px]" strokeWidth={1.7} />
        {unread > 0 ? <span aria-hidden="true" className="absolute right-[7px] top-1.5 size-2 rounded-full border-2 border-card bg-crit" /> : null}
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content align="end" sideOffset={8} className="z-40 w-80 rounded-md border border-border bg-popover p-2 text-popover-foreground shadow-lg">
          <p className="px-2 pb-2 text-sm font-semibold">Notificações</p>
          {state.status === 'loading' ? (
            <div className="space-y-2 p-2" aria-label="Carregando notificações">
              <div className="h-4 animate-pulse rounded bg-muted" />
              <div className="h-4 w-2/3 animate-pulse rounded bg-muted" />
            </div>
          ) : state.status === 'error' ? (
            <div className="space-y-2 p-2 text-sm">
              <p>Não foi possível carregar as notificações.</p>
              <button type="button" onClick={load} className="font-semibold text-primary">Tentar de novo</button>
            </div>
          ) : state.items.length === 0 ? (
            <p className="p-2 text-sm text-muted-foreground">Nada novo por aqui.</p>
          ) : (
            <ul className="max-h-80 overflow-y-auto">
              {state.items.map((n) => (
                <li key={n.id}>
                  <button type="button" onClick={() => open(n)} className="w-full rounded-md px-2 py-2 text-left hover:bg-muted">
                    <span className={n.readAt ? 'text-sm' : 'text-sm font-semibold'}>{n.title}</span>
                    <span className="block text-xs text-muted-foreground tabular-nums">{formatDateTime(n.createdAt)}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}
