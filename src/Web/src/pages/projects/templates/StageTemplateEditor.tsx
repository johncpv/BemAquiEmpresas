import { type ReactNode, useState } from 'react'
import { flushSync } from 'react-dom'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DecimalInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { InlineError } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { ChecklistPanel } from '@/pages/engineering/routings/ChecklistPanel'
import { normalizeLetter } from '@/pages/engineering/routings/routings'
import {
  draftProblems, executionCodes, MAX_CHECKLIST_TEXT, MAX_MILESTONES, MAX_STEPS, type MilestoneDraft, milestoneSum, type MilestoneTriggerCode, newMilestone, newStage, renameStage, ROLE_LABEL, ROLES,
  type StageDraft, type StageRoleCode, TEMPLATE_DICAS, type TemplateDraft, totalHours, TRIGGER_LABEL, TRIGGERS, usageText,
} from './stage-templates'

export interface PositionOption {
  id: string
  name: string
}

export interface SegmentOption {
  code: string
  name: string
}

interface StageTemplateEditorProps {
  initial: TemplateDraft
  positions: readonly PositionOption[]
  segments: readonly SegmentOption[]
  canEdit: boolean
  /** Modelo existente: confirma avisando que os projetos que já usam não mudam (CA 6). */
  usageCount?: number
  confirmEdit?: boolean
  onSubmit: (draft: TemplateDraft) => Promise<(() => void) | void>
  onReload?: () => Promise<unknown>
  footerExtra?: ReactNode
}

const box = 'size-4 accent-primary'
const selectClass = inputClass

/**
 * Editor do modelo de etapas (EXE-02 CA 2 a 6): nome, segmento, etapas no RowsEditor (a 400 px cada etapa vira cartão com "Mover para cima" e
 * "Mover para baixo", sem arrastar, e o checklist recolhido num botão), marcos sugeridos com a soma ao vivo, prévia do indicador com as
 * letras, total de horas por unidade no rodapé e os erros do servidor na linha (steps[n].campo).
 */
