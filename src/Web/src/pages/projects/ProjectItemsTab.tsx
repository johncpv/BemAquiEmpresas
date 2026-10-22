import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { generateFabricationOrder, getListFabricationOrdersQueryKey, getListPendingFabricationItemsQueryKey, useListFabricationOrders, useListInstallations, useListPendingFabricationItems } from '@/api/generated/bemaqui'
import type { ProjectDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { FormError } from '@/components/form/form'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { INSTALLATION_STATUS } from '../production/installations/installations'
import { FABRICATION_DICAS, FABRICATION_STATUS } from '../production/orders/fabrication'
import { NewInstallationDialog } from './NewInstallationDialog'

const qty = (v: number) => v.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

/**
 * Itens e OS (COM-05 CA 5, COM-06, PRD-01 CA 5 e 21): cada item com a OS de fabricação e a etapa, ou "Gerar OS de fabricação"
 * (production.create) para o item que ainda pode gerar; abaixo, as OS de instalação e "Nova OS de instalação".
 */
export function ProjectItemsTab({ project }: { project: ProjectDetail }) {
  const canView = useCan('production.view')
  const canCreateFabrication = useCan('production.create')
  const canCreate = canCreateFabrication && ['Planned', 'InProgress', 'Fabricated', 'Installing'].includes(project.status ?? '') && project.contractStatus === 'Active'
  const orders = useListInstallations({ projectId: project.id, pageSize: 100 }, { query: { enabled: canView } })
  const fabrication = useListFabricationOrders({ projectId: project.id, pageSize: 100 }, { query: { enabled: canView } })
  const pending = useListPendingFabricationItems({ projectId: project.id, pageSize: 100 }, { query: { enabled: canView } })
  const client = useQueryClient()
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | undefined>()
  const list = orders.data?.items ?? []
  const made = (fabrication.data?.items ?? []).filter((o) => o.status !== 'Cancelled')
  const canGenerate = new Set((pending.data?.items ?? []).map((i) => i.projectItemId))
  const generate = async (projectItemId: string) => {
    setBusy(projectItemId)
    setError(undefined)
    try {
      const created = await generateFabricationOrder({ projectItemId }, idempotency.options)
      idempotency.renew()
      await client.invalidateQueries({ queryKey: getListFabricationOrdersQueryKey() })
      await client.invalidateQueries({ queryKey: getListPendingFabricationItemsQueryKey() })
      await navigate({ to: '/app/producao/os/$id', params: { id: created.id }, search: { gerada: 1 } } as never)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível gerar a OS. Tente de novo.')
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="space-y-3">
      {canCreate ? <NewInstallationDialog project={project} /> : null}
      <FormError message={error} />
      {project.items.length === 0 ? <EmptyState title="Projeto sem itens." /> : (
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Itens do projeto">
          {project.items.map((i) => {
            const os = made.find((o) => o.projectItemId === i.id)
            return (
              <li key={i.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span><b>{i.sequence}.</b> {i.description} <span className="tabular-nums text-muted-foreground">· {qty(i.quantity)}</span></span>
                {os ? (
                  <span className="flex flex-wrap items-center gap-2">
                    <Link to={'/app/producao/os/$id' as never} params={{ id: os.id } as never} className="font-semibold text-primary hover:underline">{os.number}</Link>
                    {os.noRouting ? <StatusChip tone="warn">Sem roteiro</StatusChip> : <StageIndicator stages={os.stepNames} letters={os.codes} current={os.currentStepIndex} />}
                  </span>
                ) : canView && canCreateFabrication && canGenerate.has(i.id) ? (
                  <span className="inline-flex items-center">
                    <button type="button" disabled={busy === i.id} onClick={() => void generate(i.id)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-50">
                      {busy === i.id ? 'Gerando…' : 'Gerar OS de fabricação'}
                    </button>
                    <Dica nome="Gerar OS" texto={FABRICATION_DICAS.gerarOs} />
                  </span>
                ) : null}
              </li>
            )
          })}
        </ul>
      )}
      <h3 className="text-sm font-semibold">OS de fabricação</h3>
      {!canView || made.length === 0 ? <p className="text-sm text-muted-foreground">Ainda sem OS de fabricação.</p> : (
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="OS de fabricação do projeto">
          {made.map((o) => (
            <li key={o.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <span><Link to={'/app/producao/os/$id' as never} params={{ id: o.id } as never} className="font-semibold text-primary hover:underline">{o.number}</Link> <span className="text-muted-foreground">Fabricação · {o.productName ?? o.itemDescription}</span></span>
              <span className="flex items-center gap-2">
                {o.noRouting ? null : <StageIndicator stages={o.stepNames} letters={o.codes} current={o.currentStepIndex} showLabel />}
                <StatusChip tone={FABRICATION_STATUS[o.status]?.tone ?? 'neutral'}>{FABRICATION_STATUS[o.status]?.label ?? o.status}</StatusChip>
              </span>
            </li>
          ))}
        </ul>
      )}
      <h3 className="text-sm font-semibold">OS de instalação</h3>
      {!canView || list.length === 0 ? <p className="text-sm text-muted-foreground">Ainda sem OS.</p> : (
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="OS do projeto">
          {list.map((o) => (
            <li key={o.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <span><Link to={'/app/producao/instalacoes/$id' as never} params={{ id: o.id } as never} className="font-semibold text-primary hover:underline">{o.number}</Link> <span className="text-muted-foreground">Instalação · {o.installerName} · etapa {o.stepsDone}/{o.stepsTotal}</span></span>
              <span className="flex items-center gap-2"><span className="tabular-nums">{formatDate(o.plannedEnd)}</span><StatusChip tone={INSTALLATION_STATUS[o.status]?.tone ?? 'neutral'}>{INSTALLATION_STATUS[o.status]?.label ?? o.status}</StatusChip></span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}