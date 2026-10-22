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
import { InlineError } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { ChecklistPanel } from './ChecklistPanel'
import { type CenterOption, type CenterSuggestion, draftProblems, MAX_STEPS, newStep, normalizeLetter, renameStep, ROUTING_DICAS, type RoutingDraft, type StepDraft, suggestCenter, totalHours } from './routings'

interface RoutingEditorProps {
  initial: RoutingDraft
  centers: readonly CenterOption[]
  suggestions: readonly CenterSuggestion[]
  productTypes: readonly string[]
  canEdit: boolean
  /** Edição de roteiro existente: confirma avisando que as OS já criadas não mudam (CA 12). */
  confirmEdit?: boolean
  /** Grava; pode devolver o que fazer depois (ex.: navegar), chamado já sem a guarda de alteração não salva. */
  onSubmit: (draft: RoutingDraft) => Promise<(() => void) | void>
  /** 409 concurrency_conflict: "Recarregar" (CA 17). */
  onReload?: () => Promise<unknown>
  /** Ações extras no rodapé fixo (ex.: "Cancelar"). */
  footerExtra?: ReactNode
  /** Só as etapas, sem nome e tipo (etapas da OS de fabricação, PRD-01 CA 7), com o verbo do salvar. */
  stepsOnly?: { saveLabel: string }
}

const box = 'size-4 accent-primary'

/**
 * Editor do roteiro (ENG-01 CA 2 a 6): nome, tipo de produto com sugestões e as etapas no RowsEditor (a 400 px cada etapa
 * vira cartão). Letra sugerida pela inicial, centro sugerido com a tag "Sugerido", "Começa sem BOM" na primeira etapa, total
 * de horas ao vivo no rodapé fixo e os erros do servidor no campo da linha (steps[n].campo).
 */
