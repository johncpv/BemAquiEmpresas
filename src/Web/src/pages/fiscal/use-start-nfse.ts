import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { createNfse } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'

/**
 * "Emitir NFS-e" a partir da OS de instalação aceita (FIS-06 CA 1 e 2): cria o rascunho (com a chave de idempotência) e abre a NFS-e; se a
 * instalação já tem NFS-e, abre a que existe. Usado na fila "A emitir", no aceite técnico e no "Faturar" do projeto.
 */
export function useStartNfse() {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const start = async (installationOrderId: string, projectId?: string) => {
    setBusy(installationOrderId)
    setError(null)
    try {
      const created = await createNfse({ installationOrderId, projectId: projectId ?? null }, idempotency.options)
      idempotency.renew()
      void navigate({ to: '/app/fiscal/saida/nfse/$id', params: { id: created.id } } as never)
    } catch (e) {
      const existing = e instanceof ApiError && typeof e.data.invoiceId === 'string' ? e.data.invoiceId : null
      if (existing) void navigate({ to: '/app/fiscal/saida/nfse/$id', params: { id: existing } } as never)
      else setError(e instanceof ApiError ? e.message : 'Não foi possível criar a NFS-e. Tente de novo.')
    } finally {
      setBusy(null)
    }
  }
  return { start, busy, error }
}
