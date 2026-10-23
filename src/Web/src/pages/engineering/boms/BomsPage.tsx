import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useEffect, useRef } from 'react'
import { createBom, getListBomsQueryKey, releaseBom, useListBoms } from '@/api/generated/bemaqui'
import type { BomOrderSummary } from '@/api/generated/model'
import { IDEMPOTENCY_HEADER } from '@/api/idempotency'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StatusChip } from '@/components/ui/status-chip'
import { formatInstantDate } from '@/lib/format'
import { FABRICATION_STATUS } from '@/pages/production/orders/fabrication'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { BOM_SITUATION, BOM_SITUATIONS, bomSituationParam, DEFAULT_BOM_SITUATION, qty } from './boms'

const action = 'rounded-md border border-border bg-card px-2.5 py-1 text-xs font-semibold'

/**
 * Fábrica → Engenharia → BOMs (ENG-02 CA 17): OS de fabricação em aberto com a situação da BOM. Abre no filtro "OS que vão parar sem
 * BOM" (etapa atual depois da primeira e sem BOM liberada), com "Criar BOM" e "Liberar BOM sem material" por linha; filtros na URL; a
 * primeira coluna leva à aba BOM da OS.
 */
export function BomsPage() {
  const [search] = useTableSearch()
  const { situacao } = useTableFilters(['situacao'])
  const raw = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const opened = useRef(false)
  // CA 17: sem filtro na URL, a lista abre em "OS que vão parar sem BOM"; depois, "Todos" (vazio) mostra todas as OS em aberto.
  useEffect(() => {
    if (opened.current) return
    opened.current = true
    if (raw.situacao === undefined) void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ...prev, situacao: DEFAULT_BOM_SITUATION }) } as never)
  }, [raw.situacao, navigate])
  const query = useListBoms({ situation: situacao ? bomSituationParam(situacao) : undefined, q: search.q, page: search.page, pageSize: search.pageSize })
  const client = useQueryClient()
  const canCreate = useCan('engineering.create')
  const canApprove = useCan('engineering.approve')
  const refresh = () => client.invalidateQueries({ queryKey: getListBomsQueryKey() })

  const columns: ColumnDef<BomOrderSummary>[] = [
    {
      id: 'number', header: 'OS', cell: ({ row: { original: r } }) => (
        <Link to={'/app/producao/os/$id' as never} params={{ id: r.productionOrderId } as never} search={{ aba: 'bom' } as never} className="font-semibold text-primary hover:underline">{r.number}</Link>
      ),
    },
    { id: 'product', header: 'Produto', cell: ({ row: { original: r } }) => (r.productName ? `${r.productCode ? r.productCode + ' · ' : ''}${r.productName}` : <StatusChip tone="warn">Sem item de estoque</StatusChip>) },
    { id: 'quantity', header: 'Qtd.', meta: { numeric: true, secondary: true }, cell: ({ row: { original: r } }) => qty(r.quantity) },
    {
      id: 'step', header: 'Etapa atual', cell: ({ row: { original: r } }) => (
        <span>
          {r.currentStepCode ? `${r.currentStepCode} · ${r.currentStepName} (${r.currentStepSequence}/${r.stepCount})` : FABRICATION_STATUS[r.orderStatus]?.label ?? r.orderStatus}
          {r.willStop ? <span className="mt-1 block"><StatusChip tone="crit">Vai parar sem BOM</StatusChip></span> : null}
        </span>
      ),
    },
    {
      id: 'situation', header: 'BOM', cell: ({ row: { original: r } }) => (
        <span>
          <StatusChip tone={BOM_SITUATION[r.situation]?.tone ?? 'neutral'}>{BOM_SITUATION[r.situation]?.label ?? r.situation}</StatusChip>
          {r.releasedRevision !== null && r.releasedRevision !== undefined ? <span className="block text-xs text-muted-foreground">Revisão {r.releasedRevision}{r.releasedAt ? ` · ${formatInstantDate(r.releasedAt)}` : ''}</span> : null}
          {r.draftBomId ? <span className="block text-xs text-muted-foreground">Rascunho com {r.draftLineCount} {r.draftLineCount === 1 ? 'item' : 'itens'}</span> : null}
        </span>
      ),
    },
    { id: 'actions', header: 'Ações', cell: ({ row: { original: r } }) => <RowActions row={r} canCreate={canCreate} canApprove={canApprove} onDone={refresh} /> },
  ]
  return (
    <>
      <PageHeading title="BOMs das OS">
        <p className="mt-2 text-muted-foreground">A lista de materiais de cada OS de fabricação em aberto. A partir da segunda etapa, a OS só anda com a BOM liberada.</p>
      </PageHeading>
      <DataTable label="BOMs das OS" columns={columns} query={query} getRowId={(r) => r.productionOrderId} searchPlaceholder="Buscar pela OS ou pelo produto"
        rowClassName={(r) => (r.willStop ? 'border-l-4 border-l-crit' : undefined)}
        filters={[{ key: 'situacao', label: 'Situação da BOM', options: BOM_SITUATIONS.filter((s) => s.value !== 'todas').map(({ value, label }) => ({ value, label })) }]}
        filteredEmpty={situacao === DEFAULT_BOM_SITUATION ? { title: 'Nenhuma OS vai parar por falta de BOM.', description: 'Todas as OS que já passaram da primeira etapa têm a BOM liberada.' } : undefined}
        empty={{ title: 'Nenhuma OS de fabricação em aberto.', description: 'As OS nascem quando o orçamento vira contrato. A BOM de cada uma é montada na aba BOM da OS.' }} />
    </>
  )
}

