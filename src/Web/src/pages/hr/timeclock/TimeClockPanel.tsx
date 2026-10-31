import { useQueryClient } from '@tanstack/react-query'
import { Clock, Info, MapPinOff, WifiOff } from 'lucide-react'
import { useEffect, useState } from 'react'
import { acceptMyGeoConsent, getGetMyTimeClockQueryKey, recordMyTimeEntry, useGetMyTimeClock } from '@/api/generated/bemaqui'
import type { MyTimeClock, TimeEntryReceipt, TimeEntryType } from '@/api/generated/model'
import { ApiError, OFFLINE_CODE } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { FloorButton, FloorDica, FloorSheet, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { notifySuccess } from '@/components/ui/messages'
import { StatusChip } from '@/components/ui/status-chip'
import {
  currentPosition, dayLabel, ENTRY_TYPES, entryLabel, formatTime, INTERNAL_CONTROL_BANNER, NOT_LINKED, serverClock, serverOffset, TIME_CLOCK_DICAS,
  TIME_CLOCK_OFFLINE,
} from './time-clock'

/** Faixa fixa de controle interno (CA 1), nas três telas do ponto. */
export function InternalControlBanner() {
  return (
    <div role="note" className="flex items-start gap-2 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
      <Info aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <p className="flex-1">{INTERNAL_CONTROL_BANNER}</p>
      <FloorDica nome="Controle interno" texto={TIME_CLOCK_DICAS.controleInterno} />
    </div>
  )
}

/** Relógio do servidor (CA 2): a hora do servidor em São Paulo, andando no aparelho pela diferença medida na carga. */
function ServerClock({ serverNow }: { serverNow: string }) {
  const [text, setText] = useState(() => serverClock(serverOffset(serverNow)))
  useEffect(() => {
    const offset = serverOffset(serverNow)
    const timer = setInterval(() => setText(serverClock(offset)), 1000)
    return () => clearInterval(timer)
  }, [serverNow])
  return (
    <p className="flex items-center gap-1 font-display text-[40px] font-bold font-wider tabular-nums leading-none" aria-label={`Hora do servidor ${text}`}>
      <span aria-hidden="true">{text}</span>
      <FloorDica nome="Hora do servidor" texto={TIME_CLOCK_DICAS.horaServidor} />
    </p>
  )
}

const offline = (e: unknown) => (e instanceof ApiError && e.code === OFFLINE_CODE) || (typeof navigator !== 'undefined' && !navigator.onLine)

/**
 * "Meu ponto" (RH-01a CA 1 a 10), o mesmo layout no tablet (/fab/ponto) e no celular ou computador (/app/meu-ponto): faixa de controle
 * interno, "Olá, {nome}", a hora do servidor, a jornada do dia, as marcações de hoje e o botão grande da próxima marcação; "Outra
 * marcação"; o comprovante de controle interno. Com a geolocalização ligada (só fora do tablet), o aviso antes do primeiro pedido.
 */
export function TimeClockPanel({ onSessionEnded }: { onSessionEnded?: (receipt: TimeEntryReceipt) => void }) {
  const client = useQueryClient()
  const query = useGetMyTimeClock({ query: { retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2 } })
  const idempotency = useIdempotencyKey()
  const [busy, setBusy] = useState<TimeEntryType | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [receipt, setReceipt] = useState<TimeEntryReceipt | null>(null)
  const [choosing, setChoosing] = useState(false)
  const [consentFor, setConsentFor] = useState<TimeEntryType | null>(null)

  if (query.isPending) return <FloorSkeleton rows={2} />
  if (query.isError) {
    const notLinked = query.error instanceof ApiError && query.error.code === 'employee_not_active'
    if (notLinked) return <FloorState title={NOT_LINKED} message="Sem funcionário ativo ligado ao seu usuário, o ponto fica bloqueado." />
    if (offline(query.error)) {
      return <FloorState tone="error" title={TIME_CLOCK_OFFLINE} action={<FloorButton onClick={() => void query.refetch()}><WifiOff aria-hidden="true" className="size-6" />Tentar de novo</FloorButton>} />
    }

    return <FloorState tone="error" title="Não foi possível carregar o seu ponto." message="Verifique a conexão e tente de novo." action={<FloorButton onClick={() => void query.refetch()}>Tentar de novo</FloorButton>} />
  }

  const clock: MyTimeClock = query.data
  const mark = async (type: TimeEntryType, consented = false, noLocation = false) => {
    setChoosing(false)
    setError(null)
    if (typeof navigator !== 'undefined' && !navigator.onLine) {
      setError(TIME_CLOCK_OFFLINE)
      return
    }

    // CA 10: antes do primeiro pedido de localização, o aviso de finalidade; "Entendi e continuar" registra a ciência.
    if (clock.geolocation === 'Ask' && clock.geoConsentNeeded && !consented) {
      setConsentFor(type)
      return
    }

    setBusy(type)
    try {
      const position = clock.geolocation === 'Ask' && !noLocation ? await currentPosition() : null
      const saved = await recordMyTimeEntry({ type, ...(position ?? {}) }, idempotency.options)
      idempotency.renew()
      setReceipt(saved)
      notifySuccess(saved.message)
      if (saved.sessionEnded) {
        onSessionEnded?.(saved)
      } else {
        void client.invalidateQueries({ queryKey: getGetMyTimeClockQueryKey() })
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) idempotency.renew()
      setError(offline(e) ? TIME_CLOCK_OFFLINE : e instanceof ApiError ? e.message : 'Não foi possível registrar. Tente de novo.')
    } finally {
      setBusy(null)
    }
  }

  const acceptConsent = async () => {
    const type = consentFor
    try {
      await acceptMyGeoConsent()
      setConsentFor(null)
      client.setQueryData(getGetMyTimeClockQueryKey(), { ...clock, geoConsentNeeded: false })
      if (type) await mark(type, true)
    } catch (e) {
      setConsentFor(null)
      setError(offline(e) ? TIME_CLOCK_OFFLINE : 'Não foi possível registrar a ciência. Tente de novo.')
    }
  }

  const missing = clock.entries.some((e) => e.location === 'Missing')
  return (
    <div className="space-y-3">
      <InternalControlBanner />
      <section aria-labelledby="meu-ponto-ola" className="space-y-1">
        <h2 id="meu-ponto-ola" className="text-[22px] font-bold">Olá, {clock.firstName}</h2>
        <p className="text-sm text-muted-foreground">{dayLabel(clock.today)} · Jornada: {clock.schedule}</p>
        <ServerClock key={clock.serverNow} serverNow={clock.serverNow} />
      </section>

      {receipt ? (
        <section aria-label={receipt.title} className="rounded-md border-2 border-ok bg-ok-soft p-4 text-ok-ink">
          <p className="text-xs font-semibold uppercase tracking-wide">{receipt.title}</p>
          <p className="mt-1 font-display text-[22px] font-bold tabular-nums">{receipt.text}</p>
          {receipt.location === 'Missing' ? <p className="mt-1 text-sm">Sem localização.</p> : null}
          {receipt.hint ? <p className="mt-1 text-sm">{receipt.hint}</p> : null}
        </section>
      ) : null}

      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-3 text-[18px] font-semibold text-crit-ink">{error}</p> : null}

      {receipt?.sessionEnded ? null : (
        <div className="flex flex-col gap-[8px]">
          <FloorButton className="min-h-[72px] w-full text-[22px]" disabled={!!busy} onClick={() => void mark(clock.suggested)}>
            <Clock aria-hidden="true" className="size-7" />
            {busy === clock.suggested ? 'Registrando…' : clock.suggestedVerb}
          </FloorButton>
          <FloorButton tone="secondary" className="w-full" disabled={!!busy} onClick={() => setChoosing(true)}>Outra marcação</FloorButton>
        </div>
      )}

      {clock.outOfSequence && clock.hint ? (
        <p className="rounded-md border border-warn-soft bg-warn-soft p-3 text-sm font-semibold text-warn-ink">{clock.hint}</p>
      ) : null}

      <section aria-labelledby="marcacoes-hoje">
        <h3 id="marcacoes-hoje" className="text-[18px] font-bold">Marcações de hoje</h3>
        {clock.entries.length === 0 ? (
          <p className="text-[16px] text-muted-foreground">Nenhuma marcação hoje.</p>
        ) : (
          <ul className="mt-1 divide-y divide-border rounded-md border border-border bg-card">
            {clock.entries.map((e) => (
              <li key={e.id} className="flex items-center justify-between gap-2 px-3 py-2 text-[16px]">
                <span className="font-semibold">{entryLabel(e.type)}</span>
                <span className="flex items-center gap-2">
                  {e.location === 'Missing' ? <StatusChip tone="neutral"><MapPinOff aria-hidden="true" className="size-3" />Sem localização</StatusChip> : null}
                  <span className="tabular-nums">{formatTime(e.at)}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
        {missing ? <p className="mt-1 flex items-center text-sm text-muted-foreground">Sem localização<FloorDica nome="Sem localização" texto={TIME_CLOCK_DICAS.semLocalizacao} /></p> : null}
      </section>

      <FloorSheet open={choosing} onOpenChange={setChoosing} title="Outra marcação" description="Escolha o tipo. O horário é sempre o do servidor.">
        <div className="grid gap-[8px]">
          {ENTRY_TYPES.map((t) => (
            <FloorButton key={t.value} tone={t.value === clock.suggested ? 'primary' : 'secondary'} className="w-full" onClick={() => void mark(t.value)}>{t.verb}</FloorButton>
          ))}
        </div>
        <p className="text-[16px] text-muted-foreground">{clock.hint ?? 'Esqueceu uma marcação? Peça o ajuste ao RH.'}</p>
        <FloorButton tone="secondary" className="w-full" onClick={() => setChoosing(false)}>Voltar</FloorButton>
      </FloorSheet>

      <FloorSheet role="alertdialog" open={consentFor !== null} onOpenChange={(o) => { if (!o) setConsentFor(null) }} title="Localização no ponto"
        description={clock.geoConsentText}
        actions={(
          <div className="flex gap-[16px]">
            <FloorButton tone="secondary" className="flex-1" onClick={() => { const type = consentFor; setConsentFor(null); if (type) void mark(type, true, true) }}>Marcar sem localização</FloorButton>
            <FloorButton className="flex-1" onClick={() => void acceptConsent()}>Entendi e continuar</FloorButton>
          </div>
        )} />
    </div>
  )
}
