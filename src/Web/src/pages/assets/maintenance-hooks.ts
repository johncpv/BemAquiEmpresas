import { useQueryClient } from '@tanstack/react-query'
import {
  cancelMaintenanceOrder, completeMaintenanceOrder, getGetAssetMaintenanceQueryKey, getGetAssetQueryKey, getGetMaintenanceOrderQueryKey, getListAssetsQueryKey,
  markMaintenanceChecklistItem, startMaintenanceOrder,
} from '@/api/generated/bemaqui'
import type { MaintenanceChecklistResult, MaintenanceOrderDetail } from '@/api/generated/model'

/** Depois de gravar plano, horímetro ou OS: o bem, a lista de bens, a aba Manutenção, as listas de OS e o histórico se atualizam. */
export function useRefreshMaintenance(assetId?: string) {
  const client = useQueryClient()
  return () => {
    if (assetId) {
      void client.invalidateQueries({ queryKey: getGetAssetQueryKey(assetId) })
      void client.invalidateQueries({ queryKey: getGetAssetMaintenanceQueryKey(assetId) })
    }
    void client.invalidateQueries({ queryKey: getListAssetsQueryKey() })
    void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/assets/maintenance-orders') || String(q.queryKey[0]).startsWith('/api/v1/audit/') })
  }
}

/** Ações da OS (PAT-03a CA 5 a 7), no backoffice e no celular: gravam e atualizam a OS, o bem e as listas. */
export function useOrderActions(order: MaintenanceOrderDetail | undefined) {
  const client = useQueryClient()
  const refresh = useRefreshMaintenance(order?.assetId)
  const apply = (o: MaintenanceOrderDetail) => { client.setQueryData(getGetMaintenanceOrderQueryKey(o.id), o); refresh() }
  return {
    start: async () => apply(await startMaintenanceOrder(order!.id, { version: order!.version })),
    mark: async (itemId: string, result: MaintenanceChecklistResult) => apply(await markMaintenanceChecklistItem(order!.id, itemId, { result })),
    complete: async (workDone: string, meterValue: number | null) => apply(await completeMaintenanceOrder(order!.id, { workDone, meterValue, version: order!.version })),
    cancel: async (reason: string) => apply(await cancelMaintenanceOrder(order!.id, { reason, version: order!.version })),
  }
}
