import { Link } from '@tanstack/react-router'
import { useListInstallations } from '@/api/generated/bemaqui'
import type { ProjectDetail } from '@/api/generated/model'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { INSTALLATION_STATUS } from '../production/installations/installations'
import { NewInstallationDialog } from './NewInstallationDialog'

const qty = (v: number) => v.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

/** Itens e OS (COM-05 CA 5, COM-06): os itens e as OS de instalação; "Nova OS de instalação" com production.create. */
export function ProjectItemsTab({ project }: { project: ProjectDetail }) {
  const canView = useCan('production.view')
  const canCreate = useCan('production.create') && ['Planned', 'InProgress', 'Fabricated', 'Installing'].includes(project.status ?? '') && project.contractStatus === 'Active'
  const orders = useListInstallations({ projectId: project.id, pageSize: 100 }, { query: { enabled: canView } })
  const list = orders.data?.items ?? []
  return (
    <div className="space-y-3">
      {canCreate ? <NewInstallationDialog project={project} /> : null}
      {project.items.length === 0 ? <EmptyState title="Projeto sem itens." /> : (
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label="Itens do projeto">
          {project.items.map((i) => (
            <li key={i.id} className="flex flex-wrap justify-between gap-2 p-3">
              <span><b>{i.sequence}.</b> {i.description}</span>
              <span className="tabular-nums">{qty(i.quantity)}</span>
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
