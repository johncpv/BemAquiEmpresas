import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useEffect, useRef, useState } from 'react'
import { getGetTenantQueryKey, useGetTenant } from '@/api/generated/bemaqui'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { STEP_LABEL, TENANT_STATUS, type TenantStatusName } from './status'
import { TenantActions } from './TenantActions'
import { TenantModulesTab } from './TenantModulesTab'

/**
 * Detalhe do tenant (FND-03b CA 2, 4 a 6): progresso por etapa (consulta a cada 3 s enquanto provisiona), ações e a aba
 * "Módulos" (FND-06). Ao concluir o provisionamento, oferece liberar módulos (próximo passo).
 */
export function TenantDetailPage() {
  const { key } = useParams({ strict: false }) as { key: string }
  const { aba } = useSearch({ strict: false }) as { aba?: string }
  const navigate = useNavigate()
  const client = useQueryClient()
  const detail = useGetTenant(key, { query: { refetchInterval: (q) => (q.state.data?.tenant.status === 'Provisioning' ? 3000 : false) } })
  const previous = useRef<string | undefined>(undefined)
  const [provisioned, setProvisioned] = useState(false)
  const status = detail.data?.tenant.status
  const tab = (t: string) => void navigate({ to: '.', search: { aba: t }, replace: true } as never)

  useEffect(() => {
    if (previous.current === 'Provisioning' && status === 'Active') {
      notifySuccess('Provisionamento concluído. Convite enviado ao proprietário.')
      setProvisioned(true)
    }
    previous.current = status
  }, [status])

  if (detail.isPending) return <Skeleton className="h-80" />
  if (detail.isError) return <ErrorState title="Não foi possível carregar o tenant." onRetry={() => detail.refetch()} />

  const { tenant, log, steps, domains } = detail.data
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetTenantQueryKey(key) }) }
  const last = (step: string) => [...log].reverse().find((l) => l.step === step)
  const current = Math.max(0, steps.findIndex((s) => last(s)?.status !== 'Succeeded'))
  const failed = log.length > 0 && log[log.length - 1].status === 'Failed'
  const [tone, label] = TENANT_STATUS[tenant.status as TenantStatusName] ?? TENANT_STATUS.Active
  const stepChip = (s?: string) => (s === 'Succeeded' ? (['ok', 'Concluída'] as const) : s === 'Failed' ? (['crit', 'Falhou'] as const) : s ? (['info', 'Em andamento'] as const) : (['info', 'Aguardando'] as const))

  return (
    <>
      <PageHeading title={tenant.tradeName || tenant.legalName}>
        <p className="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <StatusChip tone={tone}>{label}</StatusChip> {tenant.legalName} · {tenant.document ?? 'sem CNPJ'} · {domains[0] ?? 'sem endereço'}
        </p>
      </PageHeading>
      {provisioned ? <div className="mb-4"><NextSteps onDismiss={() => setProvisioned(false)} steps={[{ label: 'Liberar módulos', onSelect: () => { setProvisioned(false); tab('modulos') } }]} /></div> : null}
      <nav aria-label="Seções do tenant" className="mb-4 flex gap-1 border-b border-border">
        {[['geral', 'Visão geral'], ['modulos', 'Módulos']].map(([id, text]) => (
          <button key={id} type="button" onClick={() => tab(id)} aria-current={(aba ?? 'geral') === id ? 'page' : undefined} className="border-b-2 border-transparent px-3 pb-2 text-sm font-medium text-muted-foreground aria-[current=page]:border-signal aria-[current=page]:text-foreground">
            {text}
          </button>
        ))}
      </nav>
      {aba === 'modulos' ? <TenantModulesTab tenantId={tenant.id} /> : (
        <div className="space-y-4">
          <section className="grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
            <p><span className="block text-xs text-muted-foreground">Plano</span>{tenant.plan ?? '—'}</p>
            <p><span className="block text-xs text-muted-foreground">Versão do banco</span><code className="text-xs">{tenant.schemaVersion ?? '—'}</code></p>
            <p><span className="block text-xs text-muted-foreground">Criado em</span><span className="tabular-nums">{formatDateTime(tenant.createdAt)}</span></p>
            <p><span className="block text-xs text-muted-foreground">Endereços</span>{domains.join(', ')}</p>
          </section>
          <section aria-labelledby="etapas" className="space-y-3 rounded-md border border-border bg-card p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 id="etapas" className="text-[15px] font-semibold">Provisionamento</h2>
              <StageIndicator stages={steps.map((s) => STEP_LABEL[s] ?? s)} current={tenant.status === 'Provisioning' ? current : steps.length - 1} />
            </div>
            <ol className="space-y-1 text-sm">
              {steps.map((s) => {
                const entry = last(s)
                const [chipTone, chipLabel] = stepChip(entry?.status)
                return (
                  <li key={s} className="flex flex-wrap items-center gap-2">
                    <StatusChip tone={chipTone}>{chipLabel}</StatusChip>
                    <span>{STEP_LABEL[s] ?? s}</span>
                    {entry?.status === 'Failed' && entry.message ? <span className="text-xs text-crit-ink">{entry.message}</span> : null}
                  </li>
                )
              })}
            </ol>
          </section>
          <TenantActions tenant={tenant} host={domains[0]} failed={failed} refresh={refresh} />
        </div>
      )}
    </>
  )
}
