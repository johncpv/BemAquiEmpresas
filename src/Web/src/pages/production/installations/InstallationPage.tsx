import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { getGetInstallationQueryKey, useGetInstallation } from '@/api/generated/bemaqui'
import type { InstallationAcceptResult } from '@/api/generated/model'
import { RecordHistory } from '@/components/history/RecordHistory'
import { FactsBar } from '@/components/ui/facts-bar'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { OrderHeader } from '@/components/ui/order-header'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDate } from '@/lib/format'
import { useStartNfse } from '@/pages/fiscal/use-start-nfse'
import { useCan } from '@/pages/use-can'
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
  const canInvoice = useCan('fiscal.create')
  const nfse = useStartNfse()
  const query = useGetInstallation(id)
  const [accepted, setAccepted] = useState<InstallationAcceptResult | null>(null)
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a OS." onRetry={() => query.refetch()} />
  const o = query.data
  const refresh = () => client.invalidateQueries({ queryKey: getGetInstallationQueryKey(id) })
  return (
    <>
      <OrderHeader title={`${o.number} — ${o.projectName}`}
        status={{ label: INSTALLATION_STATUS[o.status]?.label ?? o.status, tone: INSTALLATION_STATUS[o.status]?.tone ?? 'neutral' }}
        stages={o.status !== 'Cancelled' ? { names: o.steps.map((s) => s.name), current: currentStep(o.steps) } : null}
        project={{ id: o.projectId, number: o.projectNumber }}
        actions={<InstallationActions order={o} onChange={refresh} onAccepted={setAccepted} />} />
      {accepted ? (
        <div className="mb-4"><NextSteps onDismiss={() => setAccepted(null)} steps={[
          ...(canFinance ? [{ label: 'Ver parcela liberada', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: o.projectId }, search: { aba: 'financeiro' } } as never) }] : []),
          { label: 'Conferir título do instalador', onSelect: () => void navigate({ to: '/app/financeiro/pagar/$id', params: { id: accepted.payableId } } as never) },
          // FIS-06 CA 1: o aceite técnico oferece a NFS-e da instalação.
          ...(canInvoice ? [{ label: 'Emitir NFS-e da instalação', onSelect: () => void nfse.start(o.id, o.projectId) }] : []),
        ]} /></div>
      ) : null}
      {o.status === 'Completed' && canInvoice && !accepted ? (
        <p className="mb-4 flex flex-wrap items-center gap-2 text-sm">
          <span className="text-muted-foreground">Aceite técnico registrado: a instalação sai em NFS-e.</span>
          <button type="button" disabled={nfse.busy !== null} onClick={() => void nfse.start(o.id, o.projectId)} className="inline-flex min-h-11 items-center rounded-md border border-border bg-card px-3 font-semibold">{nfse.busy ? 'Abrindo…' : 'Emitir NFS-e da instalação'}</button>
        </p>
      ) : null}
      {nfse.error ? <p role="alert" className="mb-4 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{nfse.error}</p> : null}
      {o.status === 'Cancelled' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">OS cancelada{o.cancelReason ? `: ${o.cancelReason}` : ''}.</p> : null}
      <FactsBar facts={[
        { label: 'Instalador', value: o.installerName },
        { label: 'Valor combinado', dica: INSTALLATION_DICAS.valor, value: <Money value={o.contractValue} className="font-semibold" /> },
        { label: 'Previsão', value: <span className="tabular-nums">{formatDate(o.plannedStart)} a {formatDate(o.plannedEnd)}</span> },
        { label: 'Aceite', value: o.acceptedOn ? `${formatDate(o.acceptedOn)} · ${o.acceptedByName ?? ''}` : '—' },
      ]} />
      <UrlTabs label="Seções da OS" fallback="etapas" tabs={[
        { id: 'etapas', label: 'Etapas', content: <InstallationSteps order={o} onChange={refresh} /> },
        { id: 'checklist', label: 'Checklist de entrega técnica', content: <InstallationChecklist order={o} onChange={refresh} /> },
        { id: 'documentos', label: 'Documentos', content: <InstallationDocuments orderId={id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="ProductionOrder" id={id} /> },
      ]} />
    </>
  )
}
