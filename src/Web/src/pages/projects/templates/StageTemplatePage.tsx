import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import {
  activateStageTemplate, deleteStageTemplate, getGetStageTemplateQueryKey, getListStageTemplatesQueryKey, inactivateStageTemplate, setDefaultStageTemplate, updateStageTemplate,
  useGetStageTemplate, useListStageTemplatePositions, useListStageTemplateSegments,
} from '@/api/generated/bemaqui'
import type { StageTemplateDetail } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { NextSteps } from '@/components/ui/next-steps'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatBrDecimal } from '@/lib/decimal'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { StageTemplateEditor } from './StageTemplateEditor'
import { StageTemplateHistory } from './StageTemplateHistory'
import { StageTemplateItems } from './StageTemplateItems'
import { milestoneText, type TemplateDraft, toDraft, toInput, usageText } from './stage-templates'

const action = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/** Modelo de etapas (EXE-02): cabeçalho com chips e letras; abas Etapas, Itens e Histórico de alterações, na URL. */
export function StageTemplatePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { criado } = useSearch({ strict: false }) as { criado?: unknown }
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetStageTemplate(id)
  const [justCreated, setJustCreated] = useState(String(criado) === '1')
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o modelo." message="Ele pode ter sido excluído. Volte à lista ou tente de novo." onRetry={() => query.refetch()} />
  const t = query.data
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetStageTemplateQueryKey(id) })
    await client.invalidateQueries({ queryKey: getListStageTemplatesQueryKey() })
  }

  return (
    <>
      <PageHeading title={t.name}
        meta={
          <>
            <StatusChip tone={t.isActive ? 'ok' : 'neutral'}>{t.isActive ? 'Ativo' : 'Inativo'}</StatusChip>
            {t.isDefault ? <StatusChip tone="info">Padrão{t.segmentName ? ` de ${t.segmentName}` : ''}</StatusChip> : null}
            {t.isSuggested ? <Tag>Sugerido — ajuste ao seu jeito</Tag> : null}
            {t.steps.length > 0 ? <StageIndicator stages={t.steps.map((s) => s.name)} letters={t.steps.map((s) => s.code)} current={0} /> : null}
            <span className="text-sm text-muted-foreground tabular-nums">
              {t.steps.length} etapas · {formatBrDecimal(t.totalHoursPerUnit.toFixed(4), 2)} h por unidade · revisão {t.revision}{t.usageCount > 0 ? ` · ${usageText(t.usageCount)}` : ''}
            </span>
            {t.suggestedMilestones.length > 0 ? <span className="w-full text-xs text-muted-foreground">Marcos sugeridos: {t.suggestedMilestones.map(milestoneText).join(' · ')}</span> : null}
          </>
        }
        actions={<TemplateActions template={t} onChange={refresh} />} />
      {justCreated ? (
        <div className="mb-4"><NextSteps onDismiss={() => setJustCreated(false)} steps={[
          ...(t.itemCount === 0 ? [{ label: 'Vincular itens de serviço', onSelect: () => void navigate({ to: '.', search: { aba: 'itens' } } as never) }] : []),
          ...(!t.isDefault ? [{ label: 'Usar como padrão', onSelect: async () => { await setDefaultStageTemplate(t.id, { version: t.version }); await refresh() } }] : []),
        ]} /></div>
      ) : null}
      <UrlTabs label="Seções do modelo" fallback="etapas" tabs={[
        { id: 'etapas', label: 'Etapas', content: <TemplateSteps key={t.version} template={t} onSaved={refresh} /> },
        { id: 'itens', label: 'Itens', content: <StageTemplateItems template={t} onChange={refresh} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <StageTemplateHistory id={t.id} /> },
      ]} />
    </>
  )
}

