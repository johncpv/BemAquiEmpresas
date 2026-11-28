import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { applyProjectStageTemplate, getGetProjectQueryKey, getGetProjectStagesQueryKey, getGetProjectTimelineQueryKey, useGetProjectStages } from '@/api/generated/bemaqui'
import type { ProjectStagesView, ProjectStageView } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { doneText, hoursText, plannedText, ROLE_LABELS, STAGE_DICAS, STATUS_LABELS } from './stages'

const secondaryClass = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primaryClass = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/** Letra da etapa no estilo do display de andar (âmbar sobre fundo escuro). */
function StageLetter({ code }: { code: string }) {
  return <span aria-hidden="true" className="inline-flex size-7 shrink-0 items-center justify-center rounded bg-rail font-display text-sm font-extrabold text-signal">{code}</span>
}

function Checklist({ stage }: { stage: ProjectStageView }) {
  return stage.checklistTotal > 0 ? <span className="tabular-nums">{stage.checklistDone}/{stage.checklistTotal}</span> : <span className="text-muted-foreground">—</span>
}

/**
 * Aba Etapas do projeto de serviço (EXE-03a CA 7): cada etapa do modelo com letra, nome, papel, situação, responsável, previsto, realizado e o
 * progresso do checklist, em leitura. Vazio: "Usar um modelo de etapas" (projects.edit). Celular: cartões.
 */
