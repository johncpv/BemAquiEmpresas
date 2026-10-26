import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useEffect, useState } from 'react'
import { generateFabricationOrders, getListFabricationOrdersQueryKey, getListPendingFabricationItemsQueryKey, useListFabricationOrders, useListPendingFabricationItems, useListWorkCenters } from '@/api/generated/bemaqui'
import type { FabricationOrderSummary, PendingFabricationItem } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'
import { delayLabel, FABRICATION_DICAS, FABRICATION_SITUATIONS, FABRICATION_STATUS, quantity } from './fabrication'

const button = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/**
 * Fábrica → Produção → OS de fabricação (PRD-01 CA 15): etapa atual, prometido com atraso e situação; filtros na URL por
 * projeto, cliente, linha, situação e atraso; o filtro "Itens de projeto sem OS" troca a lista pelos itens que ainda podem gerar OS.
 */
export function FabricationOrdersPage() {
  const { semOs } = useTableFilters(['semOs'])
  const navigate = useNavigate()
  const pending = semOs === '1'
  const toggle = () => void navigate({ to: '/app/producao/os', search: pending ? {} : { semOs: 1 } } as never)
  return (
    <>
      <PageHeading title="Produção: OS de fabricação">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Cada equipamento vendido vira uma OS com o roteiro do produto. Ajuste as etapas antes de começar.</p>
          <button type="button" aria-pressed={pending} onClick={toggle} className={pending ? primary : button}>Itens de projeto sem OS</button>
        </div>
      </PageHeading>
      <ProductionTabs />
      {pending ? <PendingItems /> : <OrdersTable />}
    </>
  )
}

function OrdersTable() {
  const [search] = useTableSearch()
  const navigate = useNavigate()
  const { situacao, linha, atraso, projeto, cliente } = useTableFilters(['situacao', 'linha', 'atraso', 'projeto', 'cliente'])
  const canApprove = useCan('production.approve')
  // PRD-06a1 CA 1: quem conclui a OS abre a lista em "Aguardando você" (prontas para concluir); "Todas" troca o filtro.
  useEffect(() => {
    if (canApprove && !situacao && !projeto && !cliente) void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, situacao: 'pronta' }), replace: true } as never)
    // Só ao abrir a lista: depois, o filtro é do usuário.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canApprove])
  const waiting = situacao === 'pronta'
  const centers = useListWorkCenters({ pageSize: 100 })
  const lines = [...new Set((centers.data?.items ?? []).map((c) => c.line).filter((l): l is string => !!l))].sort((a, b) => a.localeCompare(b, 'pt-BR'))
  const query = useListFabricationOrders({
    situation: situacao && situacao !== 'todas' ? situacao : undefined, line: linha || undefined, late: atraso === '1' ? true : undefined, projectId: projeto || undefined, partyId: cliente || undefined,
    page: search.page, pageSize: search.pageSize,
  })
  const filterBy = (key: 'projeto' | 'cliente', value: string) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, [key]: value, page: undefined }) } as never)
  const columns: ColumnDef<FabricationOrderSummary>[] = [
    {
      id: 'number', header: 'OS', cell: ({ row: { original: o } }) => (
        <span>
          <Link to={'/app/producao/os/$id' as never} params={{ id: o.id } as never} className="font-semibold text-primary hover:underline">{o.number}</Link>
          {delayLabel(o.delayDays) ? <span className="block text-xs font-semibold text-crit-ink">{delayLabel(o.delayDays)}</span> : o.risk === 'AtRisk' ? <span className="block text-xs font-semibold text-warn-ink">Em risco</span> : null}
        </span>
      ),
    },
    {
      id: 'project', header: 'Projeto', meta: { secondary: true }, cell: ({ row: { original: o } }) => (
        <span>
          <button type="button" onClick={() => filterBy('projeto', o.projectId)} className="text-left hover:underline" title="Filtrar por este projeto">{o.projectNumber}</button>
          <button type="button" onClick={() => filterBy('cliente', o.partyId)} className="block text-left text-xs text-muted-foreground hover:underline" title="Filtrar por este cliente">{o.partyName}</button>
        </span>
      ),
    },
    { id: 'product', header: 'Produto', cell: ({ row: { original: o } }) => <span>{o.productName ?? o.itemDescription}<span className="block text-xs text-muted-foreground tabular-nums">Qtd. {quantity(o.quantity)}</span></span> },
    {
      id: 'stage', header: 'Etapa', cell: ({ row: { original: o } }) => (o.noRouting
        ? <span className="inline-flex items-center"><StatusChip tone="warn">Sem roteiro</StatusChip><Dica nome="Sem roteiro" texto={FABRICATION_DICAS.semRoteiro} /></span>
        : <StageIndicator stages={o.stepNames} letters={o.codes} current={o.currentStepIndex} />),
    },
    { id: 'promised', header: 'Prometido', meta: { secondary: true }, cell: ({ row: { original: o } }) => <span className="tabular-nums">{o.promisedDate ? formatDate(o.promisedDate) : '—'}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: o } }) => <StatusChip tone={FABRICATION_STATUS[o.status]?.tone ?? 'neutral'}>{FABRICATION_STATUS[o.status]?.label ?? o.status}</StatusChip> },
  ]
  const clear = (projeto || cliente) ? (
    <p className="mb-2 flex flex-wrap items-center gap-2 text-sm">
      {projeto ? <Tag>Filtrado por projeto</Tag> : null}{cliente ? <Tag>Filtrado por cliente</Tag> : null}
      <button type="button" className="font-semibold text-primary hover:underline" onClick={() => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, projeto: undefined, cliente: undefined }) } as never)}>Limpar</button>
    </p>
  ) : null
  const empty = (
    <span className="flex flex-wrap gap-2">
      <Link to={'/app/projetos' as never} className={primary}>Ver projetos</Link>
      <Link to={'/app/producao/os' as never} search={{ semOs: 1 } as never} className={button}>Itens de projeto sem OS</Link>
    </span>
  )
  return (
    <>
      {clear}
      <DataTable label="OS de fabricação" columns={columns} query={query} getRowId={(o) => o.id} searchable={false}
        rowClassName={(o) => (o.delayDays ? 'border-l-4 border-l-crit' : o.risk === 'AtRisk' ? 'border-l-4 border-l-warn' : undefined)}
        filters={[
          { key: 'situacao', label: 'Situação', options: [...(canApprove ? [{ value: 'todas', label: 'Todas' }] : []), ...FABRICATION_SITUATIONS.map((s) => (s.value === 'pronta' && canApprove ? { ...s, label: 'Aguardando você' } : s))] },
          ...(lines.length > 0 ? [{ key: 'linha', label: 'Linha', options: lines.map((l) => ({ value: l, label: l })) }] : []),
          { key: 'atraso', label: 'Prazo', options: [{ value: '1', label: 'Atrasadas' }] },
        ]}
        filteredEmpty={waiting && !linha && !atraso && !projeto && !cliente ? {
          title: 'Nenhuma OS pronta para concluir.', description: 'A OS chega aqui quando todas as etapas e a qualidade terminam. Acompanhe a fábrica pelo Kanban.',
          action: (
            <span className="flex flex-wrap gap-2">
              <Link to={'/app/producao/kanban' as never} className={primary}>Ver no Kanban</Link>
              <Link to={'/app/producao/os' as never} search={{ situacao: 'todas' } as never} className={button}>Ver todas as OS</Link>
            </span>
          ),
        } : undefined}
        empty={{ title: 'As OS de fabricação nascem com a aprovação do orçamento.', description: 'Equipamento gera OS sozinho. Componente fabricado e itens antigos, pelo filtro "Itens de projeto sem OS".', action: empty }} />
    </>
  )
}

