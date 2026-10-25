import { useQueryClient } from '@tanstack/react-query'
import { getGetInventoryCountDivergencesQueryKey, getGetInventoryCountingQueryKey, getGetInventoryCountQueryKey } from '@/api/generated/bemaqui'

/** Recarrega o inventário, a contagem e as divergências depois de uma ação (EST-05). */
export function useRefreshCount(id: string) {
  const client = useQueryClient()
  return async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: getGetInventoryCountQueryKey(id) }),
      client.invalidateQueries({ queryKey: getGetInventoryCountingQueryKey(id) }),
      client.invalidateQueries({ queryKey: getGetInventoryCountDivergencesQueryKey(id) }),
    ])
  }
}
