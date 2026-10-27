import { Link, useParams, useSearch } from '@tanstack/react-router'
import { Lock } from 'lucide-react'
import { useState } from 'react'
import { useGetPayrollRun } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { RecordHistory } from '@/components/history/RecordHistory'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { useUrlTab } from '@/components/ui/url-tab'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ChargesTab } from './ChargesTab'
import { PAYROLL_DICAS, periodLabel, STATUS_LABELS, STATUS_TONES, typeLabel } from './payroll'
import { PayslipsTab } from './PayslipsTab'
import { ReviewTab } from './ReviewTab'

/** Bloco "Dados protegidos" (padrão 8 das Fases 4 a 8): sem hr.view_sensitive, nada de valor é pedido ao servidor. */
export function PayrollProtected() {
  return (
    <div className="flex max-w-3xl items-center gap-2 rounded-md border border-border bg-muted px-4 py-6 text-sm text-muted-foreground">
      <Lock aria-hidden="true" className="size-4" />
      Dados protegidos
      <Dica nome="Dados protegidos" texto={PAYROLL_DICAS.protegidos} />
    </div>
  )
}

/**
 * Folha (RH-02a, molde "detalhe com abas"): Holerites, Encargos da empresa, Conferência e Histórico de alterações, com as abas na
 * URL. Folha fechada abre em modo leitura. Sem hr.view_sensitive, só o resumo e o bloco "Dados protegidos".
 */
export function PayrollRunPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const query = useGetPayrollRun(id)
  const canSensitive = useCan('hr.view_sensitive')
  const canEdit = useCan('hr.edit') && canSensitive
  const [, setTab] = useUrlTab('holerites')
  const [steps, setSteps] = useState(search.novo === '1')
  const [importing, setImporting] = useState(false)

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-96" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Folha não encontrada.' : 'Não foi possível carregar a folha.'} message={notFound ? 'Volte à lista de folhas.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const run = query.data
  const draft = run.status === 'Draft'
  const editable = draft && canEdit
  const protectedTab = <PayrollProtected />
  return (
    <>
      <PageHeading title={`Folha ${typeLabel(run.type)} de ${periodLabel(run.period)}`}>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <StatusChip tone={STATUS_TONES[run.status] ?? 'neutral'}>{STATUS_LABELS[run.status] ?? run.status}</StatusChip>
          <span className="inline-flex items-center text-sm text-muted-foreground">Folha lançada<Dica nome="Folha lançada" texto={PAYROLL_DICAS.lancada} /></span>
          <span className="text-sm tabular-nums text-muted-foreground">{run.payslipCount === 1 ? '1 holerite' : `${run.payslipCount} holerites`}</span>
          {run.closedByName ? <span className="text-sm text-muted-foreground">Fechada por {run.closedByName}</span> : null}
          <Link to="/app/rh/folha" className="ml-2 text-sm font-semibold text-primary">Voltar às folhas</Link>
        </div>
      </PageHeading>
      {!draft ? <p role="status" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">A folha de {periodLabel(run.period)} está fechada. Os valores ficam só para consulta.</p> : null}
      {steps && editable ? (
        <div className="mb-4">
          <NextSteps onDismiss={() => setSteps(false)} steps={[
            { label: 'Importar lançamentos', onSelect: () => { setTab('holerites'); setImporting(true); setSteps(false) } },
            { label: 'Informar os encargos', onSelect: () => { setTab('encargos'); setSteps(false) } },
          ]} />
        </div>
      ) : null}
      <UrlTabs label="Seções da folha" fallback="holerites" tabs={[
        { id: 'holerites', label: 'Holerites', content: canSensitive ? <PayslipsTab run={run} editable={editable} importing={importing} onImportingChange={setImporting} onReviewed={() => setTab('conferencia')} /> : protectedTab },
        { id: 'encargos', label: 'Encargos da empresa', content: canSensitive ? <ChargesTab run={run} editable={editable} /> : protectedTab },
        { id: 'conferencia', label: 'Conferência', content: canSensitive ? <ReviewTab run={run} /> : protectedTab },
        { id: 'historico', label: 'Histórico de alterações', content: canSensitive ? <RecordHistory entity="PayrollRun" id={run.id} /> : protectedTab },
      ]} />
    </>
  )
}
