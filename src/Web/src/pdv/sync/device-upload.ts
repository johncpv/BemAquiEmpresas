import { getPostPosSyncEventsUrl, postPosSyncEvents } from '@/api/generated/bemaqui'
import type { PosHandshake } from '@/api/generated/model'
import { ApiError, OFFLINE_CODE } from '@/api/http'
import { loadDeviceKey, loadDocumentKey, signRequest } from '@/pdv/device-signing'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { recordServerTime } from '@/pdv/offline/offline-access'
import { aesDocumentCipher } from '@/pdv/sale/customer-document'
import { type UploadBody, uploadOutbox, type UploadResponse, type UploadSummary } from './event-upload'

/** Aperto de mão guardado no aparelho (PdvPage): o caixa, a loja, a época e o id do aparelho. */
type SavedRegister = { register: PosHandshake }

/**
 * Adaptador real da subida (OFF-02): a chave do aparelho assina cada lote (`POST /api/v1/pos/sync/events`, esquema `Device`, sem operador), o
 * aparelho, o caixa e a época vêm do último aperto de mão guardado, e o documento do consumidor é decifrado só para o bloco à parte do lote.
 * Aparelho bloqueado ou trocado continua subindo o que tinha gravado (o servidor guarda em quarentena). Sem chave ou sem aperto de mão: nada a
 * fazer (o aparelho ainda não é um caixa).
 */
export async function uploadDeviceEvents(store: LocalStore): Promise<UploadSummary | null> {
  if (!store.writable) return null
  const [keys, saved] = await Promise.all([loadDeviceKey(), store.getMeta<SavedRegister>('register')])
  if (!keys || !saved?.register) return null
  if ((await store.pendingSummary()).count === 0) return { sent: 0, withDivergence: 0, quarantined: 0, remaining: 0 }
  const cipher = aesDocumentCipher(loadDocumentKey)
  const url = getPostPosSyncEventsUrl()
  return uploadOutbox({
    store,
    device: { deviceId: saved.register.deviceId, registerId: saved.register.registerId, deviceEpoch: saved.register.epoch },
    send: async (body: UploadBody) => {
      const response = await postPosSyncEvents(body, { headers: await signRequest(keys, 'POST', url, JSON.stringify(body)) })
      // OFF-03 CA 14: o horário do servidor vira o piso da janela sem sincronizar.
      if (response.serverTime) await recordServerTime(store, response.serverTime).catch(() => undefined)
      return response as UploadResponse
    },
    document: async (documentId) => {
      const record = await store.getCustomerDocument(documentId)
      return record ? { kind: record.kind, value: await cipher.decrypt(record) } : null
    },
  })
}

/** O que a faixa de estado diz quando a subida falha (sem rede não é problema: a fila espera). */
export function uploadIssue(error: unknown): string | null {
  if (error instanceof ApiError) {
    if (error.status === 0 && error.code === OFFLINE_CODE) return null
    if (error.code === 'device_clock_skew') return 'as vendas não sobem: acerte o relógio do aparelho'
    if (error.code === 'device_not_registered') return 'as vendas não sobem: registre o aparelho de novo'
    return `as vendas não subiram: ${error.message}`
  }

  return 'as vendas não subiram; o caixa tenta de novo sozinho'
}
