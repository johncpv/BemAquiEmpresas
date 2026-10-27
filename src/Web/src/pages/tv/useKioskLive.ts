import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import type { TvConnection } from '@/components/tv/kit'

export const KIOSK_HUB = '/hubs/kiosk'
export const ORDER_BOARD_CHANGED = 'OrderBoardChanged'
export const SCHEDULE_CHANGED = 'ScheduleChanged'
export const KIOSK_CONFIG_CHANGED = 'KioskConfigChanged'
export const KIOSK_REVOKED = 'KioskRevoked'
const RESTART_MS = 15_000

export interface KioskLiveHandlers {
  /** Reconectou: a TV recarrega tudo pela API de quiosque. */
  onChange: () => void
  /** OS mudou (OrderBoardChanged, só o id): a TV recarrega o cartão dela, agrupado em 300 ms (PNL-01b CA 8 e 9). */
  onOrder: (orderId: string) => void
  /** Plano novo (ScheduleChanged, sem corpo): a fila, os atrasos e as datas previstas recarregam (PNL-01b CA 8). */
  onSchedule: () => void
  /** Configuração nova da tela (linha, visões, rotação). */
  onConfig: () => void
  /** Fim da sessão: revogada, novo link ou painel indisponível. */
  onEnded: (reason: string | undefined, company: string | undefined) => void
}

/**
 * Tempo real da TV (PNL-01a CA 12 a 15, ADR 0030 §3): hub /hubs/kiosk só com o cookie da sessão (nenhum token na URL); os grupos são
 * do servidor. Caiu a conexão: "Reconectando…" e nova tentativa a cada 15 s; ao reconectar, recarrega.
 */
export function useKioskLive(enabled: boolean, handlers: KioskLiveHandlers): TvConnection {
  const [status, setStatus] = useState<TvConnection>('connecting')
  const ref = useRef(handlers)
  useEffect(() => { ref.current = handlers }, [handlers])

  useEffect(() => {
    if (!enabled) return
    let disposed = false
    let restart: ReturnType<typeof setTimeout> | null = null
    const connection = new HubConnectionBuilder().withUrl(KIOSK_HUB).withAutomaticReconnect().configureLogging(LogLevel.None).build()
    const scheduleRestart = () => {
      if (restart || disposed) return
      restart = setTimeout(() => {
        restart = null
        if (connection.state === HubConnectionState.Disconnected) void start(true)
      }, RESTART_MS)
    }
    const start = async (reload: boolean) => {
      try {
        await connection.start()
        if (disposed) {
          void connection.stop()
          return
        }

        setStatus('live')
        if (reload) ref.current.onChange()
      } catch {
        if (!disposed) {
          setStatus('offline')
          scheduleRestart()
        }
      }
    }

    connection.on(ORDER_BOARD_CHANGED, (message: { productionOrderId?: string } | undefined) => {
      if (message?.productionOrderId) ref.current.onOrder(message.productionOrderId)
      else ref.current.onChange()
    })
    connection.on(SCHEDULE_CHANGED, () => ref.current.onSchedule())
    connection.on(KIOSK_CONFIG_CHANGED, () => ref.current.onConfig())
    connection.on(KIOSK_REVOKED, (message: { reason?: string; companyName?: string } | undefined) => ref.current.onEnded(message?.reason, message?.companyName))
    connection.onreconnecting(() => setStatus('offline'))
    connection.onreconnected(() => {
      setStatus('live')
      ref.current.onChange()
    })
    connection.onclose(() => {
      if (disposed) return
      setStatus('offline')
      scheduleRestart()
    })
    void start(false)

    return () => {
      disposed = true
      if (restart) clearTimeout(restart)
      void connection.stop()
    }
  }, [enabled])

  return status
}
