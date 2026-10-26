import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { getGetKioskScreenQueryKey, openKioskSession, useGetKioskScreen } from '@/api/generated/bemaqui'
import type { KioskScreenContext } from '@/api/generated/model'
import { FullscreenButton, TvHeader, TvNotice } from '@/components/tv/kit'
import { useWakeLock } from '@/components/tv/wake-lock'
import { stateOfEnd, stateOfError, takeFragmentToken, TV_TEXT, revokedText, type TvState } from './tv'
import { useKioskLive } from './useKioskLive'

/** Revalidação da sessão a cada 30 s (ADR 0030 §2) e recarga de reserva a cada 60 s sem tempo real (ADR 0009). */
export const REVALIDATE_MS = 30_000
export const FALLBACK_RELOAD_MS = 60_000

/** Troca do token desta carga da página (no máximo uma). */
let pairing: Promise<void> | null = null

const VIEW_LABELS: Record<string, string> = { board: 'Quadro por etapa', queue: 'Fila e previsões', late: 'Atrasos' }

/** Estados em que a TV para de chamar a API: só um link novo (ou outra tela) resolve. */
const terminal = (state: TvState) => state.kind === 'invalid' || state.kind === 'legacy' || state.kind === 'revoked' || state.kind === 'reconnect'

/**
 * Painel da TV (/tv, PNL-01a CA 5 a 7, 12 a 15 e 18): troca o token do fragmento pela sessão de quiosque (cookie __Host-bq_kiosk) e
 * apaga o fragmento da barra; depois, só o cookie. Sempre escuro, sem dock, com o cabeçalho do kit TV. As visões são da PNL-01b.
 */
export function TvPage() {
  const client = useQueryClient()
  // Fim vindo da troca do token ou do hub; o resto do estado sai da consulta da tela.
  const [ended, setEnded] = useState<TvState | null>(null)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    let cancelled = false
    // O token vale uma conexão só: a troca fica guardada para a carga da página (o StrictMode roda o efeito duas vezes).
    const token = takeFragmentToken(window.location, window.history)
    if (token) {
      const current = openKioskSession({ token }).then(() => undefined)
      const clear = () => { if (pairing === current) pairing = null }
      pairing = current
      current.then(clear, clear)
    }

    ;(pairing ?? Promise.resolve())
      .then(() => { if (!cancelled) setReady(true) })
      .catch((error: unknown) => { if (!cancelled) setEnded(stateOfError(error, 'a empresa')) })
    return () => { cancelled = true }
  }, [])

  // Revalida a cada 30 s; para quando só um link novo resolve (revogada, inválida, vencida).
  const screen = useGetKioskScreen({
    query: {
      enabled: ready && ended === null,
      retry: false,
      staleTime: 0,
      refetchIntervalInBackground: true,
      refetchInterval: (query) => (query.state.status === 'error' && terminal(stateOfError(query.state.error, '')) ? false : REVALIDATE_MS),
    },
  })
  const company = screen.data?.companyName ?? 'a empresa'
  const state: TvState = ended ?? (screen.isError ? stateOfError(screen.error, company) : screen.data ? { kind: 'panel' } : { kind: 'connecting' })
  const updatedAt = screen.dataUpdatedAt > 0 ? new Date(screen.dataUpdatedAt) : null
  useWakeLock(state.kind === 'panel')

  const reload = useCallback(() => void client.invalidateQueries({ queryKey: getGetKioskScreenQueryKey() }), [client])
  const handlers = useMemo(() => ({
    onChange: reload,
    onConfig: reload,
    onEnded: (reason: string | undefined, name: string | undefined) => setEnded(stateOfEnd(reason, name ?? company)),
  }), [reload, company])
  const live = useKioskLive(state.kind === 'panel', handlers)

  // Reserva sem tempo real: recarrega a cada 60 s enquanto o hub está fora.
  useEffect(() => {
    if (state.kind !== 'panel' || live === 'live') return
    const timer = setInterval(reload, FALLBACK_RELOAD_MS)
    return () => clearInterval(timer)
  }, [state.kind, live, reload])

  useEffect(() => {
    document.title = screen.data ? `${screen.data.name} · Painel da TV` : 'Painel da TV'
  }, [screen.data])

  switch (state.kind) {
    case 'connecting':
      return <TvNotice title={TV_TEXT.connecting} />
    case 'invalid':
      return <TvNotice title={TV_TEXT.invalid} hint={TV_TEXT.invalidHint} tone="crit" />
    case 'legacy':
      return <TvNotice title={TV_TEXT.legacy} tone="crit" />
    case 'reconnect':
      return <TvNotice title={TV_TEXT.reconnect} />
    case 'revoked':
      return <TvNotice title={revokedText(state.company)} tone="crit" />
    case 'unavailable':
      return <TvNotice title={TV_TEXT.unavailable} />
    case 'network':
      return <TvNotice title={TV_TEXT.network} tone="crit" />
    default:
      return screen.data ? <TvPanel screen={screen.data} status={live} updatedAt={updatedAt} /> : <TvNotice title={TV_TEXT.connecting} />
  }
}

/** Base do painel conectado: cabeçalho do kit TV e a área das visões (preenchida pela PNL-01b). */
function TvPanel({ screen, status, updatedAt }: { screen: KioskScreenContext; status: 'connecting' | 'live' | 'offline'; updatedAt: Date | null }) {
  return (
    <div className="flex min-h-[calc(100dvh-64px)] flex-col gap-8">
      <TvHeader title={screen.line ?? 'Todas as linhas'} status={status} updatedAt={updatedAt} actions={<FullscreenButton />} />
      <section aria-label="Visões do painel" className="grid flex-1 place-items-center rounded-md border border-dashed border-border p-8 text-center">
        <div className="space-y-4">
          <p className="font-display text-[length:var(--tv-title)] font-extrabold font-wider">{screen.name}</p>
          <p className="text-[length:var(--tv-line)] text-muted-foreground">Tela conectada. As visões aparecem aqui: {(screen.views ?? []).map((v) => VIEW_LABELS[v] ?? v).join(', ')}.</p>
        </div>
      </section>
    </div>
  )
}

/** /tv/{qualquer coisa}: o link antigo, com o token no caminho, foi desligado (CA 17). Nenhuma chamada à API. */
export function TvLegacyPage() {
  useEffect(() => {
    // O caminho antigo pode ter o token: sai da barra e do histórico.
    window.history.replaceState(window.history.state, '', '/tv/link-antigo')
    document.title = 'Painel da TV'
  }, [])
  return <TvNotice title={TV_TEXT.legacy} tone="crit" />
}
