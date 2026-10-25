import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useListProjectMargins, useListProjectReceived, useListProjects } from '@/api/generated/bemaqui'
import type { ProjectListRow } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { Money } from '@/components/ui/money'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { PROJECT_LETTERS, PROJECT_STAGES, projectStage } from '../contracts/contracts'
import { forecastReasonText, riskStripe, slackText } from './projects'

/**
 * Coluna "Entrega" (COM-05 CA 1; PLN-01b CA 8 e 9): a prometida, o atraso em vermelho e, abaixo, a prevista com a folga (a 400 px fica na
 * mesma célula, sem coluna nova) e o chip "Em risco" em âmbar; sem previsão, o motivo.
 */
function DueCell({ row: p }: { row: ProjectListRow }) {
  const slack = slackText(p.slackDays)
  const open = p.status !== 'Delivered' && p.status !== 'Closed' && p.status !== 'Cancelled'
  return (
    <span className="tabular-nums">
      {p.promisedDate ? formatDate(p.promisedDate) : '—'}
      {p.delayDays ? <span className="block text-xs text-crit-ink">{p.delayDays} dias de atraso</span> : null}
      {p.forecastDate ? (
        <span className="block text-xs text-muted-foreground">
          Prevista {formatDate(p.forecastDate)}
          {slack ? <span className={`block ${(p.slackDays ?? 0) < 0 ? 'font-semibold text-warn-ink' : ''}`}>{slack}</span> : null}
        </span>
      ) : open && p.forecastReason ? <span className="block text-xs text-warn-ink">Sem previsão: {forecastReasonText(p.forecastReason)}</span> : null}
      {p.risk === 'AtRisk' ? <StatusChip tone="warn" className="mt-1">Em risco</StatusChip> : null}
    </span>
  )
}

const SITUATIONS = [{ value: 'andamento', label: 'Em andamento' }, { value: 'entregue', label: 'Entregues' }, { value: 'encerrado', label: 'Encerrados' }, { value: 'cancelado', label: 'Cancelados' }]
const pct = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${v.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`)

/** Comercial → Projetos (COM-05 CA 1): etapa, entrega com atraso, contratado; margem com projects.view_cost e recebido com finance.view. */
export function ProjectsPage() {
  const [search] = useTableSearch()
  const { situacao, cliente, responsavel } = useTableFilters(['situacao', 'cliente', 'responsavel'])
  const canCost = useCan('projects.view_cost')
  const canFinance = useCan('finance.view')
  const query = useListProjects({ q: search.q, situation: situacao || undefined, partyId: cliente || undefined, managerId: responsavel || undefined, page: search.page, pageSize: search.pageSize })
  const ids = (query.data?.items ?? []).map((p) => p.id).join(',')
  const margins = useListProjectMargins({ ids }, { query: { enabled: canCost && ids.length > 0 } })
  const received = useListProjectReceived({ ids }, { query: { enabled: canFinance && ids.length > 0 } })
  const columns: ColumnDef<ProjectListRow>[] = [
    { id: 'number', header: 'Projeto', cell: ({ row: { original: p } }) => <span><Link to={'/app/projetos/$id' as never} params={{ id: p.id } as never} className="font-semibold text-primary hover:underline">{p.number}</Link><span className="block text-xs text-muted-foreground">{p.name}</span></span> },
    { id: 'party', header: 'Cliente', cell: ({ row: { original: p } }) => p.partyName },
    { id: 'stage', header: 'Etapa', cell: ({ row: { original: p } }) => (projectStage(p.status ?? '') < 0 ? <StatusChip tone="neutral">Cancelado</StatusChip> : <StageIndicator stages={PROJECT_STAGES} letters={PROJECT_LETTERS} current={projectStage(p.status ?? '')} />) },
    { id: 'due', header: 'Entrega', cell: ({ row: { original: p } }) => <DueCell row={p} /> },
    { id: 'value', header: 'Contratado', meta: { numeric: true, secondary: true }, cell: ({ row: { original: p } }) => <Money value={p.contractValue} /> },
    ...(canCost ? [{ id: 'margin', header: 'Margem', meta: { numeric: true, secondary: true }, cell: ({ row: { original: p } }: { row: { original: ProjectListRow } }) => <span className="tabular-nums">{pct(margins.data?.find((m) => m.id === p.id)?.marginPct)}</span> }] as ColumnDef<ProjectListRow>[] : []),
    ...(canFinance ? [{ id: 'received', header: 'Recebido', meta: { numeric: true, secondary: true }, cell: ({ row: { original: p } }: { row: { original: ProjectListRow } }) => <span className="tabular-nums">{pct(received.data?.find((r) => r.id === p.id)?.receivedPct)}</span> }] as ColumnDef<ProjectListRow>[] : []),
    { id: 'manager', header: 'Responsável', meta: { secondary: true }, cell: ({ row: { original: p } }) => p.managerName ?? '—' },
  ]
  const goQuotes = <Link to={'/app/orcamentos' as never} search={{ situacao: 'enviado' } as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver orçamentos enviados</Link>
  return (
    <>
      <PageHeading title="Comercial: Projetos">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Cada contrato vira um projeto. Todo custo lançado cai aqui.</p>
          <Link to={'/app/contratos' as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ver contratos</Link>
        </div>
      </PageHeading>
      <DataTable label="Projetos" columns={columns} query={query} getRowId={(p) => p.id} searchPlaceholder="Buscar pelo número ou nome"
        rowClassName={(p) => riskStripe(p.risk) ?? (p.delayDays ? 'border-l-4 border-l-crit' : undefined)}
        filters={[{ key: 'situacao', label: 'Situação', options: SITUATIONS }]}
        empty={{ title: 'Os projetos nascem quando um orçamento é aprovado.', action: goQuotes }} />
    </>
  )
}
