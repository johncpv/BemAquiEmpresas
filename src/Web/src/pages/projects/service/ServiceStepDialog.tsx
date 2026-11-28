import { Link } from '@tanstack/react-router'
import { useRef, useState } from 'react'
import { registerProjectAcceptance, registerProjectDelivery, revertProjectAcceptance, startProject, uploadAttachment, useGetProjectMilestonePreview } from '@/api/generated/bemaqui'
import type { MilestonePreview, ProjectDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { today as todayIso } from '../../finance/receivables/receivables'
import { dateError, needsBackdateReason, parcelLine, REASON_MAX, SERVICE_DICAS, type ServiceStep, STEP_VERBS } from './service'

const NAME_MAX = 120
const ROLE_MAX = 80

/**
 * Bloco "efeito nas parcelas" (EXE-01b CA 7 e 18), pela prévia do servidor: uma parcela por linha (empilhadas a 400 px, valor em tabular-nums)
 * e o total no fim; sem parcela, a frase fixa; parcela de aceite que continua aguardando, a frase própria.
 */
export function ParcelEffect({ preview, step }: { preview: MilestonePreview; step: ServiceStep }) {
  const lines = preview.lines ?? []
  const waiting = preview.stillWaiting ?? []
  return (
    <div className="space-y-2" data-testid="efeito-parcelas">
      {step === 'revert' ? (
        lines.length === 0
          ? <p>Nenhuma parcela volta a aguardar.</p>
          : lines.map((l) => <p key={l.sequence}>A parcela {l.compatibility ? l.label : l.description} volta a aguardar o aceite.</p>)
      ) : lines.length === 0 ? <p>Nenhuma parcela é liberada por este passo.</p> : (
        <>
          <p>Ao confirmar, estas parcelas passam a valer para cobrança:</p>
          <ul className="space-y-1" aria-label="Parcelas liberadas">
            {lines.map((l) => <li key={`${l.sequence}-${l.description}`} className="tabular-nums">{parcelLine(l)}</li>)}
          </ul>
          {typeof preview.total === 'number' ? <p className="font-semibold tabular-nums">Total liberado: <Money value={preview.total} /></p> : null}
        </>
      )}
      {waiting.map((w) => <p key={w}>A parcela {w} continua aguardando o aceite do cliente.</p>)}
    </div>
  )
}

function Counter({ value }: { value: string }) {
  return <span className="block text-right text-xs tabular-nums text-muted-foreground">{value.length}/{REASON_MAX}</span>
}

/**
 * Diálogos do projeto de serviço (EXE-01b CA 7 a 11): iniciar, entrega, aceite e desfazer, com o bloco de efeito nas parcelas, o motivo da data
 * anterior a hoje, a caixa "também aceitou" sempre desmarcada, o termo assinado e o desfazer travado quando a parcela já foi cobrada.
 * Aberto de fora (open/onOpenChange); o servidor decide tudo (versão, situação esperada e Idempotency-Key).
 */
export function ServiceStepDialog({ project, step, open, onOpenChange, onDone, onReload }: {
  project: ProjectDetail
  step: ServiceStep
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone: () => Promise<void>
  onReload: () => Promise<unknown>
}) {
  const today = todayIso()
  const [date, setDate] = useState(today)
  const [reason, setReason] = useState('')
  const [alsoAccept, setAlsoAccept] = useState(false)
  const [name, setName] = useState('')
  const [role, setRole] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [tried, setTried] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  const idempotency = useIdempotencyKey()
  const canAccept = useCan('projects.register_acceptance')
  const preview = useGetProjectMilestonePreview(project.id, { step, date: step === 'revert' ? undefined : date || today }, { query: { enabled: open } })
  const { verb, done, title: baseTitle } = STEP_VERBS[step]
  const acceptance = project.acceptance
  const title = step === 'revert' && acceptance
    ? `Desfazer o aceite de ${acceptance.acceptedByName ?? 'cliente'}, registrado em ${formatDate(acceptance.occurredOn)}?`
    : baseTitle
  const backdated = step !== 'revert' && needsBackdateReason(date, today)
  const showAlso = step === 'delivery' && canAccept && preview.data?.hasAcceptanceMilestone === false
  const needsName = step === 'acceptance' || (step === 'delivery' && alsoAccept)
  const locked = step === 'revert' ? preview.data?.lockedReason ?? null : null

  const reset = () => {
    setDate(today)
    setReason('')
    setAlsoAccept(false)
    setName('')
    setRole('')
    setFile(null)
    setErrors({})
    setTried(false)
  }

  const localErrors: Record<string, string> = {}
  if (step !== 'revert') {
    const de = dateError(date, today, step, project)
    if (de) localErrors.date = de
    if (backdated && !reason.trim()) localErrors.backdateReason = 'Informe por que a data é anterior.'
  }
  if (needsName && !name.trim()) localErrors.acceptedByName = 'Informe quem aceitou.'
  if (step === 'revert' && !reason.trim()) localErrors.reason = 'Informe o motivo.'
  const shown = { ...(tried ? localErrors : { ...(localErrors.date ? { date: localErrors.date } : {}) }), ...errors }

  const onFieldErrors = (e: ApiError) => {
    const next: Record<string, string> = {}
    for (const [key, messages] of Object.entries(e.fields)) next[key] = messages[0] ?? e.message
    setErrors(next)
    return true
  }

  const confirm = async () => {
    setTried(true)
    if (Object.keys(localErrors).length > 0) throw new ApiError(422, { title: 'Revise os campos destacados.', errors: Object.fromEntries(Object.entries(localErrors).map(([k, v]) => [k, [v]])) })
    const base = { version: project.version, expectedStatus: project.status }
    const backdateReason = backdated ? reason.trim() : null
    const options = idempotency.options
    switch (step) {
      case 'start':
        await startProject(project.id, { ...base, date, backdateReason }, options)
        break
      case 'delivery':
        await registerProjectDelivery(project.id, { ...base, date, backdateReason, alsoAccept, acceptedByName: alsoAccept ? name.trim() : null, acceptedByRole: alsoAccept && role.trim() ? role.trim() : null }, options)
        break
      case 'acceptance': {
        const attachment = file ? await uploadAttachment('ProjectAcceptance', project.id, { file }) : null
        await registerProjectAcceptance(project.id, { ...base, date, backdateReason, acceptedByName: name.trim(), acceptedByRole: role.trim() || null, attachmentId: attachment?.id ?? null }, options)
        break
      }
      case 'revert':
        await revertProjectAcceptance(project.id, { ...base, reason: reason.trim() }, options)
        break
    }
    idempotency.renew()
    await onDone()
  }

  const impact = preview.isPending ? <Skeleton className="h-12" /> : preview.isError
    ? <p>Não foi possível calcular o efeito nas parcelas. <button type="button" onClick={() => void preview.refetch()} className="font-semibold text-primary underline">Tentar de novo</button></p>
    : <ParcelEffect preview={preview.data} step={step} />

  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} onOpen={reset} verb={verb} done={done} title={title}
      description={step === 'delivery' && project.status === 'Planned' ? 'O projeto passa direto por Em execução.' : undefined}
      impact={impact} onConfirm={confirm} onFieldErrors={onFieldErrors} onReload={onReload}
      preconditions={locked ? [{ ok: false, text: locked }] : []}>
      {step !== 'revert' ? (
        <>
          <Field label={step === 'start' ? 'Data do início' : step === 'delivery' ? 'Data da entrega' : 'Data do aceite'} required error={shown.date}>
            {(f) => <DateInput {...f} value={date} onChange={(v) => { setDate(v); setErrors({}) }} className={inputClass} />}
          </Field>
          {backdated ? (
            <Field label="Por que a data é anterior?" required error={shown.backdateReason}>
              {(f) => <><textarea {...f} value={reason} maxLength={REASON_MAX} onChange={(e) => setReason(e.target.value)} rows={2} className={inputClass} /><Counter value={reason} /></>}
            </Field>
          ) : null}
        </>
      ) : (
        <Field label="Motivo" required error={shown.reason}>
          {(f) => <><textarea {...f} value={reason} maxLength={REASON_MAX} onChange={(e) => setReason(e.target.value)} rows={2} className={inputClass} /><Counter value={reason} /></>}
        </Field>
      )}
      {showAlso ? (
        <label className="flex items-start gap-2 text-sm">
          <input type="checkbox" checked={alsoAccept} onChange={(e) => setAlsoAccept(e.target.checked)} className="mt-1 size-4" />
          <span>Também registrar que o cliente aceitou (não há parcela ligada ao aceite)</span>
        </label>
      ) : null}
      {needsName ? (
        <>
          <Field label="Quem aceitou" required error={shown.acceptedByName}>
            {(f) => <input {...f} value={name} maxLength={NAME_MAX} onChange={(e) => setName(e.target.value)} className={inputClass} autoComplete="off" />}
          </Field>
          <Field label="Papel ou cargo" error={shown.acceptedByRole}>
            {(f) => <input {...f} value={role} maxLength={ROLE_MAX} onChange={(e) => setRole(e.target.value)} className={inputClass} autoComplete="off" />}
          </Field>
        </>
      ) : null}
      {step === 'acceptance' ? (
        <div className="space-y-1 text-sm">
          <span className="inline-flex items-center font-medium">Termo assinado<Dica nome="Aceite do cliente" texto={SERVICE_DICAS.aceite} /></span>
          <div className="flex flex-wrap items-center gap-2">
            <input ref={fileInput} id={`termo-${project.id}`} type="file" accept=".pdf,.png,.jpg,.jpeg" className="sr-only" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
            <label htmlFor={`termo-${project.id}`} className="cursor-pointer rounded-md border border-border px-3 py-2 font-semibold">{file ? 'Trocar arquivo' : 'Escolher arquivo'}</label>
            <span className="min-w-0 truncate text-muted-foreground">{file ? file.name : 'PDF, PNG ou JPG até 25 MB (opcional)'}</span>
            {file ? <button type="button" onClick={() => { setFile(null); if (fileInput.current) fileInput.current.value = '' }} className="text-xs font-semibold text-primary">Remover</button> : null}
          </div>
          {shown.attachmentId ? <p role="alert" className="text-xs text-crit-ink">{shown.attachmentId}</p> : null}
        </div>
      ) : null}
      {locked ? <p className="text-sm"><Link to={'/app/contratos/$id/aditivos/$aditivoId' as never} params={{ id: project.contractId, aditivoId: 'novo' } as never} className="font-semibold text-primary hover:underline">Fazer aditivo</Link></p> : null}
    </ConfirmDialog>
  )
}