export function ProjectStagesTab({ projectId }: { projectId: string }) {
  const query = useGetProjectStages(projectId)
  const canEdit = useCan('projects.edit')
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar as etapas." message="Verifique a conexão e tente de novo." onRetry={() => query.refetch()} />
  const view = query.data
  const stages = view.stages ?? []
  const canApply = canEdit && view.canApplyTemplate && (view.templates ?? []).length > 0
  if (stages.length === 0) {
    return (
      <EmptyState title="Este projeto ainda não tem etapas."
        description={canApply ? 'Use um modelo de etapas para ver o caminho até o aceite do cliente.' : (view.templates ?? []).length === 0 && canEdit ? 'Cadastre um modelo de etapas em Projetos > Modelos de etapas.' : undefined}
        action={canApply ? <ApplyTemplateDialog view={view} label="Usar um modelo de etapas" primary /> : undefined} />
    )
  }

  return (
    <section aria-label="Etapas do projeto" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="inline-flex items-center text-sm text-muted-foreground">
          {view.templateName ? `Modelo ${view.templateName} (revisão ${view.templateRevision})` : 'Etapas do projeto'}
          <Dica nome="Modelo de etapas" texto={STAGE_DICAS.modelo} />
        </p>
        {canApply ? <ApplyTemplateDialog view={view} label="Trocar o modelo" /> : null}
      </div>
      <div className="hidden overflow-x-auto rounded-md border border-border sm:block">
        <table className="w-full text-sm">
          <thead className="bg-muted text-left text-xs text-muted-foreground">
            <tr>
              <th className="px-3 py-2 font-semibold">Etapa</th>
              <th className="px-3 py-2 font-semibold">Papel</th>
              <th className="px-3 py-2 font-semibold">Situação</th>
              <th className="px-3 py-2 font-semibold">Responsável</th>
              <th className="px-3 py-2 font-semibold">Previsto</th>
              <th className="px-3 py-2 font-semibold">Realizado</th>
              <th className="px-3 py-2 text-right font-semibold">Checklist</th>
            </tr>
          </thead>
          <tbody>
            {stages.map((s) => (
              <tr key={s.id} className="border-t border-border">
                <td className="px-3 py-2"><span className="inline-flex items-center gap-2"><StageLetter code={s.code} /><span><b>{s.name}</b><span className="block text-xs text-muted-foreground tabular-nums">{hoursText(s.plannedHours)}</span></span></span></td>
                <td className="px-3 py-2">{ROLE_LABELS[s.role]}</td>
                <td className="px-3 py-2"><StatusChip tone={STATUS_LABELS[s.status].tone}>{STATUS_LABELS[s.status].label}</StatusChip></td>
                <td className="px-3 py-2">{s.responsibleName ?? <span className="text-muted-foreground">Sem responsável</span>}</td>
                <td className="px-3 py-2 tabular-nums">{plannedText(s.plannedStart, s.plannedEnd)}</td>
                <td className="px-3 py-2 tabular-nums">{doneText(s.completedOn, s.startedAt)}</td>
                <td className="px-3 py-2 text-right"><Checklist stage={s} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ol className="space-y-2 sm:hidden" aria-label="Etapas">
        {stages.map((s) => (
          <li key={s.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <div className="flex items-start justify-between gap-2">
              <span className="inline-flex min-w-0 items-center gap-2"><StageLetter code={s.code} /><b className="truncate">{s.name}</b></span>
              <StatusChip tone={STATUS_LABELS[s.status].tone}>{STATUS_LABELS[s.status].label}</StatusChip>
            </div>
            <dl className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
              <dt className="text-muted-foreground">Papel</dt><dd>{ROLE_LABELS[s.role]}</dd>
              <dt className="text-muted-foreground">Responsável</dt><dd>{s.responsibleName ?? 'Sem responsável'}</dd>
              <dt className="text-muted-foreground">Previsto</dt><dd className="tabular-nums">{plannedText(s.plannedStart, s.plannedEnd)}</dd>
              <dt className="text-muted-foreground">Realizado</dt><dd className="tabular-nums">{doneText(s.completedOn, s.startedAt)}</dd>
              <dt className="text-muted-foreground">Checklist</dt><dd><Checklist stage={s} /></dd>
            </dl>
          </li>
        ))}
      </ol>
    </section>
  )
}

/**
 * "Usar um modelo de etapas" (CA 3) e "Trocar o modelo" (CA 5): o sugerido vem marcado. No projeto já entregue, avisa que as etapas nascem
 * concluídas sem liberar parcela.
 */
function ApplyTemplateDialog({ view, label, primary }: { view: ProjectStagesView; label: string; primary?: boolean }) {
  const client = useQueryClient()
  const templates = view.templates ?? []
  const initial = () => view.suggestedTemplateId ?? view.templateId ?? templates[0]?.id ?? ''
  const [chosen, setChosen] = useState(initial)
  const replacing = (view.stages ?? []).length > 0
  const confirm = async () => {
    await applyProjectStageTemplate(view.projectId, { templateId: chosen, version: view.version })
    await client.invalidateQueries({ queryKey: getGetProjectStagesQueryKey(view.projectId) })
    await client.invalidateQueries({ queryKey: getGetProjectQueryKey(view.projectId) })
    await client.invalidateQueries({ queryKey: getGetProjectTimelineQueryKey(view.projectId) })
  }

  return (
    <ConfirmDialog trigger={<button type="button" className={primary ? primaryClass : secondaryClass}>{label}</button>}
      verb={replacing ? 'Trocar o modelo' : 'Usar o modelo'} done="Etapas copiadas do modelo" title={label} onOpen={() => setChosen(initial())}
      onConfirm={confirm} onReload={() => client.invalidateQueries({ queryKey: getGetProjectStagesQueryKey(view.projectId) })}
      description={replacing ? 'As etapas atuais saem e as do modelo escolhido entram no lugar.' : 'As etapas do modelo são copiadas para este projeto. Nenhuma parcela é liberada.'}
      preconditions={[{ ok: chosen !== '', text: 'Escolha um modelo.' }]}>
      <fieldset className="space-y-2">
        <legend className="sr-only">Modelo de etapas</legend>
        {templates.map((t) => (
          <label key={t.id} className="flex min-h-11 items-center gap-2 rounded-md border border-border px-3 py-2 text-sm">
            <input type="radio" name={`modelo-${view.projectId}`} value={t.id} checked={chosen === t.id} onChange={() => setChosen(t.id)} className="size-4" />
            <span className="flex-1">{t.name}</span>
            {t.id === view.suggestedTemplateId ? <Tag>Sugerido</Tag> : null}
          </label>
        ))}
      </fieldset>
    </ConfirmDialog>
  )
}