export function StageTemplateEditor({ initial, positions, segments, canEdit, usageCount = 0, confirmEdit, onSubmit, onReload, footerExtra }: StageTemplateEditorProps) {
  const [draft, setDraft] = useState<TemplateDraft>(initial)
  const [baseline, setBaseline] = useState<TemplateDraft>(initial)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<string | undefined>()
  const [checklistOf, setChecklistOf] = useState<number | null>(null)
  const [confirming, setConfirming] = useState(false)
  const dirty = JSON.stringify(draft) !== JSON.stringify(baseline)
  const stepField = (i: number, name: string) => fields[`steps[${i}].${name}`]
  const milestoneField = (i: number, name: string) => fields[`suggestedMilestones[${i}].${name}`]
  const clear = (prefix: string) => setFields(Object.fromEntries(Object.entries(fields).filter(([k]) => !k.startsWith(prefix))))
  const setSteps = (steps: StageDraft[]) => {
    setDraft({ ...draft, steps })
    clear('steps')
  }
  const setMilestones = (milestones: MilestoneDraft[]) => {
    setDraft({ ...draft, milestones })
    clear('suggestedMilestones')
  }

  const focusFirstError = () => requestAnimationFrame(() => document.querySelector<HTMLElement>('#modelo-etapas [aria-invalid="true"]')?.focus())

  const showServer = (e: unknown) => {
    if (e instanceof ApiError) {
      const flat = Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))
      setFields(flat)
      setMessage(Object.keys(flat).length === 0 || Object.keys(flat).every((k) => k === 'steps' || k === 'suggestedMilestones') ? e.message : undefined)
      if (Object.keys(flat).length > 0) focusFirstError()
    } else {
      setMessage('Não foi possível salvar o modelo. Tente de novo.')
    }
  }

  const validate = () => {
    const problems = draftProblems(draft)
    setFields(problems)
    setMessage(problems.steps ?? problems.suggestedMilestones)
    if (Object.keys(problems).length > 0) focusFirstError()
    return Object.keys(problems).length === 0
  }

  const done = (next: (() => void) | void) => {
    flushSync(() => {
      setBaseline(draft)
      setFields({})
      setMessage(undefined)
    })
    next?.()
  }

  const save = async () => {
    try {
      done(await onSubmit(draft))
      return true
    } catch (e) {
      showServer(e)
      return false
    }
  }

  const requestSave = async () => {
    if (!validate()) return false
    if (confirmEdit) {
      setConfirming(true)
      return false
    }
    return save()
  }

  const positionOptions = (term: string) => Promise.resolve(positions
    .filter((p) => !term || p.name.toLocaleLowerCase('pt-BR').includes(term.toLocaleLowerCase('pt-BR')))
    .map((p) => ({ id: p.id, label: p.name })))
  const codes = executionCodes(draft.steps)
  const sum = milestoneSum(draft.milestones)

  return (
    <form id="modelo-etapas" noValidate onSubmit={(e) => { e.preventDefault(); void requestSave() }} className="space-y-5 pb-4">
      <UnsavedChangesGuard when={dirty && canEdit} onSave={requestSave} />
      {usageCount > 0 ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">{usageText(usageCount)}. Mudanças valem só para os próximos.</p> : null}
      <FormError message={message} />
      <fieldset disabled={!canEdit} className="space-y-5">
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Nome" required error={fields.name} hint="Ex.: Implantação de software, Obra / instalação.">
            {(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
          </Field>
          <Field label="Segmento" error={fields.segment} hint="Em branco, vale para toda a empresa." dica="O padrão do segmento é sugerido antes do padrão da empresa.">
            {(p) => (
              <select {...p} value={draft.segment} onChange={(e) => setDraft({ ...draft, segment: e.target.value })} className={selectClass}>
                <option value="">Toda a empresa</option>
                {segments.map((s) => <option key={s.code} value={s.code}>{s.name}</option>)}
              </select>
            )}
          </Field>
        </div>

        <section aria-labelledby="etapas-titulo" className="space-y-2">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
            <h2 id="etapas-titulo" className="text-base font-semibold">Etapas</h2>
            {draft.steps.length > 0 ? <StageIndicator stages={draft.steps.map((s) => s.name || 'Etapa')} letters={draft.steps.map((s) => s.code || '?')} current={0} /> : null}
            <p className="flex flex-wrap items-center gap-x-3 text-xs text-muted-foreground">
              <span className="inline-flex items-center">Papel<Dica nome="Papel" texto={TEMPLATE_DICAS.papel} /></span>
              <span className="inline-flex items-center">Horas por unidade<Dica nome="Horas por unidade" texto={TEMPLATE_DICAS.horas} /></span>
              <span className="inline-flex items-center">Exige aprovação<Dica nome="Exige aprovação" texto={TEMPLATE_DICAS.aprovacao} /></span>
            </p>
          </div>
          <RowsEditor noun="etapa" rows={draft.steps} onChange={setSteps} min={1} max={MAX_STEPS} reorder disabled={!canEdit}
            newRow={() => newStage()}
            columns={[
              {
                label: 'Nome', width: 'minmax(0, 2fr)', render: (row, i, update) => (
                  <div>
                    <input aria-label={`Nome da etapa ${i + 1}`} aria-invalid={!!stepField(i, 'name')} value={row.name} maxLength={60}
                      onChange={(e) => {
                        const renamed = renameStage(row, e.target.value)
                        update({ name: renamed.name, code: renamed.code })
                      }} className={inputClass} />
                    <InlineError id={`etapa-${i}-name`} message={stepField(i, 'name')} />
                  </div>
                ),
              },
              {
                label: 'Letra', width: '4.5rem', render: (row, i, update) => (
                  <div>
                    <input aria-label={`Letra da etapa ${i + 1}`} aria-invalid={!!stepField(i, 'code')} value={row.code} maxLength={2} autoCapitalize="characters"
                      onChange={(e) => update({ code: normalizeLetter(e.target.value), codeTouched: true })} className={`${inputClass} text-center font-display font-extrabold uppercase`} />
                    <InlineError id={`etapa-${i}-code`} message={stepField(i, 'code')} />
                  </div>
                ),
              },
              {
                label: 'Papel', width: '10rem', render: (row, i, update) => (
                  <div>
                    <select aria-label={`Papel da etapa ${i + 1}`} aria-invalid={!!stepField(i, 'role')} value={row.role} onChange={(e) => update({ role: e.target.value as StageRoleCode })} className={selectClass}>
                      {ROLES.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}
                    </select>
                    <InlineError id={`etapa-${i}-role`} message={stepField(i, 'role')} />
                  </div>
                ),
              },
              {
                label: 'Horas por unidade', width: '7rem', render: (row, i, update) => (
                  <div>
                    <DecimalInput scale={2} aria-label={`Horas por unidade da etapa ${i + 1}`} aria-invalid={!!stepField(i, 'standardHoursPerUnit')} value={row.hours} onChange={(v) => update({ hours: v })} suffix="h" />
                    <InlineError id={`etapa-${i}-hours`} message={stepField(i, 'standardHoursPerUnit')} />
                  </div>
                ),
              },
              {
                label: 'Cargo sugerido', width: 'minmax(0, 1.5fr)', render: (row, i, update) => (
                  <div>
                    <Combobox aria-label={`Cargo sugerido da etapa ${i + 1}`} aria-invalid={!!stepField(i, 'positionId')} queryKey={['stage-positions', positions.length]} search={positionOptions}
                      placeholder="Sem cargo" selected={row.positionId ? { id: row.positionId, label: row.positionName ?? 'Cargo' } : null}
                      onChange={(o) => update({ positionId: o?.id ?? null, positionName: o?.label ?? null, positionInactive: false })} />
                    {row.positionInactive ? (
                      <p className="mt-1 text-xs text-warn-ink"><Tag>Cargo inativo</Tag> Escolha outro cargo para os próximos projetos.</p>
                    ) : null}
                    <InlineError id={`etapa-${i}-position`} message={stepField(i, 'positionId')} />
                  </div>
                ),
              },
              {
                label: 'Regras', width: '8.5rem', render: (row, i, update) => (
                  <div className="space-y-1 text-sm">
                    <label className="flex items-center gap-2"><input type="checkbox" className={box} checked={row.requiresApproval} onChange={(e) => update({ requiresApproval: e.target.checked })} aria-label={`Exige aprovação para concluir (etapa ${i + 1})`} />Exige aprovação</label>
                    {canEdit ? (
                      <button type="button" onClick={() => setChecklistOf(i)} className="w-full rounded-md border border-border px-2 py-1.5 text-sm font-semibold" aria-label={`Checklist da etapa ${i + 1} (${row.checklist.length} itens)`}>
                        Checklist <span className="tabular-nums">({row.checklist.length})</span>
                      </button>
                    ) : <span className="text-muted-foreground tabular-nums">{row.checklist.length} itens no checklist</span>}
                    <InlineError id={`etapa-${i}-checklist`} message={Object.entries(fields).find(([k]) => k.startsWith(`steps[${i}].checklist`))?.[1]} />
                  </div>
                ),
              },
            ]} />
        </section>

        <section aria-labelledby="marcos-titulo" className="space-y-2">
          <div className="flex flex-wrap items-center gap-x-2">
            <h2 id="marcos-titulo" className="text-base font-semibold">Marcos sugeridos</h2>
            <Dica nome="Marcos sugeridos" texto={TEMPLATE_DICAS.marcos} />
          </div>
          <RowsEditor noun="marco" rows={draft.milestones} onChange={setMilestones} max={MAX_MILESTONES} disabled={!canEdit}
            newRow={() => newMilestone('Signature', Math.max(0, 100 - sum).toFixed(2))}
            summary={<span className={sum === 100 || draft.milestones.length === 0 ? '' : 'font-semibold text-warn-ink'}>Soma: {sum.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}%</span>}
            columns={[
              {
                label: 'Percentual', width: '7rem', render: (row, i, update) => (
                  <div>
                    <DecimalInput scale={2} aria-label={`Percentual do marco ${i + 1}`} aria-invalid={!!milestoneField(i, 'percent')} value={row.percent} onChange={(v) => update({ percent: v })} suffix="%" />
                    <InlineError id={`marco-${i}-percent`} message={milestoneField(i, 'percent')} />
                  </div>
                ),
              },
              {
                label: 'Gatilho', width: 'minmax(0, 1.5fr)', render: (row, i, update) => (
                  <div>
                    <select aria-label={`Gatilho do marco ${i + 1}`} aria-invalid={!!milestoneField(i, 'trigger')} value={row.trigger} onChange={(e) => update({ trigger: e.target.value as MilestoneTriggerCode })} className={selectClass}>
                      {TRIGGERS.map((t) => <option key={t} value={t}>{TRIGGER_LABEL[t]}</option>)}
                    </select>
                    <InlineError id={`marco-${i}-trigger`} message={milestoneField(i, 'trigger')} />
                  </div>
                ),
              },
              {
                label: 'Etapa', width: '7rem', render: (row, i, update) => (row.trigger === 'StageCompleted' ? (
                  <div>
                    <select aria-label={`Etapa do marco ${i + 1}`} aria-invalid={!!milestoneField(i, 'stageCode')} value={row.stageCode} onChange={(e) => update({ stageCode: e.target.value })} className={selectClass}>
                      <option value="">Escolher</option>
                      {codes.map((c) => <option key={c} value={c}>{c}</option>)}
                    </select>
                    <InlineError id={`marco-${i}-stage`} message={milestoneField(i, 'stageCode')} />
                  </div>
                ) : <span className="text-sm text-muted-foreground">—</span>),
              },
              {
                label: 'Prazo (dias)', width: '6rem', render: (row, i, update) => (
                  <div>
                    <input type="number" inputMode="numeric" min={0} max={365} aria-label={`Prazo em dias do marco ${i + 1}`} aria-invalid={!!milestoneField(i, 'daysAfterEvent')} value={row.days}
                      onChange={(e) => update({ days: e.target.value })} className={`${inputClass} tabular-nums`} />
                    <InlineError id={`marco-${i}-days`} message={milestoneField(i, 'daysAfterEvent')} />
                  </div>
                ),
              },
            ]} />
          {draft.milestones.length === 0 ? <p className="text-sm text-muted-foreground">Sem marcos sugeridos. O orçamento começa sem parcelas sugeridas por este modelo.</p> : null}
        </section>
      </fieldset>

      <div className="sticky bottom-20 z-10 flex flex-wrap items-center justify-between gap-3 rounded-md border border-border bg-card px-4 py-2 shadow-sm">
        <p aria-live="polite" className="text-sm tabular-nums">
          {draft.steps.length} {draft.steps.length === 1 ? 'etapa' : 'etapas'} · Horas por unidade: <b>{totalHours(draft.steps)} h</b>
        </p>
        <div className="flex flex-wrap gap-2">
          {footerExtra}
          {canEdit ? <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar modelo</button> : null}
        </div>
      </div>

      {confirmEdit ? (
        <ConfirmDialog open={confirming} onOpenChange={setConfirming} verb="Salvar modelo" done="Modelo salvo" title="Salvar as mudanças do modelo?"
          description={usageCount > 0 ? `${usageText(usageCount)}. Mudanças valem só para os próximos.` : 'Os projetos que já copiaram as etapas não mudam. Só os próximos usam o modelo novo.'}
          onConfirm={() => onSubmit(draft).then(done)}
          onFieldErrors={(e) => { showServer(e); setConfirming(false); return true }}
          onReload={onReload} />
      ) : null}

      {checklistOf !== null && draft.steps[checklistOf] ? (
        <ChecklistPanel key={checklistOf} index={checklistOf} stepName={draft.steps[checklistOf].name} items={draft.steps[checklistOf].checklist} disabled={!canEdit} maxText={MAX_CHECKLIST_TEXT}
          description="O que a equipe confere antes de concluir a etapa."
          onApply={(items) => setDraft({ ...draft, steps: draft.steps.map((s, i) => (i === checklistOf ? { ...s, checklist: items } : s)) })}
          onClose={() => setChecklistOf(null)} />
      ) : null}
    </form>
  )
}
