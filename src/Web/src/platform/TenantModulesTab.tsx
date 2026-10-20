import * as Switch from '@radix-ui/react-switch'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetTenantModulesQueryKey, saveTenantModules, useGetTenantModules } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { notifySuccess } from '@/components/ui/messages'

/** Aba "Módulos" do tenant (FND-06 CA 5 a 8): liberar ou bloquear sem deploy; o que difere do plano vira exceção. */
export function TenantModulesTab({ tenantId }: { tenantId: string }) {
  const client = useQueryClient()
  const modules = useGetTenantModules(tenantId)
  const [changes, setChanges] = useState<Record<string, boolean>>({})
  const [error, setError] = useState<string>()
  if (modules.isPending) return <Skeleton className="h-64" />
  if (modules.isError) return <ErrorState onRetry={() => modules.refetch()} />

  const dirty = Object.keys(changes).length > 0
  const save = async () => {
    setError(undefined)
    try {
      await saveTenantModules(tenantId, { modules: Object.entries(changes).map(([code, released]) => ({ code, released })) })
      setChanges({})
      await client.invalidateQueries({ queryKey: getGetTenantModulesQueryKey(tenantId) })
      notifySuccess('Liberação salva. Vale no próximo acesso dos usuários.')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar a liberação.')
    }
  }

  const areas = [...new Set(modules.data.map((m) => m.area))]
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-end gap-2">
        {dirty ? <StatusChip tone="warn">Alterações não salvas</StatusChip> : null}
        <button type="button" disabled={!dirty} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar liberação</button>
      </div>
      <FormError message={error} />
      {areas.map((area) => (
        <section key={area} aria-labelledby={'area-' + area} className="rounded-md border border-border bg-card">
          <h3 id={'area-' + area} className="border-b border-border px-3 py-2 text-sm font-semibold">{area}</h3>
          <ul className="divide-y divide-border">
            {modules.data.filter((m) => m.area === area).map((m) => {
              const released = changes[m.code] ?? m.released
              return (
                <li key={m.code} className="flex items-center gap-3 px-3 py-2">
                  <Switch.Root
                    checked={released}
                    onCheckedChange={(on) => setChanges((c) => { const n = { ...c }; if (on === m.released) delete n[m.code]; else n[m.code] = on; return n })}
                    aria-label={`Liberar ${m.name}`}
                    className="relative inline-flex h-5 w-9 flex-none items-center rounded-full bg-input data-[state=checked]:bg-primary"
                  >
                    <Switch.Thumb className="block size-4 translate-x-0.5 rounded-full bg-card transition-transform data-[state=checked]:translate-x-[18px]" />
                  </Switch.Root>
                  <span className="min-w-0 flex-1">
                    <b className="block text-sm font-semibold">{m.name}</b>
                    <span className="text-xs text-muted-foreground">{m.description}</span>
                  </span>
                  <StatusChip tone={m.isException ? 'warn' : 'info'}>{m.isException ? 'Exceção' : 'Plano'}</StatusChip>
                </li>
              )
            })}
          </ul>
        </section>
      ))}
    </div>
  )
}
