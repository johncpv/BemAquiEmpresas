import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import {
  addTenantProfile, getGetTenantModulesQueryKey, getGetTenantProfilesQueryKey, removeTenantProfile, useGetTenantProfiles, useListBusinessProfiles,
} from '@/api/generated/bemaqui'
import type { TenantProfileEntry } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { useHelpTab } from '@/help/help-context'
import { formatDateTime } from '@/lib/format'
import { DEFAULT_SEGMENT, SEGMENT_REQUIRED } from './profile-rules'

const label = (p: Pick<TenantProfileEntry, 'name' | 'segmentName'>) => (p.segmentName ? `${p.name} · ${p.segmentName}` : p.name)

/**
 * Aba "Perfis" do tenant (PERF-02 CA 6 a 9): perfis ativos com segmento, data e quem aplicou; adicionar (idempotente) e remover (só
 * oculta, nunca apaga dado; o último não sai). O histórico embaixo é a auditoria de aplicar e remover.
 */
export function TenantProfilesTab({ tenantId }: { tenantId: string }) {
  useHelpTab('perfis')
  const client = useQueryClient()
  const tab = useGetTenantProfiles(tenantId)
  const catalog = useListBusinessProfiles({ query: { staleTime: Infinity } })
  const [profile, setProfile] = useState('')
  const [segment, setSegment] = useState<string | null>(null)

  if (tab.isPending) return <Skeleton className="h-64" />
  if (tab.isError) return <ErrorState title="Não foi possível carregar os perfis." onRetry={() => tab.refetch()} />

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetTenantProfilesQueryKey(tenantId) })
    await client.invalidateQueries({ queryKey: getGetTenantModulesQueryKey(tenantId) })
  }
  const chosen = catalog.data?.find((p) => p.code === profile)
  const segmentOptions = chosen ? [...(SEGMENT_REQUIRED.has(chosen.code) ? [] : [{ code: null as string | null, name: 'Em geral' }]), ...chosen.segments] : []
  const { active, history } = tab.data
  const last = active.length === 1

  return (
    <div className="space-y-4">
      <section aria-labelledby="perfis-ativos" className="rounded-md border border-border bg-card">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border px-3 py-2">
          <h3 id="perfis-ativos" className="text-sm font-semibold">Perfis de negócio</h3>
          <ConfirmDialog
            verb="Adicionar"
            done=""
            title="Adicionar perfil"
            description="Os módulos do perfil que o plano cobre passam a aparecer para a empresa. As exceções da aba Módulos continuam como estão."
            onOpen={() => { setProfile(''); setSegment(null) }}
            preconditions={[{ ok: !!profile, text: 'Escolha o perfil.' }]}
            onConfirm={async () => {
              const result = await addTenantProfile(tenantId, { profile, segment })
              await refresh()
              notifySuccess(result.message)
            }}
            trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Adicionar perfil</button>}
          >
            <label className="block text-sm">
              <span className="mb-1 block font-medium">Perfil</span>
              <select
                value={profile}
                onChange={(e) => { setProfile(e.target.value); setSegment(DEFAULT_SEGMENT[e.target.value] ?? null) }}
                className="w-full rounded-md border border-input bg-card p-2 text-sm text-foreground"
              >
                <option value="">Escolha…</option>
                {(catalog.data ?? []).map((p) => <option key={p.code} value={p.code}>{p.name}</option>)}
              </select>
            </label>
            {segmentOptions.length > 0 ? (
              <label className="mt-2 block text-sm">
                <span className="mb-1 block font-medium">Segmento</span>
                <select value={segment ?? ''} onChange={(e) => setSegment(e.target.value || null)} className="w-full rounded-md border border-input bg-card p-2 text-sm text-foreground">
                  {segmentOptions.map((s) => <option key={s.code ?? 'geral'} value={s.code ?? ''}>{s.name}</option>)}
                </select>
              </label>
            ) : null}
          </ConfirmDialog>
        </div>
        <ul className="divide-y divide-border">
          {active.map((p) => (
            <li key={p.code + (p.segment ?? '')} className="flex flex-wrap items-center gap-3 px-3 py-2">
              <span className="min-w-0 flex-1">
                <b className="block text-sm font-semibold">{label(p)}</b>
                <span className="text-xs text-muted-foreground">Aplicado em <span className="tabular-nums">{formatDateTime(p.appliedAt)}</span> por {p.appliedBy}</span>
              </span>
              <ConfirmDialog
                verb="Remover"
                done="Perfil removido"
                title={`Remover o perfil ${label(p)}?`}
                description="Os módulos que só este perfil trazia deixam de aparecer para a empresa. Nenhum dado é apagado, e as exceções da aba Módulos continuam valendo. Dá para aplicar o perfil de novo depois."
                preconditions={[{ ok: !last, text: 'A empresa precisa de pelo menos um perfil. Adicione outro antes de remover este.' }]}
                onConfirm={async () => {
                  await removeTenantProfile(tenantId, p.code, p.segment ? { segment: p.segment } : undefined)
                  await refresh()
                }}
                trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Remover</button>}
              />
            </li>
          ))}
        </ul>
      </section>
      <section aria-labelledby="perfis-historico" className="rounded-md border border-border bg-card">
        <h3 id="perfis-historico" className="border-b border-border px-3 py-2 text-sm font-semibold">Histórico</h3>
        <ul className="divide-y divide-border text-sm">
          {history.map((p) => (
            <li key={p.code + (p.segment ?? '') + p.appliedAt} className="flex flex-wrap items-center gap-2 px-3 py-2">
              <StatusChip tone={p.removedAt ? 'neutral' : 'ok'}>{p.removedAt ? 'Removido' : 'Ativo'}</StatusChip>
              <span className="font-medium">{label(p)}</span>
              <span className="text-xs text-muted-foreground">
                aplicado em <span className="tabular-nums">{formatDateTime(p.appliedAt)}</span> por {p.appliedBy}
                {p.removedAt ? <>; removido em <span className="tabular-nums">{formatDateTime(p.removedAt)}</span> por {p.removedBy}</> : null}
              </span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}
