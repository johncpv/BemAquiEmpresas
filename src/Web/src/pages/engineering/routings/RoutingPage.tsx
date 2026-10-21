import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import {
  activateRoutingTemplate, deleteRoutingTemplate, getGetRoutingTemplateQueryKey, getListRoutingTemplatesQueryKey, inactivateRoutingTemplate, updateRoutingTemplate,
  useGetRoutingTemplate, useListRoutingProductTypes, useListRoutingWorkCenters, useListSuggestedRoutingWorkCenters,
} from '@/api/generated/bemaqui'
import type { RoutingTemplateDetail } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatBrDecimal } from '@/lib/decimal'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { RoutingEditor } from './RoutingEditor'
import { RoutingHistory } from './RoutingHistory'
import { RoutingProducts } from './RoutingProducts'
import { type RoutingDraft, toDraftSteps, toStepsInput } from './routings'

const action = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/** Roteiro (ENG-01): cabeçalho com a situação e as letras; abas Etapas, Produtos e Histórico de alterações, na URL. */
export function RoutingPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { criado } = useSearch({ strict: false }) as { criado?: unknown }
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetRoutingTemplate(id)
  const [justCreated, setJustCreated] = useState(String(criado) === '1')
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o roteiro." message="Ele pode ter sido excluído. Volte à lista ou tente de novo." onRetry={() => query.refetch()} />
  const t = query.data
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetRoutingTemplateQueryKey(id) })
    await client.invalidateQueries({ queryKey: getListRoutingTemplatesQueryKey() })
  }

  return (
    <>
      <PageHeading title={t.name}>
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <StatusChip tone={t.isActive ? 'ok' : 'neutral'}>{t.isActive ? 'Ativo' : 'Inativo'}</StatusChip>
          {t.isExample ? <Tag>Exemplo</Tag> : null}
          {t.steps.length > 0 ? <StageIndicator stages={t.steps.map((s) => s.name)} letters={t.steps.map((s) => s.code)} current={0} /> : null}
          <span className="text-sm text-muted-foreground tabular-nums">{t.productType ? `${t.productType} · ` : ''}{t.steps.length} etapas · {formatBrDecimal(t.totalStandardHours.toFixed(4), 2)} h padrão{t.usage ? ` · Usado em ${t.usage}` : ''}</span>
          <RoutingActions template={t} onChange={refresh} />
        </div>
      </PageHeading>
      {justCreated ? (
        <div className="mb-4"><NextSteps onDismiss={() => setJustCreated(false)} steps={[
          ...(t.itemCount === 0 ? [{ label: 'Vincular produtos', onSelect: () => void navigate({ to: '.', search: { aba: 'produtos' } } as never) }] : []),
          { label: 'Duplicar para outro produto', onSelect: () => void navigate({ to: '/app/engenharia/roteiros/novo', search: { de: t.id } } as never) },
        ]} /></div>
      ) : null}
      <UrlTabs label="Seções do roteiro" fallback="etapas" tabs={[
        { id: 'etapas', label: 'Etapas', content: <RoutingSteps key={t.version} template={t} onSaved={refresh} /> },
        { id: 'produtos', label: 'Produtos', content: <RoutingProducts template={t} onChange={refresh} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RoutingHistory id={t.id} /> },
      ]} />
    </>
  )
}

/** Aba Etapas: o editor com o aviso de que as OS já criadas não mudam; sem engineering.edit, só leitura com o motivo. */
function RoutingSteps({ template: t, onSaved }: { template: RoutingTemplateDetail; onSaved: () => Promise<void> }) {
  const canEdit = useCan('engineering.edit')
  const centers = useListRoutingWorkCenters()
  const suggestions = useListSuggestedRoutingWorkCenters()
  const types = useListRoutingProductTypes()
  if (centers.isPending) return <Skeleton className="h-64" />
  if (centers.isError) return <ErrorState title="Não foi possível carregar os centros de trabalho." onRetry={() => centers.refetch()} />
  const initial: RoutingDraft = { name: t.name, productType: t.productType ?? '', steps: toDraftSteps(t.steps, centers.data) }
  const missing = t.steps.some((s) => !s.workCenterId)
  const submit = async (draft: RoutingDraft) => {
    await updateRoutingTemplate(t.id, { name: draft.name.trim(), productType: draft.productType.trim() || null, steps: toStepsInput(draft.steps), version: t.version })
    await onSaved()
  }

  return (
    <div className="space-y-4">
      {!canEdit ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Você pode consultar este roteiro. Para alterar, peça a permissão Editar em Engenharia ao administrador.</p> : null}
      {!t.isActive && missing ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Escolha o centro de trabalho de cada etapa para ativar.</p> : null}
      <RoutingEditor initial={initial} centers={centers.data} suggestions={suggestions.data ?? []} productTypes={types.data ?? []} canEdit={canEdit} confirmEdit onSubmit={submit} onReload={onSaved} />
    </div>
  )
}

/** Duplicar, inativar/ativar e excluir (CA 9 a 11); excluir recusado por uso oferece "Inativar" (ADR 0016 D10). */
function RoutingActions({ template: t, onChange }: { template: RoutingTemplateDetail; onChange: () => Promise<void> }) {
  const navigate = useNavigate()
  const canCreate = useCan('engineering.create')
  const canEdit = useCan('engineering.edit')
  const canDelete = useCan('engineering.delete')
  const ready = t.steps.length > 0 && t.steps.every((s) => s.workCenterId && s.workCenterActive)
  return (
    <span className="flex flex-wrap gap-2">
      {canCreate ? <button type="button" onClick={() => void navigate({ to: '/app/engenharia/roteiros/novo', search: { de: t.id } } as never)} className={action}>Duplicar</button> : null}
      {canEdit && t.isActive ? (
        <ConfirmDialog verb="Inativar roteiro" done="Roteiro inativado" title={`Inativar o roteiro ${t.name}?`} description="Ele some dos seletores e da sugestão pelo produto. As OS que já o usam continuam como estão."
          onConfirm={async () => { await inactivateRoutingTemplate(t.id); await onChange() }} trigger={<button type="button" className={action}>Inativar roteiro</button>} />
      ) : null}
      {canEdit && !t.isActive ? (
        <button type="button" disabled={!ready} title={ready ? undefined : 'Escolha o centro de trabalho de cada etapa para ativar.'}
          onClick={async () => { await activateRoutingTemplate(t.id); notifySuccess('Roteiro ativado'); await onChange() }} className={`${action} disabled:opacity-50`}>Ativar roteiro</button>
      ) : null}
      {canDelete ? (
        <ConfirmDialog destructive verb="Excluir roteiro" done="Roteiro excluído" title={`Excluir o roteiro ${t.name}?`}
          description={t.itemCount > 0 ? `${t.itemCount} ${t.itemCount === 1 ? 'produto fica' : 'produtos ficam'} sem roteiro.` : 'Nenhum produto usa este roteiro.'}
          onConfirm={async () => { await deleteRoutingTemplate(t.id); await navigate({ to: '/app/engenharia/roteiros' } as never) }}
          recovery={canEdit ? { verb: 'Inativar', done: 'Roteiro inativado', when: (code) => code === 'routing_template_in_use', onSelect: async () => { await inactivateRoutingTemplate(t.id); await onChange() } } : undefined}
          trigger={<button type="button" className={`${action} text-crit-ink`}>Excluir roteiro</button>} />
      ) : null}
    </span>
  )
}