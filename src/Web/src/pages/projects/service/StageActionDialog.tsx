import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import {
  approveProjectStage, completeProjectStage, getGetProjectQueryKey, getGetProjectStagesQueryKey, getGetProjectTimelineQueryKey, startProjectStage, uploadAttachment,
  useGetProjectMilestonePreview,
} from '@/api/generated/bemaqui'
import type { ProjectStatus, ProjectStagesView, ProjectStageView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'
import { today as todayIso } from '../../finance/receivables/receivables'
import { needsBackdateReason, REASON_MAX } from './service'
import { ParcelEffect } from './ServiceStepDialog'
import { STAGE_VERBS, type StageAction, stageGuarded } from './stages'

const NAME_MAX = 120
const ROLE_MAX = 80

/**
 * Diálogo da etapa (EXE-03b): iniciar (com o projeto planejado, a data do início), concluir (data, checklist completo, motivo da data anterior na
 * Entrega, no Aceite e com marco, quem aceitou no Aceite) e aprovar. Entrega e Aceite mostram o bloco de efeito nas parcelas pela prévia do
 * servidor (EXE-01b). O servidor decide tudo (versão, situação esperada, permissão e Idempotency-Key).
 */
export function StageActionDialog({ view, stage, action, projectStatus, open, onOpenChange, onDone }: {
  view: ProjectStagesView
  stage: ProjectStageView
  action: StageAction
  projectStatus: ProjectStatus
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone?: () => Promise<unknown> | void
}) {
  const client = useQueryClient()
  const today = todayIso()
  const [date, setDate] = useState(today)
  const [reason, setReason] = useState('')
  const [name, setName] = useState('')
  const [role, setRole] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [tried, setTried] = useState(false)
  const idempotency = useIdempotencyKey()
  const guarded = stageGuarded(stage)
  // Na aprovação, a data é a da conclusão.
  const factDate = action === 'approve' ? stage.completedOn ?? today : date
  const askDate = action === 'complete' || (action === 'start' && projectStatus === 'Planned')
  const askReason = needsBackdateReason(factDate, today) && (action === 'start' || guarded)
  const askName = stage.role === 'Acceptance' && action !== 'start'
  const previewStep = action === 'start' ? null : stage.role === 'Delivery' ? 'delivery' : stage.role === 'Acceptance' ? 'acceptance' : null
  const preview = useGetProjectMilestonePreview(view.projectId, { step: previewStep ?? 'start', date: factDate || today }, { query: { enabled: open && previewStep !== null } })
  const { verb, done, title } = STAGE_VERBS[action]
  const checklistOpen = action === 'complete' && stage.checklistDone < stage.checklistTotal

  const reset = () => {
    setDate(today)
    setReason('')
    setName('')
    setRole('')
    setFile(null)
    setErrors({})
    setTried(false)
  }

  const localErrors: Record<string, string> = {}
  if (askDate) {
    if (!date) localErrors.date = 'Informe a data.'
    else if (date > today) localErrors.date = 'A data não pode ser depois de hoje.'
  }
  if (askReason && !reason.trim()) localErrors.backdateReason = 'Informe por que a data é anterior.'
  if (askName && !name.trim()) localErrors.acceptedByName = 'Informe quem aceitou.'
  const shown = { ...(tried ? localErrors : { ...(localErrors.date ? { date: localErrors.date } : {}) }), ...errors }

  const onFieldErrors = (e: ApiError) => {
    const next: Record<string, string> = {}
    for (const [key, messages] of Object.entries(e.fields)) next[key] = messages[0] ?? e.message
    setErrors(next)
    return true
  }

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getGetProjectStagesQueryKey(view.projectId) })
    await client.invalidateQueries({ queryKey: getGetProjectQueryKey(view.projectId) })
    await client.invalidateQueries({ queryKey: getGetProjectTimelineQueryKey(view.projectId) })
  }

  const confirm = async () => {
    setTried(true)
    if (Object.keys(localErrors).length > 0) throw new ApiError(422, { title: 'Revise os campos destacados.', errors: Object.fromEntries(Object.entries(localErrors).map(([k, v]) => [k, [v]])) })
    const backdateReason = askReason ? reason.trim() : null
    const acceptance = askName
      ? { acceptedByName: name.trim(), acceptedByRole: role.trim() || null, attachmentId: file ? (await uploadAttachment('ProjectAcceptance', view.projectId, { file })).id : null }
      : {}
    const options = idempotency.options
    const base = { version: view.version, expectedStatus: projectStatus }
    switch (action) {
      case 'start':
        await startProjectStage(view.projectId, stage.id, { ...base, date: projectStatus === 'Planned' ? date : null, backdateReason }, options)
        break
      case 'complete':
        await completeProjectStage(view.projectId, stage.id, { ...base, date, backdateReason, ...acceptance }, options)
        break
      case 'approve':
        await approveProjectStage(view.projectId, stage.id, { ...base, backdateReason, ...acceptance }, options)
        break
    }
    idempotency.renew()
    await refresh()
    await onDone?.()
  }

  const impact = previewStep === null
    ? stage.hasMilestone && action !== 'start' ? <p>Esta etapa libera parcela do contrato.</p> : undefined
    : preview.isPending ? <Skeleton className="h-12" /> : preview.isError
      ? <p>Não foi possível calcular o efeito nas parcelas. <button type="button" onClick={() => void preview.refetch()} className="font-semibold text-primary underline">Tentar de novo</button></p>
      : <ParcelEffect preview={preview.data} step={previewStep} />

  const description = action === 'start'
    ? projectStatus === 'Planned' ? `${stage.code} ${stage.name}. O projeto passa a Em execução.` : `${stage.code} ${stage.name}.`
    : action === 'complete' && stage.requiresApproval
      ? `${stage.code} ${stage.name}. A etapa exige aprovação: fica aguardando quem aprova, salvo se a regra de autoaprovação deixar você aprovar.`
      : action === 'approve' && stage.completedOn
        ? `${stage.code} ${stage.name}, concluída em ${formatDate(stage.completedOn)}${stage.completedByName ? ` por ${stage.completedByName}` : ''}.`
        : `${stage.code} ${stage.name}.`

  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} onOpen={reset} verb={verb} done={done} title={title} description={description}
      impact={impact} onConfirm={confirm} onFieldErrors={onFieldErrors} onReload={refresh}
      preconditions={checklistOpen ? [{ ok: false, text: 'Marque todos os itens do checklist (Feito ou Não se aplica) antes de concluir.' }] : []}>
      {askDate ? (
        <Field label={action === 'start' ? 'Data do início' : 'Data da conclusão'} required error={shown.date}>
          {(f) => <DateInput {...f} value={date} onChange={(v) => { setDate(v); setErrors({}) }} className={inputClass} />}
        </Field>
      ) : null}
      {askReason ? (
        <Field label="Por que a data é anterior?" required error={shown.backdateReason}>
          {(f) => (
            <>
              <textarea {...f} value={reason} maxLength={REASON_MAX} onChange={(e) => setReason(e.target.value)} rows={2} className={inputClass} />
              <span className="block text-right text-xs tabular-nums text-muted-foreground">{reason.length}/{REASON_MAX}</span>
            </>
          )}
        </Field>
      ) : null}
      {askName ? (
        <>
          <Field label="Quem aceitou" required error={shown.acceptedByName}>
            {(f) => <input {...f} value={name} maxLength={NAME_MAX} onChange={(e) => setName(e.target.value)} className={inputClass} autoComplete="off" />}
          </Field>
          <Field label="Papel ou cargo" error={shown.acceptedByRole}>
            {(f) => <input {...f} value={role} maxLength={ROLE_MAX} onChange={(e) => setRole(e.target.value)} className={inputClass} autoComplete="off" />}
          </Field>
          <div className="space-y-1 text-sm">
            <label htmlFor={`termo-etapa-${stage.id}`} className="font-medium">Termo assinado (opcional)</label>
            <input id={`termo-etapa-${stage.id}`} type="file" accept=".pdf,.png,.jpg,.jpeg" onChange={(e) => setFile(e.target.files?.[0] ?? null)} className="block w-full text-sm" />
            {shown.attachmentId ? <p role="alert" className="text-xs text-crit-ink">{shown.attachmentId}</p> : null}
          </div>
        </>
      ) : null}
    </ConfirmDialog>
  )
}
