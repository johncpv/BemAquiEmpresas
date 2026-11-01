import { Link, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useListAssetResponsibleChoices, useListAssets, useListAssetSites, useListAssetWorkCenterChoices } from '@/api/generated/bemaqui'
import type { AssetListItem } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatMoney } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { LabelBatchReady, PrintLabelsDialog } from './AssetLabelActions'
import { AssetPhoto } from './AssetPhoto'
import { ASSET_DICAS, isAssetStatus, isAssetType, STATUS_LABELS, STATUS_OPTIONS, STATUS_TONES, TYPE_LABELS, TYPE_OPTIONS } from './assets'
import { AssetsTabs } from './AssetsTabs'

/**
 * Patrimônio → Bens (PAT-01a CA 9): busca por etiqueta, nome e série; filtros por tipo, local, centro, responsável e situação na URL.
 * PAT-01b CA 3: seleção e "Imprimir etiquetas" (os selecionados ou todos do filtro); `?lote=` (link do sino) baixa o PDF pronto.
 */
export function AssetsPage() {
  const [search] = useTableSearch()
  const { tipo, local, centro, responsavel, situacao } = useTableFilters(['tipo', 'local', 'centro', 'responsavel', 'situacao'])
  const canEdit = useCan('assets.edit')
  const [selected, setSelected] = useState<string[]>([])
  const lote = (useSearch({ strict: false }) as Record<string, unknown>).lote
  const sites = useListAssetSites({ includeInactive: true })
  const centers = useListAssetWorkCenterChoices()
  const responsibles = useListAssetResponsibleChoices()
  const query = useListAssets({
    q: search.q, type: isAssetType(tipo) ? tipo : undefined, siteId: local || undefined, workCenterId: centro || undefined, responsibleEmployeeId: responsavel || undefined,
    status: isAssetStatus(situacao) ? situacao : undefined, page: search.page, pageSize: search.pageSize, sort: search.sort,
  })

  const columns: ColumnDef<AssetListItem>[] = [
    {
      id: 'tag', header: () => <span className="inline-flex items-center">Etiqueta<Dica nome="Etiqueta" texto={ASSET_DICAS.etiqueta} /></span>, meta: { sortField: 'tag' },
      cell: ({ row: { original: a } }) => (
        <span className="flex items-center gap-2">
          <AssetPhoto assetId={a.id} photoId={a.photoId} name={a.name} />
          <Link to="/app/patrimonio/bens/$id" params={{ id: a.id }} className="font-semibold tabular-nums text-primary hover:underline">{a.tag}</Link>
        </span>
      ),
    },
    { id: 'name', header: 'Nome', meta: { sortField: 'name' }, cell: ({ row: { original: a } }) => a.name },
    { id: 'type', header: 'Tipo', meta: { secondary: true }, cell: ({ row: { original: a } }) => TYPE_LABELS[a.type] },
    { id: 'site', header: 'Local', meta: { secondary: true }, cell: ({ row: { original: a } }) => a.siteName ?? '—' },
    {
      id: 'responsible', header: 'Responsável', meta: { secondary: true },
      cell: ({ row: { original: a } }) => a.responsibleName ? <span className="flex flex-wrap items-center gap-1">{a.responsibleName}{a.responsibleTerminated ? <StatusChip tone="warn">Desligado</StatusChip> : null}</span> : '—',
    },
    { id: 'center', header: 'Centro', meta: { secondary: true }, cell: ({ row: { original: a } }) => a.workCenterName ?? '—' },
    ...(canEdit ? [{ id: 'value', header: 'Valor de aquisição', meta: { secondary: true }, cell: ({ row: { original: a } }) => <span className="tabular-nums">{a.acquisitionValue == null ? '—' : formatMoney(a.acquisitionValue)}</span> } as ColumnDef<AssetListItem>] : []),
    { id: 'status', header: 'Situação', cell: ({ row: { original: a } }) => <StatusChip tone={STATUS_TONES[a.status]}>{STATUS_LABELS[a.status]}</StatusChip> },
  ]

  const newLink = <Can perm="assets.create"><Link to="/app/patrimonio/bens/novo" className="inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo bem</Link></Can>
  return (
    <>
      <PageHeading title="Patrimônio: Bens">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">O que a empresa tem, onde está e quem responde por cada bem.</p>
          <div className="flex flex-wrap gap-2">
            <PrintLabelsDialog selected={selected} filteredTotal={query.data?.total ?? 0} onDone={() => setSelected([])}
              filter={{ q: search.q ?? null, type: isAssetType(tipo) ? tipo : null, siteId: local || null, workCenterId: centro || null, responsibleEmployeeId: responsavel || null, status: isAssetStatus(situacao) ? situacao : null }} />
            {newLink}
          </div>
        </div>
      </PageHeading>
      <AssetsTabs />
      {typeof lote === 'string' && lote ? <LabelBatchReady attachmentId={lote} /> : null}
      <DataTable
        label="Bens"
        columns={columns}
        query={query}
        getRowId={(a) => a.id}
        selectable={{ selected, onChange: setSelected, rowLabel: (a) => `bem ${a.tag}` }}
        rowHref={(a) => `/app/patrimonio/bens/${a.id}`}
        searchPlaceholder="Buscar pela etiqueta, nome ou série"
        filters={[
          { key: 'tipo', label: 'Tipo', options: TYPE_OPTIONS },
          { key: 'local', label: 'Local', options: (sites.data ?? []).map((s) => ({ value: s.id, label: s.name })) },
          { key: 'centro', label: 'Centro', options: (centers.data ?? []).map((c) => ({ value: c.id, label: c.name })) },
          { key: 'responsavel', label: 'Responsável', options: (responsibles.data ?? []).map((r) => ({ value: r.id, label: r.name })) },
          { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS },
        ]}
        empty={{
          title: 'Cadastre as máquinas e ferramentas para acompanhar manutenção e custo.',
          description: 'Comece pelas máquinas da fábrica: o centro de trabalho sugere o local e o responsável.',
          action: <Can perm="assets.create"><Link to="/app/patrimonio/bens/novo" className="inline-flex min-h-11 items-center text-sm font-semibold text-primary">Novo bem</Link></Can>,
        }}
      />
    </>
  )
}
