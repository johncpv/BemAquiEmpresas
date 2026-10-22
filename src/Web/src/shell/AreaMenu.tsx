import { Link } from '@tanstack/react-router'
import { Lock } from 'lucide-react'
import type { KeyboardEvent } from 'react'
import type { NavArea } from '@/navigation/modules'

/** Lista de módulos da área (CA 2, 5): setas movem, Enter abre; Esc fecha pelo Radix e o foco volta para a área. */
export function AreaMenu({ area, onNavigate }: { area: NavArea; onNavigate: () => void }) {
  const move = (event: KeyboardEvent<HTMLUListElement>) => {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return
    const links = [...event.currentTarget.querySelectorAll<HTMLAnchorElement>('a')]
    const index = links.indexOf(document.activeElement as HTMLAnchorElement)
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? links.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length
    links[next]?.focus()
    event.preventDefault()
  }

  return (
    <div>
      <p className="px-2 pb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">{area.name}</p>
      {area.modules.length === 0 ? (
        <p className="px-2 py-3 text-sm text-muted-foreground">Nenhum módulo nesta área por enquanto.</p>
      ) : (
        <ul className="space-y-0.5" onKeyDown={move}>
          {area.modules.map((m) => (
            <li key={m.code}>
              <Link
                to={m.path as never}
                onClick={onNavigate}
                aria-label={m.locked ? `${m.name}, não liberado` : m.count ? `${m.name}, ${m.count} pendentes` : undefined}
                className="flex items-center gap-3 rounded-md px-2 py-2 text-sm font-medium text-foreground hover:bg-muted focus-visible:bg-muted"
              >
                <m.icon aria-hidden="true" className="size-[18px] text-muted-foreground" />
                <span className={m.locked ? 'flex-1 text-muted-foreground' : 'flex-1'}>{m.name}</span>
                {m.locked ? <Lock aria-hidden="true" className="size-3.5 text-muted-foreground" /> : null}
                {m.count ? (
                  <span aria-hidden="true" className="rounded-full bg-signal px-1.5 text-[10px] font-extrabold text-signal-foreground tabular-nums">
                    {m.count}
                  </span>
                ) : null}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
