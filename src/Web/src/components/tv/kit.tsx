import { Maximize, Minimize, Radio, WifiOff } from 'lucide-react'
import { type ReactNode, useEffect, useState } from 'react'
import { cn } from '@/lib/utils'
import { clockText, TV_TEXT } from '@/pages/tv/tv'

/**
 * Kit TV base (PNL-01a CA 18, D18, ADR 0030 §5): leitura a distância com os tokens --tv-title (32 px), --tv-line (28 px) e --tv-count
 * (48 px), que crescem com a altura da tela; sempre no tema escuro da superfície /tv. Os cartões, as colunas e as visões são da PNL-01b.
 */

export type TvConnection = 'connecting' | 'live' | 'offline'

/** Relógio de 48 px, em America/Sao_Paulo, atualizado a cada segundo. */
export function TvClock({ now: fixed }: { now?: Date }) {
  const [now, setNow] = useState(() => fixed ?? new Date())
  useEffect(() => {
    if (fixed) return
    const timer = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(timer)
  }, [fixed])
  return (
    <time dateTime={now.toISOString()} aria-label={`Agora ${clockText(now)}`} className="font-display text-[length:var(--tv-count)] font-extrabold leading-none tabular-nums font-wider">
      {clockText(now)}
    </time>
  )
}

/** Estado da conexão: "Ao vivo"; sem conexão, "Reconectando…" e "Atualizado às {hh:mm}" (os dados continuam na tela). */
export function TvConnectionStatus({ status, updatedAt }: { status: TvConnection; updatedAt: Date | null }) {
  const live = status === 'live'
  return (
    <div role="status" aria-live="polite" className="flex flex-col items-end gap-1">
      <span data-status={status} className={cn('inline-flex items-center gap-3 rounded-full px-4 py-1 text-[length:var(--tv-line)] font-semibold', live ? 'bg-ok-soft text-ok-ink' : 'bg-warn-soft text-warn-ink')}>
        {live ? <Radio aria-hidden="true" className="size-[0.9em]" /> : <WifiOff aria-hidden="true" className="size-[0.9em]" />}
        {live ? TV_TEXT.live : TV_TEXT.reconnecting}
      </span>
      {!live && updatedAt ? <span className="text-[length:var(--tv-line)] text-muted-foreground tabular-nums">Atualizado às {clockText(updatedAt)}</span> : null}
    </div>
  )
}

/** Cabeçalho da TV: nome da linha (título de 32 px), relógio de 48 px, estado da conexão e as ações (tela cheia). */
export function TvHeader({ title, status, updatedAt, actions, now }: { title: string; status: TvConnection; updatedAt: Date | null; actions?: ReactNode; now?: Date }) {
  return (
    <header className="flex flex-wrap items-center justify-between gap-6 border-b border-border pb-4">
      <h1 className="min-w-0 font-display text-[length:var(--tv-title)] font-extrabold leading-tight font-wider">{title}</h1>
      <div className="flex flex-wrap items-center gap-6">
        <TvConnectionStatus status={status} updatedAt={updatedAt} />
        <TvClock now={now} />
        {actions}
      </div>
    </header>
  )
}

/** "Tela cheia": a TV fica sem a barra do navegador. Some quando o navegador não permite. */
export function FullscreenButton() {
  const [full, setFull] = useState(() => typeof document !== 'undefined' && !!document.fullscreenElement)
  useEffect(() => {
    const change = () => setFull(!!document.fullscreenElement)
    document.addEventListener('fullscreenchange', change)
    return () => document.removeEventListener('fullscreenchange', change)
  }, [])
  if (typeof document === 'undefined' || !document.fullscreenEnabled) return null
  const toggle = () => void (full ? document.exitFullscreen() : document.documentElement.requestFullscreen()).catch(() => undefined)
  return (
    <button type="button" onClick={toggle} className="inline-flex min-h-14 items-center gap-2 rounded-md border border-border px-4 text-[length:var(--tv-line)] font-semibold">
      {full ? <Minimize aria-hidden="true" className="size-[0.9em]" /> : <Maximize aria-hidden="true" className="size-[0.9em]" />}
      {full ? 'Sair da tela cheia' : 'Tela cheia'}
    </button>
  )
}

/** Aviso de tela inteira da TV (link inválido, desligada, indisponível...), em letra grande e sem botão para quem está longe. */
export function TvNotice({ title, hint, tone = 'info' }: { title: string; hint?: string; tone?: 'info' | 'crit' }) {
  return (
    <section role="alert" aria-live="assertive" className="grid min-h-[70dvh] place-items-center text-center">
      <div className="max-w-[28ch] space-y-6">
        <p className={cn('font-display text-[length:var(--tv-title)] font-extrabold leading-tight font-wider', tone === 'crit' ? 'text-crit-ink' : 'text-foreground')}>{title}</p>
        {hint ? <p className="text-[length:var(--tv-line)] text-muted-foreground">{hint}</p> : null}
      </div>
    </section>
  )
}
