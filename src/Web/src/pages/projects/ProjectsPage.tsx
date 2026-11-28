import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { getExportProjectsUrl, useListProjectMargins, useListProjectReceived, useListProjects } from '@/api/generated/bemaqui'
import type { ProjectListRow } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ExportMenu } from '@/components/ui/export-menu'
import { Money } from '@/components/ui/money'
import { useHasProfile } from '@/navigation/navigation-context'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ProjectStageIndicator } from './ProjectStageIndicator'
import { awaitingAcceptance, forecastReasonText, riskStripe, slackText } from './projects'

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

const KINDS = [{ value: 'fabricacao', label: 'Fabricação' }, { value: 'servico', label: 'Serviço' }]
const SITUATIONS = [{ value: 'andamento', label: 'Em andamento' }, { value: 'entregue', label: 'Entregues' }, { value: 'encerrado', label: 'Encerrados' }, { value: 'cancelado', label: 'Cancelados' }]
const pct = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${v.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`)

/** Comercial → Projetos (COM-05 CA 1): etapa, entrega com atraso, contratado; margem com projects.view_cost e recebido com finance.view. */
export function ProjectsPage() {
  const [search] = useTableSearch()
  const { situacao, cliente, responsavel, tipo } = useTableFilters(['situacao', 'cliente', 'responsavel', 'tipo'])
  // EXE-01b CA 12: coluna e filtro "Tipo" só no tenant com o recurso (perfil Serviços).
  const services = useHasProfile(['services'])
  const canCost = useCan('projects.view_cost')
  const canFinance = useCan('finance.view')
  const filters = { q: search.q, situation: situacao || undefined, partyId: cliente || undefined, managerId: responsavel || undefined, ...(services && tipo ? { kind: tipo } : {}) }
  const query = useListProjects({ ...filters, page: search.page, pageSize: search.pageSize })
  const ids = (query.data?.items ?? []).map((p) => p.id).join(',')
  const margins = useListProjectMargins({ ids }, { query: { enabled: canCost && ids.length > 0 } })
  const received = useListProjectReceived({ ids }, { query: { enabled: canFinance && ids.length > 0 } })
  const columns: ColumnDef<ProjectListRow>[] = [
    { id: 'number', header: 'Projeto', cell: ({ row: { original: p } }) => <span><Link to={'/app/projetos/$id' as never} params={{ id: p.id } as never} className="font-semibold text-primary hover:underline">{p.number}</Link><span className="block text-xs text-muted-foreground">{p.name}</span></span> },
    { id: 'party', header: 'Cliente', cell: ({ row: { original: p } }) => p.partyName },
    ...(services ? [{ id: 'kind', header: 'Tipo', meta: { secondary: true }, cell: ({ row: { original: p } }: { row: { original: ProjectListRow } }) => <Tag>{p.executionKind === 'Service' ? 'Serviço' : 'Fabricação'}</Tag> }] as ColumnDef<ProjectListRow>[] : []),
    { id: 'stage', header: 'Etapa', cell: ({ row: { original: p } }) => <ProjectStageIndicator project={p} /> },
    { id: 'due', header: 'Entrega', cell: ({ row: { original: p } }) => <DueCell row={p} /> },
    { id: 'value', header: 'Contratado', meta: { numeric: true, secondary: true }, cell: ({ row: { original: p } }) => <Money value={p.contractValue} /> },
    ...(canCost ? [{ id: 'margin', header: 'Margem', meta: { numeric: true, secondary: true }, cell: ({ row: { original: p } }: { row: { original: ProjectListRow } }) => <span className="tabular-nums">{pct(margins.data?.find((m) => m.id === p.id)?.marginPct)}</span> }] as ColumnDef<ProjectListRow>[] : []),
    ...(canFinance ? [{ id: 'received', header: 'Recebido', meta: { numeric: true, secondary: true }, cell: ({ row: { original: p } }: { row: { original: ProjectListRow } }) => <span className="tabular-nums">{pct(received.data?.find((r) => r.id === p.id)?.receivedPct)}</span> }] as ColumnDef<ProjectListRow>[] : []),
    { id: 'manager', header: 'Responsável', meta: { secondary: true }, cell: ({ row: { original: p } }) => p.managerName ?? '—' },
  ]
  const goQuotes = <Link to={'/app/orcamentos' as never} search={{ situacao: 'enviado' } as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver orçamentos enviados</Link>
  return (
    <>
      <PageHeading title="Comercial: Projetos" description="Cada contrato vira um projeto. Todo custo lançado cai aqui."
        actions={(
          <span className="flex flex-wrap gap-2">
            {/* EXE-02: modelos de etapas do projeto de serviço, só no perfil Serviços. */}
            {services ? <Link to={'/app/projetos/modelos' as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Modelos de etapas</Link> : null}
            <Link to={'/app/contratos' as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ver contratos</Link>
          </span>
        )} />
      <DataTable label="Projetos" columns={columns} query={query} getRowId={(p) => p.id} searchPlaceholder="Buscar pelo número ou nome"
        rowClassName={(p) => riskStripe(p.risk) ?? (p.delayDays ? 'border-l-4 border-l-crit' : awaitingAcceptance(p) ? 'border-l-4 border-l-warn' : undefined)}
        filters={[{ key: 'situacao', label: 'Situação', options: SITUATIONS }, ...(services ? [{ key: 'tipo', label: 'Tipo', options: KINDS }] : [])]}
        toolbar={<ExportMenu formats={['xlsx']} fileBase="projetos" empty={query.data?.total === 0} url={(format) => getExportProjectsUrl({ ...filters, format })} />}
        empty={tipo === 'servico' && services
          ? { title: 'Nenhum projeto de serviço ainda. Eles nascem quando um orçamento de serviço é aprovado.', action: <Link to={'/app/orcamentos' as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver orçamentos</Link> }
          : { title: 'Os projetos nascem quando um orçamento é aprovado.', action: goQuotes }} />
    </>
  )
}
