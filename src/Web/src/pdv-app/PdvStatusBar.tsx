import { CloudOff, RefreshCw, Wifi } from 'lucide-react'
import type { StatusView } from '@/pdv/status-bar'
import { cn } from '@/lib/utils'

const tones: Record<StatusView['tone'], string> = {
  ok: 'border-ok-soft bg-ok-soft text-ok-ink',
  warn: 'border-warn bg-warn-soft text-warn-ink',
  crit: 'border-crit-soft bg-crit-soft text-crit-ink',
}

/**
 * Faixa de estado fixa no topo do /pdv (OFF-01 CA 2 e 13): rede, vendas a enviar com a idade da mais antiga e a hora da última sincronização.
 * O texto vem de `statusView` (src/pdv/status-bar.ts), testado à parte.
 */
export function PdvStatusBar({ view }: { view: StatusView }) {
  const Icon = view.title.startsWith('Sem conexão') ? CloudOff : view.tone === 'ok' ? Wifi : RefreshCw
  return (
    <div role="status" aria-live="polite" aria-label="Estado do caixa" data-tone={view.tone}
      className={cn('sticky top-0 z-30 border-b px-4 py-2 text-[16px]', tones[view.tone])}>
      <div className="mx-auto flex max-w-2xl flex-wrap items-center gap-x-3 gap-y-1">
        <Icon aria-hidden="true" className="size-5 shrink-0" />
        <span className="font-semibold">{view.title}</span>
        {view.details.length ? <span className="text-[15px]">{view.details.join(' · ')}</span> : null}
      </div>
    </div>
  )
}