/** Aba Etapas: o editor com o aviso "Usado em {n} projetos" (CA 6); sem projects.edit, só leitura com o motivo. */
function TemplateSteps({ template: t, onSaved }: { template: StageTemplateDetail; onSaved: () => Promise<void> }) {
  const canEdit = useCan('projects.edit')
  const positions = useListStageTemplatePositions()
  const segments = useListStageTemplateSegments()
  if (positions.isPending || segments.isPending) return <Skeleton className="h-64" />
  if (positions.isError || segments.isError) return <ErrorState title="Não foi possível carregar os cargos." onRetry={() => { void positions.refetch(); void segments.refetch() }} />
  const submit = async (draft: TemplateDraft) => {
    await updateStageTemplate(t.id, { ...toInput(draft), version: t.version })
    await onSaved()
  }

  return (
    <div className="space-y-4">
      {!canEdit ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Você pode consultar este modelo. Para alterar, peça a permissão Editar em Projetos ao administrador.</p> : null}
      <StageTemplateEditor initial={toDraft(t)} positions={positions.data} segments={segments.data} canEdit={canEdit} usageCount={t.usageCount} confirmEdit onSubmit={submit} onReload={onSaved} />
    </div>
  )
}

/** Usar como padrão, inativar/ativar e excluir (CA 7 e 8): o padrão não sai sem outro no lugar. */
function TemplateActions({ template: t, onChange }: { template: StageTemplateDetail; onChange: () => Promise<void> }) {
  const navigate = useNavigate()
  const canEdit = useCan('projects.edit')
  const canDelete = useCan('projects.delete')
  const defaultHint = 'Escolha outro modelo padrão antes.'
  return (
    <span className="flex flex-wrap gap-2">
      {canEdit && t.isActive && !t.isDefault ? (
        <ConfirmDialog verb="Usar como padrão" done="Modelo padrão" title={`Usar ${t.name} como padrão${t.segmentName ? ` de ${t.segmentName}` : ''}?`}
          description="Os próximos orçamentos de serviço sem modelo no item sugerem este. O padrão anterior deixa de ser."
          onConfirm={async () => { await setDefaultStageTemplate(t.id, { version: t.version }); await onChange() }} onReload={onChange}
          trigger={<button type="button" className={action}>Usar como padrão</button>} />
      ) : null}
      {canEdit && t.isActive ? (
        <ConfirmDialog verb="Inativar modelo" done="Modelo inativado" title={`Inativar o modelo ${t.name}?`} description="Ele some dos seletores e da sugestão. Os projetos que já o usam continuam como estão."
          onConfirm={async () => { await inactivateStageTemplate(t.id, { version: t.version }); await onChange() }} onReload={onChange}
          trigger={<button type="button" disabled={t.isDefault} title={t.isDefault ? defaultHint : undefined} className={`${action} disabled:opacity-50`}>Inativar modelo</button>} />
      ) : null}
      {canEdit && !t.isActive ? (
        <ConfirmDialog verb="Ativar modelo" done="Modelo ativado" title={`Ativar o modelo ${t.name}?`} description="Ele volta aos seletores e à sugestão."
          onConfirm={async () => { await activateStageTemplate(t.id, { version: t.version }); await onChange() }} onReload={onChange}
          trigger={<button type="button" className={action}>Ativar modelo</button>} />
      ) : null}
      {canDelete ? (
        <ConfirmDialog destructive verb="Excluir modelo" done="Modelo excluído" title={`Excluir o modelo ${t.name}?`}
          description={t.itemCount > 0 ? `${t.itemCount} ${t.itemCount === 1 ? 'item de serviço fica' : 'itens de serviço ficam'} sem modelo. Os projetos que já o usam não mudam.` : 'Os projetos que já o usam não mudam.'}
          onConfirm={async () => { await deleteStageTemplate(t.id, { version: t.version }); await navigate({ to: '/app/projetos/modelos' } as never) }} onReload={onChange}
          trigger={<button type="button" disabled={t.isDefault} title={t.isDefault ? defaultHint : undefined} className={`${action} text-crit-ink disabled:opacity-50`}>Excluir modelo</button>} />
      ) : null}
    </span>
  )
}
