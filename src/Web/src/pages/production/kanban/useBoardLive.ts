import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { type QueryClient, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { getGetProductionBoardQueryKey } from '@/api/generated/bemaqui'
import { getAccessToken } from '@/api/session'

export const HUB_PATH = '/hubs/production'
export const BOARD_CHANGED = 'OrderBoardChanged'
/** Plano novo gravado (PLN-01b CA 4): só o sinal, sem corpo. */
export const SCHEDULE_CHANGED = 'ScheduleChanged'
/** Recarga agrupada (CA 14): 50 mudanças por minuto não travam a tela. */
export const PUSH_DEBOUNCE_MS = 300
/** Reserva enquanto o tempo real está fora (CA 13, ADR 0009). */
export const FALLBACK_POLL_MS = 60_000
const RESTART_MS = 15_000

export type LiveStatus = 'connecting' | 'live' | 'offline'

/** Junta os ids que chegam em {delay} ms e chama {flush} uma vez com todos (sem repetir id). */
export function createBatcher(flush: (ids: string[]) => void, delay = PUSH_DEBOUNCE_MS) {
  let pending = new Set<string>()
  let timer: ReturnType<typeof setTimeout> | null = null
  return {
    push(id: string) {
      pending.add(id)
      timer ??= setTimeout(() => {
        const ids = [...pending]
        pending = new Set()
        timer = null
        flush(ids)
      }, delay)
    },
    cancel() {
      if (timer) clearTimeout(timer)
      timer = null
      pending = new Set()
    },
  }
}

/**
 * Tempo real do Kanban (PRD-02 CA 12 e 13): o hub manda só o id da OS (OrderBoardChanged); o quadro recarrega pela API, agrupado
 * em 300 ms. Caiu a conexão: "Offline", recarga a cada 60 s pela consulta e nova tentativa; ao reconectar, recarrega o quadro.
 * O token vai pela query do WebSocket (o servidor e o nginx não gravam a query no log, CA 18).
 */
export function useBoardLive(): LiveStatus {
  return useProductionLive(refreshBoard)
}

const refreshBoard = (client: QueryClient) => void client.invalidateQueries({ queryKey: getGetProductionBoardQueryKey() })

/** Mesmo hub para o app do operador (PRD-03a CA 23): recarrega a fila e a etapa abertas pela API. */
export const refreshShopfloor = (client: QueryClient) =>
  void client.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith('/api/v1/shopfloor') })

/** O que escutar e de quanto em quanto agrupar (o Cronograma, PLN-03 CA 12: OrderBoardChanged e ScheduleChanged a cada 5 s). */
export interface LiveOptions {
  schedule?: boolean
  delay?: number
}

/** Conexão com o hub da produção; <c>reload</c> decide o que recarregar quando chega o aviso. */
export function useProductionLive(reload: (client: QueryClient) => void, options: LiveOptions = {}): LiveStatus {
  const { schedule = false, delay = PUSH_DEBOUNCE_MS } = options
  const client = useQueryClient()
  const [status, setStatus] = useState<LiveStatus>('connecting')

  useEffect(() => {
    let disposed = false
    let restart: ReturnType<typeof setTimeout> | null = null
    const refresh = () => reload(client)
    const batch = createBatcher(() => refresh(), delay)
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_PATH, { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build()

    const start = async (reload: boolean) => {
      try {
        await connection.start()
        if (disposed) {
          void connection.stop()
          return
        }

        setStatus('live')
        if (reload) refresh()
      } catch {
        if (!disposed) {
          setStatus('offline')
          scheduleRestart()
        }
      }
    }
    const scheduleRestart = () => {
      if (restart || disposed) return
      restart = setTimeout(() => {
        restart = null
        if (connection.state === HubConnectionState.Disconnected) void start(true)
      }, RESTART_MS)
    }

    connection.on(BOARD_CHANGED, (message: { productionOrderId?: string } | undefined) => {
      if (message?.productionOrderId) batch.push(message.productionOrderId)
    })
    if (schedule) connection.on(SCHEDULE_CHANGED, () => batch.push('plano'))
    connection.onreconnecting(() => setStatus('offline'))
    connection.onreconnected(() => {
      setStatus('live')
      refresh()
    })
    connection.onclose(() => {
      if (disposed) return
      setStatus('offline')
      scheduleRestart()
    })
    void start(false)

    return () => {
      disposed = true
      batch.cancel()
      if (restart) clearTimeout(restart)
      void connection.stop()
    }
  }, [client, reload, schedule, delay])

  return status
}
