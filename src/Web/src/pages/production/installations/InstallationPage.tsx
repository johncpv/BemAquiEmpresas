import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetInstallationQueryKey, useGetInstallation } from '@/api/generated/bemaqui'
import type { InstallationAcceptResult } from '@/api/generated/model'
import { RecordHistory } from '@/components/history/RecordHistory'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { InstallationActions } from './InstallationActions'
import { InstallationDocuments } from './InstallationDocuments'
import { currentStep, INSTALLATION_DICAS, INSTALLATION_STATUS } from './installations'
import { InstallationChecklist, InstallationSteps } from './InstallationWork'

/** OS de instalação (COM-06): cabeçalho com a etapa e as ações; abas Etapas, Checklist, Documentos e Histórico. */
export function InstallationPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canFinance = useCan('finance.view')
  const query = useGetInstallation(id)
  const [accepted, setAccepted] = useState<InstallationAcceptResult | null>(null)
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a OS." onRetry={() => query.refetch()} />
  const o = query.data
  const refresh = () => client.invalidateQueries({ queryKey: getGetInstallationQueryKey(id) })
  return (
    <>
      <PageHeading title={`${o.number} — ${o.projectName}`}>
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <StatusChip tone={INSTALLATION_STATUS[o.status]?.tone ?? 'neutral'}>{INSTALLATION_STATUS[o.status]?.label ?? o.status}</StatusChip>
          {o.status !== 'Cancelled' ? <StageIndicator stages={o.steps.map((s) => s.name)} current={currentStep(o.steps)} showLabel /> : null}
          <Link to={'/app/projetos/$id' as never} params={{ id: o.projectId } as never} search={{ aba: 'itens' } as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ver projeto {o.projectNumber}</Link>
          <InstallationActions order={o} onChange={refresh} onAccepted={setAccepted} />
        </div>
      </PageHeading>
      {accepted ? (
        <div className="mb-4"><NextSteps onDismiss={() => setAccepted(null)} steps={[
          ...(canFinance ? [{ label: 'Ver parcela liberada', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: o.projectId }, search: { aba: 'financeiro' } } as never) }] : []),
          { label: 'Conferir título do instalador', onSelect: () => void navigate({ to: '/app/financeiro/pagar/$id', params: { id: accepted.payableId } } as never) },
        ]} /></div>
      ) : null}
      {o.status === 'Cancelled' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">OS cancelada{o.cancelReason ? `: ${o.cancelReason}` : ''}.</p> : null}
      <dl className="mb-4 grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Instalador</dt><dd>{o.installerName}</dd></div>
        <div><dt className="flex items-center text-xs text-muted-foreground">Valor combinado<Dica nome="Valor combinado" texto={INSTALLATION_DICAS.valor} /></dt><dd><Money value={o.contractValue} className="font-semibold" /></dd></div>
        <div><dt className="text-xs text-muted-foreground">Previsão</dt><dd className="tabular-nums">{formatDate(o.plannedStart)} a {formatDate(o.plannedEnd)}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Aceite</dt><dd>{o.acceptedOn ? `${formatDate(o.acceptedOn)} · ${o.acceptedByName ?? ''}` : '—'}</dd></div>
      </dl>
      <UrlTabs label="Seções da OS" fallback="etapas" tabs={[
        { id: 'etapas', label: 'Etapas', content: <InstallationSteps order={o} onChange={refresh} /> },
        { id: 'checklist', label: 'Checklist de entrega técnica', content: <InstallationChecklist order={o} onChange={refresh} /> },
        { id: 'documentos', label: 'Documentos', content: <InstallationDocuments orderId={id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="ProductionOrder" id={id} /> },
      ]} />
    </>
  )
}
