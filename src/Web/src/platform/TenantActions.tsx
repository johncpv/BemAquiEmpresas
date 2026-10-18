import { useState } from 'react'
import { reactivateTenant, resumeTenantProvisioning, suspendTenant } from '@/api/generated/bemaqui'
import type { TenantRow } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { toastDone } from '@/components/ui/toast'

/** Ações do tenant (FND-03b CA 4 a 6): retomar, copiar link, suspender com motivo e reativar. */
export function TenantActions({ tenant, host, failed, refresh }: { tenant: TenantRow; host?: string; failed: boolean; refresh: () => Promise<void> }) {
  const [reason, setReason] = useState('')
  const name = tenant.tradeName || tenant.legalName
  return (
    <div className="flex flex-wrap gap-2">
      {tenant.status === 'Provisioning' && failed ? (
        <button type="button" onClick={async () => { await resumeTenantProvisioning(tenant.key); toastDone('Provisionamento retomado'); await refresh() }} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">
          Retomar provisionamento
        </button>
      ) : null}
      {host ? (
        <button type="button" onClick={() => { void navigator.clipboard?.writeText('https://' + host); toastDone('Link copiado') }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">
          Copiar link de acesso
        </button>
      ) : null}
      {tenant.status === 'Active' ? (
        <ConfirmDialog
          verb="Suspender"
          done="Tenant suspenso"
          title={`Suspender ${name}?`}
          destructive
          description={
            <span className="block space-y-2">
              <span className="block">Os usuários perdem o acesso em segundos e veem a página "Acesso suspenso".</span>
              <textarea aria-label="Motivo da suspensão" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Motivo (obrigatório)" className="w-full rounded-md border border-input bg-card p-2 text-sm text-foreground" />
            </span>
          }
          onConfirm={async () => { await suspendTenant(tenant.key, { reason }); setReason(''); await refresh() }}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Suspender</button>}
        />
      ) : null}
      {tenant.status === 'Suspended' ? (
        <ConfirmDialog
          verb="Reativar"
          done="Tenant reativado"
          title={`Reativar ${name}?`}
          description="O acesso dos usuários volta em segundos."
          onConfirm={async () => { await reactivateTenant(tenant.key); await refresh() }}
          trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Reativar</button>}
        />
      ) : null}
    </div>
  )
}
