import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useState } from 'react'
import { getListStageTemplatesQueryKey, seedSuggestedStageTemplates, useListStageTemplates } from '@/api/generated/bemaqui'
import type { StageTemplateSummary } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { notifySuccess } from '@/components/ui/messages'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatBrDecimal } from '@/lib/decimal'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { situationParam, TEMPLATE_SITUATIONS, usageText } from './stage-templates'

const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-50'

/** "Usar os sugeridos" (CA 11): cria os modelos do perfil Serviços que faltam; os que o tenant excluiu não voltam. */
export function UseSuggestedButton({ className = secondary }: { className?: string }) {
  const client = useQueryClient()
  const [busy, setBusy] = useState(false)
  const run = async () => {
    setBusy(true)
    try {
      const { created } = await seedSuggestedStageTemplates()
      notifySuccess(created === 0 ? 'Os modelos sugeridos já estão aqui (os que você excluiu não voltam).' : `${created} ${created === 1 ? 'modelo sugerido criado' : 'modelos sugeridos criados'}`)
      await client.invalidateQueries({ queryKey: getListStageTemplatesQueryKey() })
    } finally {
      setBusy(false)
    }
  }
  return <Can perm="projects.edit"><button type="button" disabled={busy} onClick={() => void run()} className={className}>Usar os sugeridos</button></Can>
}

/**
 * Projetos → Modelos de etapas (EXE-02 CA 1): nome, etapas com as letras, horas por unidade somadas, segmento, chips "Padrão" e "Sugerido" e o
 * uso; busca e filtro Ativos/Inativos na URL. Vazio oferece "Criar modelo" e "Usar os sugeridos".
 */
export function StageTemplatesPage() {
  const [search] = useTableSearch()
  const { situacao } = useTableFilters(['situacao'])
  const query = useListStageTemplates({ q: search.q, situation: situationParam(situacao), page: search.page, pageSize: search.pageSize })
  const columns: ColumnDef<StageTemplateSummary>[] = [
    {
      id: 'name', header: 'Nome', cell: ({ row: { original: t } }) => (
        <span className="flex flex-wrap items-center gap-2">
          <Link to={'/app/projetos/modelos/$id' as never} params={{ id: t.id } as never} className="font-semibold text-primary hover:underline">{t.name}</Link>
          {t.isDefault ? <StatusChip tone="info">Padrão</StatusChip> : null}
          {t.isSuggested ? <Tag>Sugerido — ajuste ao seu jeito</Tag> : null}
        </span>
      ),
    },
    {
      id: 'steps', header: 'Etapas', cell: ({ row: { original: t } }) => (t.codes.length === 0 ? '—' : (
        <span className="flex flex-wrap items-center gap-2">
          <StageIndicator stages={t.stepNames} letters={t.codes} current={0} />
          <span className="text-xs text-muted-foreground tabular-nums">{t.codes.length} etapas</span>
        </span>
      )),
    },
    { id: 'hours', header: 'Horas por unidade', meta: { numeric: true, secondary: true }, cell: ({ row: { original: t } }) => <span className="tabular-nums">{formatBrDecimal(t.totalHoursPerUnit.toFixed(4), 2)} h</span> },
    { id: 'segment', header: 'Segmento', meta: { secondary: true }, cell: ({ row: { original: t } }) => t.segmentName ?? 'Toda a empresa' },
    { id: 'status', header: 'Situação', cell: ({ row: { original: t } }) => <StatusChip tone={t.isActive ? 'ok' : 'neutral'}>{t.isActive ? 'Ativo' : 'Inativo'}</StatusChip> },
    { id: 'usage', header: 'Uso', meta: { secondary: true }, cell: ({ row: { original: t } }) => (t.usageCount > 0 ? usageText(t.usageCount) : '—') },
  ]
  const create = <Can perm="projects.edit"><Link to={'/app/projetos/modelos/novo' as never} className={primary}>Criar modelo</Link></Can>
  return (
    <>
      <PageHeading title="Modelos de etapas" description="As etapas com que cada projeto de serviço nasce: papel, horas por unidade, cargo e marcos sugeridos."
        actions={<span className="flex flex-wrap gap-2"><UseSuggestedButton />{create}</span>} />
      <DataTable label="Modelos de etapas" columns={columns} query={query} getRowId={(t) => t.id} searchPlaceholder="Buscar pelo nome"
        filters={[{ key: 'situacao', label: 'Situação', options: TEMPLATE_SITUATIONS }]}
        empty={{ title: 'Nenhum modelo de etapas ainda.', action: <span className="flex flex-wrap justify-center gap-2">{create}<UseSuggestedButton /></span> }} />
    </>
  )
}
