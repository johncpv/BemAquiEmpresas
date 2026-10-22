import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { Camera, X } from 'lucide-react'
import { type ReactNode, useEffect, useRef, useState } from 'react'
import { approveQuality, getGetQualityStepQueryKey, markQualityChecklist, rejectQuality, startQualityInspection, uploadNonConformityAttachment, useGetQualityStep } from '@/api/generated/bemaqui'
import type { ChecklistResult, QualityDecisionResult, QualityStepDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { IDEMPOTENCY_HEADER } from '@/api/idempotency'
import { FloorButton, FloorConfirm, FloorDica, FloorSheet, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { cn } from '@/lib/utils'
import { useProductionLive } from '@/pages/production/kanban/useBoardLive'
import {
  canApprove, decisionNext, defaultReturn, draftDescription, MAX_ACTION, MAX_CAUSE, MAX_DESCRIPTION, missingQuality, missingText, nonConforming, QUALITY_DICAS, QUALITY_RESULTS, refreshQuality,
} from './quality'

const tap = () => ({ headers: { [IDEMPOTENCY_HEADER]: crypto.randomUUID() } })
const linkButton = 'inline-flex min-h-[56px] min-w-[56px] items-center justify-center rounded-md border-2 border-input bg-card px-4 text-[18px] font-semibold'
const field = 'w-full rounded-md border-2 border-input bg-card p-3 text-[18px]'


/**
 * Decisão de qualidade (PRD-05 CA 3 a 8), a mesma no /fab e no backoffice, com o kit chão de fábrica (alvos de 56 px): checklist
 * "Conforme / Não conforme / Não se aplica", "Iniciar inspeção", "Aprovar qualidade" com o impacto em folha de tela cheia e
 * "Reprovar qualidade" com a NC, a etapa de retorno sugerida, o responsável (padrão: eu) e as fotos. Depois, o próximo passo.
 */
export function QualityInspection({ stepId, queue }: { stepId: string; queue: '/fab/qualidade' | '/app/producao/qualidade' }) {
  const client = useQueryClient()
  const query = useGetQualityStep(stepId, { query: { retry: false } })
  useProductionLive(refreshQuality)
  const [decided, setDecided] = useState<QualityDecisionResult | null>(null)
  const back = <Link to={queue} className={linkButton}>Voltar à fila de qualidade</Link>

  if (decided) return <Decided result={decided} back={back} />
  if (query.isPending) return <FloorSkeleton rows={4} />
  if (query.isError) {
    const error = query.error instanceof ApiError ? query.error : null
    if (error?.status === 404) return <FloorState title="Esta OS não está mais aguardando qualidade." message="Outra pessoa pode ter decidido ou a OS mudou." action={back} />
    if (error?.status === 403) return <FloorState title="Você não decide a qualidade." message="Peça a quem administra os acessos a ação Aprovar ou reprovar a qualidade." />
    return <FloorState tone="error" title="Não foi possível carregar a etapa de qualidade." message="Verifique a conexão e tente de novo." action={<FloorButton onClick={() => void query.refetch()}>Tentar de novo</FloorButton>} />
  }

  const set = (next: QualityStepDetail) => client.setQueryData(getGetQualityStepQueryKey(stepId), next)
  return <Body key={`${stepId}-${query.data.step.attempt}`} detail={query.data} onChanged={set} onReload={() => void query.refetch()} onDecided={setDecided} />
}

function Body({ detail: d, onChanged, onReload, onDecided }: {
  detail: QualityStepDetail
  onChanged: (next: QualityStepDetail) => void
  onReload: () => void
  onDecided: (result: QualityDecisionResult) => void
}) {
  const [sheet, setSheet] = useState<'approve' | 'reject' | null>(null)
  const [busy, setBusy] = useState(false)
  const missing = missingQuality(d.checklist)
  const bad = nonConforming(d.checklist)
  const blocked = !!d.blockedReason
  const step = d.step

  const mark = async (itemId: string, result: ChecklistResult | null) => {
    // Marca na hora (luva no tablet); o servidor confirma com quem marcou.
    onChanged({ ...d, checklist: d.checklist.map((c) => (c.id === itemId ? { ...c, result } : c)) })
    try {
      onChanged(await markQualityChecklist(step.stepId, itemId, { result }))
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível marcar. Tente de novo.')
      onReload()
    }
  }

  const start = async () => {
    setBusy(true)
    try {
      onChanged(await startQualityInspection(step.stepId, tap()))
      notifySuccess('Inspeção iniciada')
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível iniciar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-5">
      <section aria-label="OS em inspeção" className="space-y-3 rounded-md border-2 border-border bg-card p-4">
        <StageIndicator stages={step.stepNames} letters={step.codes} current={step.stepIndex} showLabel />
        <p className="text-[20px] font-semibold">{d.productCode ? `${d.productCode} · ` : ''}{step.productName || 'Produto'}<span className="font-normal text-muted-foreground tabular-nums"> · Qtd. {d.quantity.toLocaleString('pt-BR')}</span></p>
        <p className="text-[18px] text-muted-foreground">{step.projectNumber} · {step.projectName}{step.partyName ? ` · ${step.partyName}` : ''}</p>
        <p className="flex flex-wrap items-center gap-2 text-[18px]">
          <span className="tabular-nums">Tentativa {step.attempt}</span>
          <FloorDica nome="Tentativa" texto={QUALITY_DICAS.tentativa} />
          {step.openNonConformities > 0 ? <StatusChip tone="crit">NC aberta</StatusChip> : null}
          {d.inspectionStarted ? <StatusChip tone="info">Inspeção em andamento</StatusChip> : null}
        </p>
        {step.selfInspection ? (
          <p role="note" className="flex items-center rounded-md border border-warn-soft bg-warn-soft p-3 text-[18px] text-warn-ink">
            <span className="flex-1">Você apontou uma etapa desta tentativa e não há outro aprovador: a aprovação leva a marca Autoinspeção e o Proprietário é avisado.</span>
            <FloorDica nome="Autoinspeção" texto={QUALITY_DICAS.autoinspecao} />
          </p>
        ) : null}
        {blocked ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft p-3 text-[18px] text-warn-ink">{d.blockedReason} Outra pessoa com a ação de qualidade aprova; você ainda pode reprovar.</p> : null}
      </section>

      <section aria-labelledby="checklist-qualidade" className="space-y-2">
        <h2 id="checklist-qualidade" className="text-[20px] font-bold">Checklist da qualidade</h2>
        {d.checklist.length === 0 ? <p className="text-[18px] text-muted-foreground">Esta etapa não tem checklist. Confira a peça e decida.</p> : (
          <ul className="space-y-[12px]">
            {d.checklist.map((item) => (
              <li key={item.id} className={cn('rounded-md border-2 bg-card p-3', item.result === 'NonConforming' ? 'border-crit' : item.result ? 'border-ok' : 'border-input')}>
                <p className="text-[18px] font-medium">{item.text}</p>
                {item.result && item.markedByName ? <p className="text-base text-muted-foreground">Marcado por {item.markedByName}</p> : null}
                <div role="group" aria-label={`Resultado: ${item.text}`} className="mt-2 flex flex-wrap gap-[8px]">
                  {QUALITY_RESULTS.map((r) => (
                    <FloorButton key={r.value} tone={item.result === r.value ? (r.value === 'NonConforming' ? 'danger' : 'primary') : 'secondary'} aria-pressed={item.result === r.value}
                      className="flex-1" onClick={() => void mark(item.id, item.result === r.value ? null : r.value)}>{r.label}</FloorButton>
                  ))}
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-label="Decisão" className="space-y-3">
        {d.canStartInspection ? <FloorButton tone="secondary" className="w-full" disabled={busy} onClick={() => void start()}>Iniciar inspeção</FloorButton> : null}
        <div className="flex flex-wrap gap-[16px]">
          <FloorButton tone="danger" className="flex-1" onClick={() => setSheet('reject')}>Reprovar qualidade</FloorButton>
          <FloorButton tone="ok" className="flex-1" disabled={!canApprove(d.checklist, blocked)} aria-describedby="decisao-falta" onClick={() => setSheet('approve')}>Aprovar qualidade</FloorButton>
        </div>
        <p id="decisao-falta" className="text-[18px] font-semibold text-warn-ink empty:hidden">
          {bad.length > 0 ? 'Há item não conforme: reprove para abrir a não conformidade.' : missing > 0 ? missingText(missing) : ''}
        </p>
      </section>

      <FloorConfirm open={sheet === 'approve'} onOpenChange={(o) => setSheet(o ? 'approve' : null)} tone="ok" verb="Aprovar qualidade" title={`Aprovar qualidade · ${step.orderNumber}`}
        impact={(
          <>
            <p>{step.stepName}: aguardando → aprovada</p>
            <p>{d.approvalImpact}</p>
            {step.selfInspection ? <p className="font-semibold text-warn-ink">Com a marca Autoinspeção; o Proprietário é avisado.</p> : null}
          </>
        )}
        onConfirm={async () => {
          const result = await approveQuality(step.stepId, { version: d.version }, tap())
          notifySuccess(result.message)
          onDecided(result)
        }} />
      {sheet === 'reject' ? <RejectSheet detail={d} onOpenChange={(o) => setSheet(o ? 'reject' : null)} onDecided={onDecided} /> : null}
    </div>
  )
}

/**
 * "Reprovar qualidade" (CA 6 e 8) em folha de tela cheia: descrição (rascunho pelos itens "Não conforme"), causa e ação opcionais,
 * responsável (padrão: quem reprova), etapa de retorno com "Sugerido" e o impacto, e "Tirar foto" (câmera do tablet). As fotos
 * vão para a NC logo depois que ela nasce.
 */
function RejectSheet({ detail: d, onOpenChange, onDecided }: { detail: QualityStepDetail; onOpenChange: (open: boolean) => void; onDecided: (result: QualityDecisionResult) => void }) {
  const [description, setDescription] = useState(() => draftDescription(d.checklist))
  const [cause, setCause] = useState('')
  const [action, setAction] = useState('')
  const [responsible, setResponsible] = useState(d.defaultResponsibleId ?? d.responsibles[0]?.id ?? '')
  const [returnTo, setReturnTo] = useState(() => defaultReturn(d.returnOptions))
  const [photos, setPhotos] = useState<File[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const input = useRef<HTMLInputElement>(null)
  const alertRef = useRef<HTMLDivElement>(null)
  useEffect(() => { if (error) alertRef.current?.focus() }, [error])
  const picked = d.returnOptions.find((o) => o.stepId === returnTo)
  const ready = description.trim().length > 0 && !!returnTo

  const confirm = async () => {
    setBusy(true)
    setError(null)
    try {
      const result = await rejectQuality(d.step.stepId, {
        description: description.trim(), cause: cause.trim() || null, action: action.trim() || null, responsibleUserId: responsible || null, returnToStepId: returnTo, version: d.version,
      }, tap())
      const failed: string[] = []
      for (const file of photos) {
        try {
          await uploadNonConformityAttachment(result.nonConformityId!, { file })
        } catch {
          failed.push(file.name)
        }
      }
      notifySuccess(result.message)
      if (failed.length > 0) notifyError(`A NC foi aberta, mas ${failed.length === 1 ? 'a foto' : 'as fotos'} ${failed.join(', ')} não ${failed.length === 1 ? 'foi enviada' : 'foram enviadas'}. Anexe de novo na NC.`)
      onOpenChange(false)
      onDecided(result)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível reprovar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <FloorSheet open role="alertdialog" onOpenChange={onOpenChange} title={`Reprovar qualidade · ${d.step.orderNumber}`}
      description="A não conformidade nasce agora e a OS volta para a etapa escolhida."
      actions={(
        <div className="space-y-3">
          {picked ? <p role="status" className="rounded-md border border-border bg-card p-3 text-[18px] font-semibold tabular-nums">{picked.impact}</p> : null}
          <div className="flex gap-[16px]">
            <FloorButton tone="secondary" className="flex-1" onClick={() => onOpenChange(false)} disabled={busy}>Voltar</FloorButton>
            <FloorButton tone="danger" className="flex-1" disabled={!ready || busy} onClick={() => void confirm()}>{busy ? 'Gravando…' : 'Reprovar qualidade'}</FloorButton>
          </div>
        </div>
      )}>
      <label className="block space-y-1">
        <span className="flex items-center font-semibold">Descrição da não conformidade (obrigatória)<FloorDica nome="Não conformidade" texto={QUALITY_DICAS.naoConformidade} /></span>
        <textarea rows={4} maxLength={MAX_DESCRIPTION} value={description} onChange={(e) => setDescription(e.target.value)} className={field} aria-required="true" />
        <span className="block text-right text-base text-muted-foreground tabular-nums">{description.length}/{MAX_DESCRIPTION}</span>
      </label>
      <fieldset className="space-y-[8px]">
        <legend className="mb-2 flex items-center font-semibold">Etapa de retorno<FloorDica nome="Etapa de retorno" texto={QUALITY_DICAS.etapaRetorno} /></legend>
        {d.returnOptions.length === 0 ? <p className="text-muted-foreground">Não há etapa anterior para refazer.</p> : d.returnOptions.map((o) => (
          <label key={o.stepId} data-floor-target="" className="flex min-h-[56px] cursor-pointer items-center gap-4 rounded-md border-2 border-input bg-card px-4 py-2 has-[:checked]:border-primary has-[:checked]:bg-primary-soft">
            <input type="radio" name="retorno" value={o.stepId} checked={returnTo === o.stepId} onChange={() => setReturnTo(o.stepId)} className="size-7 accent-primary" />
            <span className="flex-1">
              <span className="flex flex-wrap items-center gap-2 font-semibold">{o.code} · {o.name}{o.suggested ? <Tag>Sugerido</Tag> : null}</span>
              <span className="block text-base text-muted-foreground">{o.impact}</span>
            </span>
          </label>
        ))}
      </fieldset>
      <label className="block space-y-1">
        <span className="font-semibold">Responsável</span>
        <select value={responsible} onChange={(e) => setResponsible(e.target.value)} className={cn(field, 'min-h-[56px]')}>
          {d.responsibles.map((u) => <option key={u.id} value={u.id}>{u.name}{u.id === d.defaultResponsibleId ? ' (você)' : ''}</option>)}
        </select>
      </label>
      <label className="block space-y-1">
        <span className="font-semibold">Causa (opcional agora)</span>
        <textarea rows={2} maxLength={MAX_CAUSE} value={cause} onChange={(e) => setCause(e.target.value)} className={field} />
      </label>
      <label className="block space-y-1">
        <span className="font-semibold">Ação corretiva (opcional agora)</span>
        <textarea rows={2} maxLength={MAX_ACTION} value={action} onChange={(e) => setAction(e.target.value)} className={field} />
      </label>
      <div className="space-y-2">
        <input ref={input} id="foto-nc" type="file" accept="image/png,image/jpeg,application/pdf" capture="environment" multiple className="sr-only"
          onChange={(e) => { setPhotos([...photos, ...Array.from(e.target.files ?? [])]); e.target.value = '' }} />
        <label htmlFor="foto-nc" data-floor-target="" className={cn(linkButton, 'w-full cursor-pointer gap-2')}><Camera aria-hidden="true" className="size-6" />Tirar foto</label>
        {photos.length > 0 ? (
          <ul aria-label="Fotos para a NC" className="space-y-[8px]">
            {photos.map((p, i) => (
              <li key={`${p.name}-${i}`} className="flex items-center gap-[8px] rounded-md border border-border bg-card p-2">
                <span className="min-w-0 flex-1 truncate px-2">{p.name}</span>
                <FloorButton tone="secondary" aria-label={`Remover ${p.name}`} onClick={() => setPhotos(photos.filter((_, j) => j !== i))}><X aria-hidden="true" className="size-6" /></FloorButton>
              </li>
            ))}
          </ul>
        ) : <p className="text-base text-muted-foreground">PNG, JPG ou PDF até 10 MB. As fotos vão para a NC.</p>}
      </div>
      {error ? <div ref={alertRef} tabIndex={-1} role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] text-crit-ink outline-none">{error}</div> : null}
    </FloorSheet>
  )
}

/** Depois da decisão (CA 4, propositivo): a mensagem, o que acontece com a OS e o próximo passo. */
function Decided({ result, back }: { result: QualityDecisionResult; back: ReactNode }) {
  const navigate = useNavigate()
  const next = decisionNext(result)
  return (
    <div className="space-y-4">
      <FloorState title={result.message} message={next.text} />
      {result.selfInspection ? <p className="flex items-center justify-center gap-2 text-[18px]"><StatusChip tone="warn">Autoinspeção</StatusChip><FloorDica nome="Autoinspeção" texto={QUALITY_DICAS.autoinspecao} /></p> : null}
      <section aria-label="Próximos passos" className="flex flex-col gap-[8px]">
        {next.complete ? <FloorButton className="w-full" onClick={() => void navigate({ to: '/app/producao/os/$id', params: { id: result.orderId } })}>Concluir OS</FloorButton> : null}
        {next.openNc && result.nonConformityId ? (
          <FloorButton className="w-full" onClick={() => void navigate({ to: '/app/producao/nao-conformidades/$id', params: { id: result.nonConformityId! } })}>Abrir a {result.nonConformityNumber}</FloorButton>
        ) : null}
        {back}
      </section>
    </div>
  )
}