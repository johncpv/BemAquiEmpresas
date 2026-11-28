import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { applyProjectStageTemplate, getGetProjectQueryKey, getGetProjectStagesQueryKey, getGetProjectTimelineQueryKey, markProjectStageCheck, useGetProjectStages } from '@/api/generated/bemaqui'
import type { ProjectStatus, ProjectStagesView, ProjectStageView, StageChecklistResult } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { IDEMPOTENCY_HEADER } from '@/api/idempotency'
import { notifyError } from '@/components/ui/messages'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { StageActionDialog } from './StageActionDialog'
import { currentStageOf, doneText, hoursText, plannedText, ROLE_LABELS, STAGE_DICAS, STAGE_EXTRA_DICAS, STAGE_VERBS, type StageAction, stageAction, STATUS_LABELS, whoCompletes } from './stages'

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
 * progresso do checklist. Vazio: "Usar um modelo de etapas" (projects.edit). Celular: cartões. EXE-03b: o painel da etapa atual com o checklist e
 * "Iniciar etapa", "Concluir etapa" e "Aprovar etapa"; no celular, o botão da etapa atual fica fixo no rodapé.
 */
export function ProjectStagesTab({ projectId, projectStatus = 'InProgress', onChanged }: { projectId: string; projectStatus?: ProjectStatus; onChanged?: () => Promise<unknown> | void }) {
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
      <StagePanel view={view} projectStatus={projectStatus} onChanged={onChanged} />
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
                <td className="px-3 py-2"><StatusChip tone={STATUS_LABELS[s.status].tone}>{STATUS_LABELS[s.status].label}</StatusChip><StageTags stage={s} /></td>
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
            <StageTags stage={s} />
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

/** "Exige aprovação" e "Autoaprovada", com a dica (EXE-03b). */
function StageTags({ stage }: { stage: ProjectStageView }) {
  if (!stage.requiresApproval && !stage.selfApproved) return null
  return (
    <span className="mt-1 flex flex-wrap items-center gap-1">
      {stage.selfApproved ? <span className="inline-flex items-center"><Tag>Autoaprovada</Tag><Dica nome="Autoaprovada" texto={STAGE_EXTRA_DICAS.autoaprovada} /></span>
        : stage.requiresApproval ? <span className="inline-flex items-center"><Tag>Exige aprovação</Tag><Dica nome="Exige aprovação" texto={STAGE_EXTRA_DICAS.exigeAprovacao} /></span> : null}
    </span>
  )
}

const RESULT_LABELS: Record<NonNullable<StageChecklistResult>, string> = { Done: 'Feito', NotApplicable: 'Não se aplica' }

/**
 * Painel da etapa atual (EXE-03b CA 1 a 5): checklist (Feito / Não se aplica / Desmarcar, com a etapa em andamento e projects.edit) e o botão
 * da etapa; sem a permissão, a linha "quem pode". No celular, o botão fica fixo no rodapé.
 */
function StagePanel({ view, projectStatus, onChanged }: { view: ProjectStagesView; projectStatus: ProjectStatus; onChanged?: () => Promise<unknown> | void }) {
  const client = useQueryClient()
  const perms: Record<string, boolean> = { 'projects.edit': useCan('projects.edit'), 'projects.approve': useCan('projects.approve'), 'projects.register_acceptance': useCan('projects.register_acceptance') }
  const [open, setOpen] = useState<StageAction | null>(null)
  const [busy, setBusy] = useState(false)
  const stage = currentStageOf(view.stages ?? [])
  if (!stage || projectStatus === 'Closed' || projectStatus === 'Cancelled') return null
  const action = stageAction(stage, (p) => perms[p] ?? false)
  const canMark = stage.status === 'InProgress' && perms['projects.edit']
  const who = stage.status === 'InProgress' && !action ? whoCompletes(stage)
    : stage.status === 'AwaitingApproval' && !action ? 'Quem aprova: usuários com permissão de aprovar projetos.'
      : stage.status === 'Ready' && !action ? 'Quem inicia: usuários com permissão de editar projetos.' : null

  const mark = async (sequence: number, result: NonNullable<StageChecklistResult> | null) => {
    setBusy(true)
    try {
      await markProjectStageCheck(view.projectId, stage.id, sequence, { result, version: view.version }, { headers: { [IDEMPOTENCY_HEADER]: crypto.randomUUID() } })
      await client.invalidateQueries({ queryKey: getGetProjectStagesQueryKey(view.projectId) })
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível marcar o item.', () => void client.invalidateQueries({ queryKey: getGetProjectStagesQueryKey(view.projectId) }))
    } finally {
      setBusy(false)
    }
  }

  const button = action ? (
    <button type="button" onClick={() => setOpen(action)} className={primaryClass}>{STAGE_VERBS[action].verb}</button>
  ) : null
  return (
    <div className="rounded-md border border-border bg-card p-3" data-testid="painel-etapa">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="inline-flex min-w-0 items-center gap-2">
          <StageLetter code={stage.code} /><b className="truncate">{stage.name}</b>
          <StatusChip tone={STATUS_LABELS[stage.status].tone}>{STATUS_LABELS[stage.status].label}</StatusChip>
        </span>
        {button ? <span className="hidden sm:inline-flex">{button}</span> : null}
      </div>
      <StageTags stage={stage} />
      {who ? <p className="mt-2 text-sm text-muted-foreground">{who}</p> : null}
      {(stage.checklist ?? []).length > 0 ? (
        <ul className="mt-3 space-y-2" aria-label="Checklist da etapa">
          {(stage.checklist ?? []).map((c) => (
            <li key={c.id} className="flex flex-wrap items-center justify-between gap-2 text-sm">
              <span className="min-w-0">
                {c.text}
                {c.result ? <span className="block text-xs text-muted-foreground">{RESULT_LABELS[c.result]}{c.markedByName ? ` · ${c.markedByName}` : ''}</span> : null}
              </span>
              {canMark ? (
                <span className="flex gap-1">
                  {(['Done', 'NotApplicable'] as const).map((r) => (
                    <button key={r} type="button" disabled={busy} aria-pressed={c.result === r} onClick={() => void mark(c.sequence, c.result === r ? null : r)}
                      className={`min-h-11 rounded-md border px-3 text-xs font-semibold ${c.result === r ? 'border-primary bg-primary text-primary-foreground' : 'border-border'}`}>
                      {RESULT_LABELS[r]}
                    </button>
                  ))}
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
      {button ? <div className="fixed inset-x-0 bottom-16 z-20 border-t border-border bg-background p-3 sm:hidden [&>button]:min-h-11 [&>button]:w-full">{button}</div> : null}
      {button ? <div aria-hidden="true" className="h-16 sm:hidden" /> : null}
      {open ? <StageActionDialog view={view} stage={stage} action={open} projectStatus={projectStatus} open onOpenChange={(o) => { if (!o) setOpen(null) }} onDone={onChanged} /> : null}
    </div>
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
