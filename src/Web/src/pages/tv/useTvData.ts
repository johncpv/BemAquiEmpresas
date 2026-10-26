import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef } from 'react'
import {
  getGetKioskBoardQueryKey, getGetKioskDelaysQueryKey, getGetKioskPreviewBoardQueryKey, getGetKioskPreviewDelaysQueryKey, getGetKioskPreviewQueueQueryKey,
  getGetKioskQueueQueryKey, getKioskBoardOrder, getKioskPreviewBoardOrder, useGetKioskBoard, useGetKioskDelays, useGetKioskPreviewBoard, useGetKioskPreviewDelays,
  useGetKioskPreviewQueue, useGetKioskQueue,
} from '@/api/generated/bemaqui'
import type { KioskBoard, KioskCard } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { createBatcher, PUSH_DEBOUNCE_MS } from '@/pages/production/kanban/useBoardLive'
import { patchBoard } from './panel'
import type { TvData } from './TvSurface'

/** De onde vêm as visões: a sessão de quiosque da TV ou a prévia da tela no backoffice (mesmo DTO). */
export type TvSource = { kind: 'kiosk' } | { kind: 'preview'; id: string }

/** Reserva sem tempo real (ADR 0009): as visões recarregam a cada 60 s. */
const STALE_MS = 0

/**
 * Dados das visões (PNL-01b CA 8 e 9): as três consultas da tela (só as visões dela) e a recarga agrupada por cartão: os ids que
 * chegam pelo OrderBoardChanged em 300 ms viram um GET do cartão cada, trocado no quadro sem recarregar a tela inteira; a OS que saiu da
 * linha (404) sai do quadro. O ScheduleChanged recarrega a fila, os atrasos e o quadro (datas previstas novas).
 */
export function useTvData(source: TvSource, views: readonly string[], enabled: boolean) {
  const client = useQueryClient()
  const kiosk = source.kind === 'kiosk'
  const id = source.kind === 'preview' ? source.id : ''
  const query = (view: string, on: boolean) => ({ query: { enabled: enabled && on && views.includes(view), retry: false, staleTime: STALE_MS, refetchOnWindowFocus: false } })

  const board = { kiosk: useGetKioskBoard(query('board', kiosk)), preview: useGetKioskPreviewBoard(id, query('board', !kiosk)) }
  const queue = { kiosk: useGetKioskQueue(query('queue', kiosk)), preview: useGetKioskPreviewQueue(id, query('queue', !kiosk)) }
  const delays = { kiosk: useGetKioskDelays(query('late', kiosk)), preview: useGetKioskPreviewDelays(id, query('late', !kiosk)) }
  const pick = <T,>(pair: { kiosk: T; preview: T }) => (kiosk ? pair.kiosk : pair.preview)

  const keys = useMemo(() => kiosk
    ? { board: getGetKioskBoardQueryKey(), queue: getGetKioskQueueQueryKey(), delays: getGetKioskDelaysQueryKey() }
    : { board: getGetKioskPreviewBoardQueryKey(id), queue: getGetKioskPreviewQueueQueryKey(id), delays: getGetKioskPreviewDelaysQueryKey(id) }, [kiosk, id])

  const reloadAll = useCallback(() => {
    for (const key of [keys.board, keys.queue, keys.delays]) void client.invalidateQueries({ queryKey: key })
  }, [client, keys])

  const reloadPlan = reloadAll

  const fetchCard = useCallback(async (orderId: string): Promise<KioskCard | null> => {
    try {
      return kiosk ? await getKioskBoardOrder(orderId) : await getKioskPreviewBoardOrder(id, orderId)
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) return null
      throw error
    }
  }, [kiosk, id])

  const flush = useCallback(async (ids: string[]) => {
    if (!views.includes('board')) {
      void client.invalidateQueries({ queryKey: keys.delays })
      return
    }

    try {
      const cards = await Promise.all(ids.map(async (orderId) => [orderId, await fetchCard(orderId)] as const))
      let reload = false
      client.setQueryData<KioskBoard>(keys.board, (old) => {
        if (!old) return old
        let current: KioskBoard | null = old
        for (const [orderId, card] of cards) {
          current = current ? patchBoard(current, orderId, card) : null
        }

        if (!current) {
          reload = true
          return old
        }

        return current
      })
      if (reload) void client.invalidateQueries({ queryKey: keys.board })
    } catch {
      void client.invalidateQueries({ queryKey: keys.board })
    }

    void client.invalidateQueries({ queryKey: keys.delays })
  }, [client, keys, fetchCard, views])

  const flushRef = useRef(flush)
  useEffect(() => { flushRef.current = flush }, [flush])
  const batcher = useRef<ReturnType<typeof createBatcher> | null>(null)
  useEffect(() => {
    const created = createBatcher((ids) => void flushRef.current(ids), PUSH_DEBOUNCE_MS)
    batcher.current = created
    return () => {
      created.cancel()
      batcher.current = null
    }
  }, [])
  const onOrder = useCallback((orderId: string) => batcher.current?.push(orderId), [])

  const current = { board: pick(board), queue: pick(queue), delays: pick(delays) }
  const data: TvData = current
  const errors = [current.board.error, current.queue.error, current.delays.error].filter((e) => e !== null && e !== undefined)
  const updatedAt = Math.max(current.board.dataUpdatedAt, current.queue.dataUpdatedAt, current.delays.dataUpdatedAt)
  return { data, errors, updatedAt: updatedAt > 0 ? new Date(updatedAt) : null, reloadAll, reloadPlan, onOrder }
}