export function RoutingEditor({ initial, centers, suggestions, productTypes, canEdit, confirmEdit, onSubmit, onReload, footerExtra, stepsOnly }: RoutingEditorProps) {
  const [draft, setDraft] = useState<RoutingDraft>(initial)
  const [baseline, setBaseline] = useState<RoutingDraft>(initial)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<string | undefined>()
  const [checklistOf, setChecklistOf] = useState<number | null>(null)
  const [confirming, setConfirming] = useState(false)
  const dirty = JSON.stringify(draft) !== JSON.stringify(baseline)
  const field = (name: string) => fields[name]
  const stepField = (i: number, name: string) => fields[`steps[${i}].${name}`]
  const setSteps = (steps: StepDraft[]) => {
    setDraft({ ...draft, steps })
    // A posição mudou: os erros por linha do último envio deixam de valer.
    setFields(Object.fromEntries(Object.entries(fields).filter(([k]) => !k.startsWith('steps'))))
  }

  const showServer = (e: unknown) => {
    if (e instanceof ApiError) {
      const flat = Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]]))
      setFields(flat)
      setMessage(Object.keys(flat).length === 0 || Object.keys(flat).every((k) => k === 'steps') ? e.message : undefined)
      if (Object.keys(flat).length > 0) focusFirstError()
    } else {
      setMessage(stepsOnly ? 'Não foi possível salvar as etapas. Tente de novo.' : 'Não foi possível salvar o roteiro. Tente de novo.')
    }
  }

  const focusFirstError = () => requestAnimationFrame(() => document.querySelector<HTMLElement>('#roteiro [aria-invalid="true"]')?.focus())

  const validate = () => {
    const problems = draftProblems(draft, !stepsOnly)
    setFields(problems)
    setMessage(problems.steps)
    if (Object.keys(problems).length > 0) focusFirstError()
    return Object.keys(problems).length === 0
  }

  /** Depois de gravar: sem erros e sem a guarda (flushSync), e só então o próximo passo do chamador. */
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

  const centerOptions = (term: string) => Promise.resolve(centers
    .filter((c) => !term || c.name.toLocaleLowerCase('pt-BR').includes(term.toLocaleLowerCase('pt-BR')))
    .map((c) => ({ id: c.id, label: c.name, hint: c.line ?? undefined })))

  return (
    <form id="roteiro" noValidate onSubmit={(e) => { e.preventDefault(); void requestSave() }} className="space-y-5 pb-4">
      <UnsavedChangesGuard when={dirty && canEdit} onSave={requestSave} />
      <FormError message={message} />
      <fieldset disabled={!canEdit} className="space-y-5">
        {stepsOnly ? null : <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Nome" required error={field('name')} hint="Ex.: Cabine padrão, Porta de pavimento.">
            {(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
          </Field>
          <Field label="Tipo de produto" error={field('productType')} hint="Texto livre; as sugestões são os tipos já usados." dica="Agrupa os roteiros, como Elevador, Porta ou Cabine. A OS sugere o roteiro pelo produto vinculado.">
            {(p) => (
              <>
                <input {...p} list="tipos-de-produto" value={draft.productType} maxLength={60} onChange={(e) => setDraft({ ...draft, productType: e.target.value })} className={inputClass} />
                <datalist id="tipos-de-produto">{productTypes.map((t) => <option key={t} value={t} />)}</datalist>
              </>
            )}
          </Field>
        </div>}

        <section aria-labelledby="etapas-titulo" className="space-y-2">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
            <h2 id="etapas-titulo" className="text-base font-semibold">Etapas</h2>
            <p className="flex flex-wrap items-center gap-x-3 text-xs text-muted-foreground">
              <span className="inline-flex items-center">Letra<Dica nome="Letra" texto={ROUTING_DICAS.letra} /></span>
              <span className="inline-flex items-center">Horas padrão<Dica nome="Horas padrão" texto={ROUTING_DICAS.horas} /></span>
              <span className="inline-flex items-center">Etapa de qualidade<Dica nome="Etapa de qualidade" texto={ROUTING_DICAS.qualidade} /></span>
              <span className="inline-flex items-center">Exige aprovação<Dica nome="Exige aprovação" texto={ROUTING_DICAS.aprovacao} /></span>
            </p>
          </div>
          <RowsEditor noun="etapa" rows={draft.steps} onChange={setSteps} min={1} max={MAX_STEPS} reorder draggable disabled={!canEdit}
            newRow={() => newStep('', suggestions, centers)}
            columns={[
              {
                label: 'Nome', width: 'minmax(0, 2fr)', render: (row, i, update) => (
                  <div>
                    <input aria-label={`Nome da etapa ${i + 1}`} aria-invalid={!!stepField(i, 'name')} aria-describedby={stepField(i, 'name') ? `etapa-${i}-name` : undefined} value={row.name} maxLength={60}
                      onChange={(e) => {
                        const renamed = renameStep(row, e.target.value)
                        update({ name: renamed.name, code: renamed.code })
                      }}
                      onBlur={() => {
                        if (row.workCenterId || !row.name.trim()) return
                        const center = suggestCenter(row.name, suggestions, centers)
                        if (center) update({ workCenterId: center.id, workCenterName: center.name, suggested: true })
                      }}
                      className={inputClass} />
                    {i === 0 ? <span className="mt-1 inline-flex items-center"><Tag>Começa sem BOM</Tag><Dica nome="Começa sem BOM" texto={ROUTING_DICAS.semBom} /></span> : null}
                    <InlineError id={`etapa-${i}-name`} message={stepField(i, 'name')} />
                  </div>
                ),
              },
              {
                label: 'Letra', width: '4.5rem', render: (row, i, update) => (
                  <div>
                    <input aria-label={`Letra da etapa ${i + 1}`} aria-invalid={!!stepField(i, 'code')} aria-describedby={stepField(i, 'code') ? `etapa-${i}-code` : undefined} value={row.code} maxLength={2} autoCapitalize="characters"
                      onChange={(e) => update({ code: normalizeLetter(e.target.value), codeTouched: true })}
                      className={`${inputClass} text-center font-display font-extrabold uppercase`} />
                    <InlineError id={`etapa-${i}-code`} message={stepField(i, 'code')} />
                  </div>
                ),
              },
              {
                label: 'Centro de trabalho', width: 'minmax(0, 2fr)', render: (row, i, update) => (
                  <div>
                    <Combobox aria-label={`Centro de trabalho da etapa ${i + 1}`} aria-required aria-invalid={!!stepField(i, 'workCenterId')} aria-describedby={stepField(i, 'workCenterId') ? `etapa-${i}-center` : undefined}
                      queryKey={['routing-work-centers', centers.length]} search={centerOptions} placeholder="Escolher centro"
                      selected={row.workCenterId ? { id: row.workCenterId, label: row.workCenterName ?? 'Centro' } : null}
                      onChange={(o) => update({ workCenterId: o?.id ?? null, workCenterName: o?.label ?? null, suggested: false })} />
                    {row.suggested && row.workCenterId ? <Tag className="mt-1">Sugerido</Tag> : null}
                    <InlineError id={`etapa-${i}-center`} message={stepField(i, 'workCenterId')} />
                  </div>
                ),
              },
              {
                label: 'Horas padrão', width: '7rem', render: (row, i, update) => (
                  <div>
                    <DecimalInput scale={2} aria-label={`Horas padrão da etapa ${i + 1}`} aria-invalid={!!stepField(i, 'standardHours')} value={row.standardHours} onChange={(v) => update({ standardHours: v })} suffix="h" />
                    <InlineError id={`etapa-${i}-hours`} message={stepField(i, 'standardHours')} />
                  </div>
                ),
              },
              {
                label: 'Regras', width: '10rem', render: (row, i, update) => (
                  <div className="space-y-1 text-sm">
                    <label className="flex items-center gap-2"><input type="checkbox" className={box} aria-invalid={!!stepField(i, 'isQualityStep')} checked={row.isQualityStep} onChange={(e) => update({ isQualityStep: e.target.checked })} aria-label={`Etapa de qualidade (etapa ${i + 1})`} />Qualidade</label>
                    <label className="flex items-center gap-2"><input type="checkbox" className={box} checked={row.requiresApproval} onChange={(e) => update({ requiresApproval: e.target.checked })} aria-label={`Exige aprovação para concluir (etapa ${i + 1})`} />Exige aprovação</label>
                    <InlineError id={`etapa-${i}-quality`} message={stepField(i, 'isQualityStep')} />
                  </div>
                ),
              },
              {
                label: 'Checklist', width: '8.5rem', render: (row, i) => (
                  <div>
                    {canEdit ? (
                      <button type="button" onClick={() => setChecklistOf(i)} className="w-full rounded-md border border-border px-2 py-2 text-sm font-semibold" aria-label={`Checklist da etapa ${i + 1} (${row.checklist.length} itens)`}>
                        Checklist <span className="tabular-nums">({row.checklist.length})</span>
                      </button>
                    ) : row.checklist.length === 0 ? <span className="text-sm text-muted-foreground">Sem checklist</span> : (
                      <ol aria-label={`Checklist da etapa ${i + 1}`} className="list-decimal pl-5 text-sm">{row.checklist.map((t, n) => <li key={n}>{t}</li>)}</ol>
                    )}
                    <InlineError id={`etapa-${i}-checklist`} message={Object.entries(fields).find(([k]) => k.startsWith(`steps[${i}].checklist`))?.[1]} />
                  </div>
                ),
              },
            ]} />
        </section>
      </fieldset>

      <div className="sticky bottom-20 z-10 flex flex-wrap items-center justify-between gap-3 rounded-md border border-border bg-card px-4 py-2 shadow-sm">
        <p aria-live="polite" className="text-sm tabular-nums">
          {draft.steps.length} {draft.steps.length === 1 ? 'etapa' : 'etapas'} · Total de horas padrão: <b>{totalHours(draft.steps)} h</b>
        </p>
        <div className="flex flex-wrap gap-2">
          {footerExtra}
          {canEdit ? <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{stepsOnly?.saveLabel ?? 'Salvar roteiro'}</button> : null}
        </div>
      </div>

      {confirmEdit ? (
        <ConfirmDialog open={confirming} onOpenChange={setConfirming} verb="Salvar roteiro" done="Roteiro salvo" title="Salvar as mudanças do roteiro?"
          description="As OS já criadas não mudam. Só as próximas usam o roteiro novo."
          onConfirm={() => onSubmit(draft).then(done)}
          onFieldErrors={(e) => { showServer(e); setConfirming(false); return true }}
          onReload={onReload} />
      ) : null}

      {checklistOf !== null && draft.steps[checklistOf] ? (
        <ChecklistPanel key={checklistOf} index={checklistOf} stepName={draft.steps[checklistOf].name} items={draft.steps[checklistOf].checklist} disabled={!canEdit}
          onApply={(items) => setDraft({ ...draft, steps: draft.steps.map((s, i) => (i === checklistOf ? { ...s, checklist: items } : s)) })}
          onClose={() => setChecklistOf(null)} />
      ) : null}
    </form>
  )

}