/** "Itens de projeto sem OS" (CA 5): escolha os itens e "Gerar OS"; o diálogo diz quantas OS vão nascer. */
function PendingItems() {
  const [search] = useTableSearch()
  const client = useQueryClient()
  const navigate = useNavigate()
  const canCreate = useCan('production.create')
  const idempotency = useIdempotencyKey()
  const query = useListPendingFabricationItems({ page: search.page, pageSize: search.pageSize })
  const [selected, setSelected] = useState<string[]>([])
  const kind = (k: string) => (k === 'Equipment' ? 'Equipamento' : 'Componente')
  const columns: ColumnDef<PendingFabricationItem>[] = [
    { id: 'project', header: 'Projeto', cell: ({ row: { original: i } }) => <span><Link to={'/app/projetos/$id' as never} params={{ id: i.projectId } as never} search={{ aba: 'itens' } as never} className="font-semibold text-primary hover:underline">{i.projectNumber}</Link><span className="block text-xs text-muted-foreground">{i.partyName}</span></span> },
    { id: 'item', header: 'Item', cell: ({ row: { original: i } }) => <span>{i.sequence}. {i.productName ?? i.description}<span className="block text-xs text-muted-foreground">{kind(i.kind)} · Qtd. {quantity(i.quantity)}</span></span> },
    { id: 'promised', header: 'Prometido', meta: { secondary: true }, cell: ({ row: { original: i } }) => <span className="tabular-nums">{i.promisedDate ? formatDate(i.promisedDate) : '—'}</span> },
  ]
  const generate = async () => {
    const result = await generateFabricationOrders({ projectItemIds: selected }, idempotency.options)
    idempotency.renew()
    setSelected([])
    await client.invalidateQueries({ queryKey: getListPendingFabricationItemsQueryKey() })
    await client.invalidateQueries({ queryKey: getListFabricationOrdersQueryKey() })
    if (result.created > 0 && result.skipped.length === 0) void navigate({ to: '/app/producao/os' } as never)
  }
  return (
    <div className="space-y-3">
      <p className="flex items-center text-sm text-muted-foreground">Equipamentos de projetos aprovados antes da OS automática e componentes fabricados.<Dica nome="Gerar OS" texto={FABRICATION_DICAS.gerarOs} /></p>
      {canCreate ? (
        <ConfirmDialog verb="Gerar OS" done={`${selected.length} OS geradas`} title={`Gerar ${selected.length} ${selected.length === 1 ? 'OS' : 'OS'} de fabricação?`}
          description={`Nasce uma OS por item, com a quantidade do item e o roteiro sugerido. ${selected.length} ${selected.length === 1 ? 'OS vai nascer' : 'OS vão nascer'}.`}
          preconditions={[{ ok: selected.length > 0, text: 'Escolha ao menos um item.' }]}
          onConfirm={generate}
          trigger={<button type="button" disabled={selected.length === 0} className={`${primary} disabled:opacity-50`}>Gerar OS{selected.length > 0 ? ` (${selected.length})` : ''}</button>} />
      ) : null}
      <DataTable label="Itens de projeto sem OS" columns={columns} query={query} getRowId={(i) => i.projectItemId} searchable={false}
        selectable={canCreate ? { selected, onChange: setSelected, rowLabel: (i) => `${i.projectNumber} item ${i.sequence}` } : undefined}
        empty={{ title: 'Todos os itens de projeto já têm OS de fabricação.', description: 'Serviço e contrato de implantação não geram OS.' }} />
    </div>
  )
}