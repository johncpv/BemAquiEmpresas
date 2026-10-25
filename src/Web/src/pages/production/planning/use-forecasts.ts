import { useQueries } from '@tanstack/react-query'
import { createContext, useContext, useMemo } from 'react'
import { getListOrderForecastsQueryKey, listOrderForecasts } from '@/api/generated/bemaqui'
import type { OrderForecastInfo } from '@/api/generated/model'
import { chunks } from './planning'

/** Previsão das OS pedidas, lida do plano gravado (CA 16), em lotes; o mapa vem vazio enquanto carrega ou sem permissão. */
export function useOrderForecasts(ids: readonly string[], enabled = true): Map<string, OrderForecastInfo> {
  const sorted = useMemo(() => [...new Set(ids)].sort(), [ids])
  return useQueries({
    queries: chunks(sorted).map((chunk) => ({
      queryKey: getListOrderForecastsQueryKey({ ids: chunk }),
      queryFn: ({ signal }: { signal: AbortSignal }) => listOrderForecasts({ ids: chunk }, { signal }),
      enabled: enabled && chunk.length > 0,
      staleTime: 30_000,
    })),
    combine: (results) => new Map(results.flatMap((r) => (Array.isArray(r.data) ? r.data : [])).map((f) => [f.orderId, f])),
  })
}

/** Previsões do Kanban, carregadas uma vez para os cartões visíveis. */
export const ForecastsContext = createContext<Map<string, OrderForecastInfo>>(new Map())

export function useForecast(orderId: string): OrderForecastInfo | undefined {
  return useContext(ForecastsContext).get(orderId)
}
