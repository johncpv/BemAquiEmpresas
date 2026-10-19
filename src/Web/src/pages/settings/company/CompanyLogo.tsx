import { useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { getGetCompanyProfileQueryKey, getGetTenantPublicContextQueryKey, uploadCompanyLogo } from '@/api/generated/bemaqui'
import type { CompanyProfileDto } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { StatusChip } from '@/components/ui/status-chip'
import { toastDone } from '@/components/ui/toast'

/**
 * Logo da empresa (CAD-06 CA 5 e 6): PNG, JPG ou WebP de até 1 MB e 1024 px. O novo fica "Aguardando verificação" até o
 * antivírus liberar; até lá, o anterior continua no topo e no login.
 */
export function CompanyLogo({ profile, canEdit }: { profile: CompanyProfileDto; canEdit: boolean }) {
  const client = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const logo = profile.logo

  const upload = async (file: File) => {
    setBusy(true)
    setError(null)
    try {
      const result = await uploadCompanyLogo({ file })
      toastDone(result.pendingNew ? 'Logo enviado. Aguardando verificação' : 'Logo trocado')
      await client.invalidateQueries({ queryKey: getGetCompanyProfileQueryKey() })
      await client.invalidateQueries({ queryKey: getGetTenantPublicContextQueryKey() })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível enviar o logo.')
    } finally {
      setBusy(false)
      if (input.current) input.current.value = ''
    }
  }

  return (
    <div className="space-y-3 sm:col-span-2">
      <FormError message={error ?? undefined} />
      <div className="flex flex-wrap items-center gap-4">
        <div className="grid size-24 place-items-center rounded-md border border-border bg-card">
          {logo.version ? <img src={`/api/v1/public/tenant-logo?v=${logo.version}`} alt="Logo atual da empresa" className="max-h-20 max-w-20 object-contain" />
            : <span className="text-xs text-muted-foreground">Sem logo</span>}
        </div>
        <div className="space-y-2">
          {logo.pendingNew ? <StatusChip tone="info">Aguardando verificação</StatusChip> : null}
          <p className="text-sm text-muted-foreground">PNG, JPG ou WebP, até 1 MB e 1024 px no maior lado. Aparece no topo do sistema, no login e nos documentos.</p>
          {canEdit ? (
            profile.exists ? (
              <>
                <input ref={input} type="file" accept=".png,.jpg,.jpeg,.webp,image/png,image/jpeg,image/webp" className="sr-only" tabIndex={-1} aria-hidden="true" onChange={(e) => e.target.files?.[0] && void upload(e.target.files[0])} />
                <button type="button" onClick={() => input.current?.click()} disabled={busy} className="rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60">{busy ? 'Enviando…' : 'Trocar logo'}</button>
              </>
            ) : <p className="text-sm">Salve os dados da empresa para enviar o logo.</p>
          ) : null}
        </div>
      </div>
    </div>
  )
}