/** "Criar BOM" (sem BOM) e "Liberar BOM sem material" (sem BOM ou rascunho vazio), para destravar a OS só de mão de obra (CA 9, 17). */
function RowActions({ row: r, canCreate, canApprove, onDone }: { row: BomOrderSummary; canCreate: boolean; canApprove: boolean; onDone: () => Promise<void> }) {
  const navigate = useNavigate()
  const openBom = () => void navigate({ to: '/app/producao/os/$id', params: { id: r.productionOrderId }, search: { aba: 'bom' } } as never)
  const empty = r.situation === 'None' || (r.situation === 'Draft' && r.draftLineCount === 0)
  return (
    <span className="flex flex-wrap gap-1">
      {r.situation === 'None' && canCreate ? (
        <ConfirmDialog verb="Criar BOM" done="BOM criada" title={`Criar a BOM da ${r.number}?`} description="Nasce a revisão em rascunho, vazia. Depois, monte as linhas na aba BOM da OS."
          trigger={<button type="button" className={action}>Criar BOM</button>}
          onConfirm={async () => { await createBom({ productionOrderId: r.productionOrderId, copyFromBomId: null }, { headers: { [IDEMPOTENCY_HEADER]: crypto.randomUUID() } }); await onDone(); openBom() }} />
      ) : null}
      {empty && canApprove && (r.situation === 'Draft' || canCreate) ? (
        <ConfirmDialog verb="Liberar BOM sem material" done="BOM liberada" title={`Liberar a BOM da ${r.number} sem material?`}
          description="BOM sem material: esta OS usa só mão de obra. Nada é reservado e as etapas seguintes podem começar."
          trigger={<button type="button" className={action}>Liberar BOM sem material</button>}
          onConfirm={async () => {
            const bom = r.situation === 'Draft' && r.draftBomId
              ? { id: r.draftBomId, version: r.draftVersion ?? 0 }
              : await createBom({ productionOrderId: r.productionOrderId, copyFromBomId: null }, { headers: { [IDEMPOTENCY_HEADER]: crypto.randomUUID() } })
            await releaseBom(bom.id, { version: bom.version })
            await onDone()
          }} />
      ) : null}
      {r.situation !== 'None' ? <button type="button" className={action} onClick={openBom}>Abrir BOM</button> : null}
    </span>
  )
}
