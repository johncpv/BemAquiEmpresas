import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import {
  finishShopfloorStep, getDownloadShopfloorAttachmentUrl, getGetShopfloorQueueQueryKey, getGetShopfloorStepQueryKey, leaveShopfloorStep, markShopfloorChecklist, pauseShopfloorStep, resumeShopfloorStep,
  startShopfloorStep, useGetShopfloorStep,
} from '@/api/generated/bemaqui'
import type { PauseReason, ShopfloorActionResult, ShopfloorAttachment, ShopfloorQueueStep, ShopfloorStepDetail } from '@/api/generated/model'
import { ApiError, apiBlobUrl, apiDownload } from '@/api/http'
import { IDEMPOTENCY_HEADER } from '@/api/idempotency'
import { FloorButton, FloorCheck, FloorConfirm, FloorDica, FloorHeader, FloorSheet, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { useHelp } from '@/help/help-context'
import { refreshShopfloor, useProductionLive } from '@/pages/production/kanban/useBoardLive'
import { cn } from '@/lib/utils'
import { NO_EMPLOYEE } from './FabQueuePage'
import {
  FAB_DICAS, finishImpact, floorHours, formatTimer, hoursFromSeconds, MAX_PAUSE_NOTES, missingCount, missingLabel, orderedReasons, pauseLabel, viewableKind, workedSeconds,
} from './shopfloor'

const tap = () => ({ headers: { [IDEMPOTENCY_HEADER]: crypto.randomUUID() } })
const time = (iso: string) => new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' })
const backLink = 'inline-flex min-h-[56px] min-w-[56px] items-center justify-center rounded-md border-2 border-input bg-card px-4 text-[18px] font-semibold'

/**
 * Etapa no app do operador (/fab/etapa/$stepId, CA 5 a 15): OS, produto, projeto e cliente, display de andar, horas padrão e
 * apontadas, quem está nela, cronômetro, checklist, anexos e os botões grandes. Toda ação passa pela folha de tela cheia quando
 * tem efeito (Finalizar, Sair, Concluir para todos, Pausar) e a mensagem sai na região do kit.
 */
export function FabStepPage() {
  const { stepId } = useParams({ strict: false }) as { stepId: string }
  const help = useHelp()
  const client = useQueryClient()
  const query = useGetShopfloorStep(stepId)
  useProductionLive(refreshShopfloor)
  const [finishedState, setFinished] = useState<{ stepId: string; result: ShopfloorActionResult; text: string } | null>(null)
  const finished = finishedState?.stepId === stepId ? finishedState : null

  useEffect(() => { document.title = 'Etapa · App do operador · BemAqui Empresas' }, [])

  const header = (title: string, subtitle?: string) => (
    <FloorHeader title={title} subtitle={subtitle} onHelp={() => help.setOpen(true)}>
      <Link to="/fab" className={backLink}>Fila</Link>
    </FloorHeader>
  )

  if (finished) return <>{header('Etapa finalizada')}<Finished result={finished.result} text={finished.text} /></>
  if (query.isPending) return <>{header('Etapa')}<div className="mx-auto max-w-3xl p-4"><FloorSkeleton rows={4} /></div></>
  if (query.isError) {
    const error = query.error instanceof ApiError ? query.error : null
    return (
      <>{header('Etapa')}<div className="mx-auto max-w-3xl p-4">
        {error?.code === 'employee_not_active' ? <FloorState title={NO_EMPLOYEE} />
          : error?.status === 404 ? <FloorState title="Esta etapa não está mais na sua fila." message="Ela pode ter sido concluída ou a OS mudou." action={<Link to="/fab" className={backLink}>Voltar à fila</Link>} />
            : <FloorState tone="error" title="Não foi possível carregar a etapa." message="Verifique a conexão e tente de novo." action={<FloorButton onClick={() => void query.refetch()}>Tentar de novo</FloorButton>} />}
      </div></>
    )
  }

  const d = query.data
  // Com a etapa devolvida pela ação, grava direto (sem nova leitura) e recarrega só a fila; sem ela, recarrega tudo do app.
  const refresh = async (next?: ShopfloorStepDetail | null) => {
    if (next) {
      client.setQueryData(getGetShopfloorStepQueryKey(stepId), next)
      await client.invalidateQueries({ queryKey: getGetShopfloorQueueQueryKey() })
    } else {
      refreshShopfloor(client)
    }
  }
  return (
    <>
      {header(`${d.step.orderNumber}, etapa ${d.step.stepName}`, d.step.workCenterName ?? undefined)}
      <StepBody key={d.step.stepId} detail={d} fetchedAt={query.dataUpdatedAt} onChanged={refresh} onFinished={(result, text) => setFinished({ stepId, result, text })} />
    </>
  )
}

function StepBody({ detail: d, fetchedAt, onChanged, onFinished }: {
  detail: ShopfloorStepDetail
  fetchedAt: number
  onChanged: (next?: ShopfloorStepDetail | null) => Promise<void>
  onFinished: (result: ShopfloorActionResult, text: string) => void
}) {
  const [now, setNow] = useState(() => Date.now())
  const [busy, setBusy] = useState(false)
  const [sheet, setSheet] = useState<'pause' | 'finish' | 'leave' | 'all' | null>(null)
  const mine = d.mine
  useEffect(() => {
    if (!mine) return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [mine])
  const offset = Date.parse(d.serverNow) - fetchedAt
  const seconds = mine ? workedSeconds(mine, now, offset) : 0
  const myHours = floorHours(hoursFromSeconds(seconds))
  const others = d.people.filter((p) => !p.isMe)
  const missing = missingCount(d)
  const paused = !!mine?.pauseStartedAt

  const act = async (action: () => Promise<ShopfloorActionResult | ShopfloorStepDetail>, done: string) => {
    setBusy(true)
    try {
      const result = await action()
      if (done) notifySuccess(done)
      await onChanged('step' in result && 'myHours' in result ? result.step : (result as ShopfloorStepDetail))
      return result
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível gravar. Tente de novo.', () => void onChanged())
      await onChanged()
      return null
    } finally {
      setBusy(false)
    }
  }

  const finish = async (forAll: boolean) => {
    const result = await finishShopfloorStep(d.step.stepId, { forAll }, tap())
    const text = forAll ? 'Etapa concluída' : `Etapa finalizada: ${floorHours(result.myHours)} h apontadas`
    notifySuccess(text)
    await onChanged(null)
    onFinished(result, text)
  }

  return (
    <div className="mx-auto max-w-3xl space-y-5 p-4">
      <section aria-label="Etapa" className="space-y-3 rounded-md border-2 border-border bg-card p-4">
        <StageIndicator stages={d.step.stepNames} letters={d.step.codes} current={d.step.stepIndex} showLabel />
        <p className="text-[20px] font-semibold">{d.productCode ? `${d.productCode} · ` : ''}{d.step.productName || 'Produto'}<span className="font-normal text-muted-foreground tabular-nums"> · Qtd. {d.quantity.toLocaleString('pt-BR')}</span></p>
        <p className="text-[18px] text-muted-foreground">{d.step.projectNumber} · {d.step.projectName}{d.step.partyName ? ` · ${d.step.partyName}` : ''}</p>
        <dl className="grid grid-cols-2 gap-3 text-[18px] tabular-nums sm:grid-cols-3">
          <div><dt className="flex items-center text-base text-muted-foreground">Horas padrão<FloorDica nome="Horas padrão" texto={FAB_DICAS.horasPadrao} /></dt><dd className="font-semibold">{floorHours(d.step.standardHours)} h</dd></div>
          <div><dt className="flex min-h-[56px] items-center text-base text-muted-foreground">Apontadas</dt><dd className={cn('font-semibold', d.step.appliedHours > d.step.standardHours && 'text-crit-ink')}>{floorHours(d.step.appliedHours)} h</dd></div>
          <div className="col-span-2 sm:col-span-1"><dt className="flex min-h-[56px] items-center text-base text-muted-foreground">Na etapa agora</dt><dd className="font-semibold">{d.people.length === 0 ? 'Ninguém' : d.people.map((p) => p.isMe ? 'Você' : p.name).join(', ')}</dd></div>
        </dl>
        {d.step.awaitingApproval ? <StatusChip tone="info">Aguardando aprovação</StatusChip> : null}
      </section>

      {mine ? (
        <section aria-label="Cronômetro" className="rounded-md border-2 border-primary bg-card p-4 text-center">
          <p className="font-display text-[44px] font-extrabold tabular-nums" aria-live="off">{formatTimer(seconds)}</p>
          <p className="text-[18px] text-muted-foreground tabular-nums">
            {paused ? `Pausada: ${pauseLabel(mine.pauseReason)}, desde ${time(mine.pauseStartedAt!)}.` : `Em andamento desde ${time(mine.startedAt)}.`} Padrão da etapa: {floorHours(d.step.standardHours)} h, apontadas {floorHours(d.step.appliedHours)} h.
          </p>
        </section>
      ) : null}

      <section aria-labelledby="checklist" className="space-y-2">
        <h2 id="checklist" className="text-[20px] font-bold">Checklist</h2>
        {d.checklist.length === 0 ? <p className="text-[18px] text-muted-foreground">Esta etapa não tem checklist.</p> : (
          <div className="space-y-[8px]">
            {d.checklist.map((item) => (
              <FloorCheck key={item.id} checked={item.done} label={item.text} disabled={!mine || busy}
                detail={item.done && item.markedByName ? `Marcado por ${item.markedByName}` : undefined}
                onChange={(done) => {
                  // Marca na hora (toque com luva precisa de resposta imediata); o servidor confirma e grava quem marcou.
                  void onChanged({ ...d, checklist: d.checklist.map((c) => (c.id === item.id ? { ...c, done } : c)) })
                  void act(() => markShopfloorChecklist(d.step.stepId, item.id, { done }), '')
                }} />
            ))}
          </div>
        )}
        {!mine && d.checklist.length > 0 ? <p className="text-base text-muted-foreground">Inicie a etapa para marcar o checklist.</p> : null}
      </section>

      <section aria-label="Ações" className="space-y-3">
        {!mine ? (
          d.canStart ? <FloorButton className="w-full" disabled={busy} onClick={() => void act(() => startShopfloorStep(d.step.stepId, tap()), 'Etapa iniciada')}>Iniciar etapa</FloorButton>
            : <p role="note" className="rounded-md border border-warn-soft bg-warn-soft p-4 text-[18px] text-warn-ink">{d.startBlockedReason ?? 'Esta etapa não pode ser iniciada agora.'}</p>
        ) : paused ? (
          <FloorButton tone="ok" className="w-full" disabled={busy} onClick={() => void act(() => resumeShopfloorStep(d.step.stepId, tap()), 'Etapa retomada')}>Retomar</FloorButton>
        ) : (
          <div className="flex flex-wrap gap-[16px]">
            <FloorButton tone="secondary" className="flex-1" disabled={busy} onClick={() => setSheet('pause')}>Pausar</FloorButton>
            {others.length === 0 ? (
              <FloorButton className="flex-1" disabled={busy || missing > 0} aria-describedby={missing > 0 ? 'faltam' : undefined} onClick={() => setSheet('finish')}>Finalizar etapa</FloorButton>
            ) : (
              <>
                <FloorButton tone="secondary" className="flex-1" disabled={busy} onClick={() => setSheet('leave')}>Sair da etapa (só eu)</FloorButton>
                <FloorButton className="flex-1" disabled={busy || missing > 0} aria-describedby={missing > 0 ? 'faltam' : undefined} onClick={() => setSheet('all')}>Concluir etapa para todos</FloorButton>
              </>
            )}
          </div>
        )}
        {mine && !paused && missing > 0 ? <p id="faltam" className="text-[18px] font-semibold text-warn-ink">{missingLabel(missing)}</p> : null}
        {mine && paused ? <p className="text-base text-muted-foreground">Para finalizar, retome a etapa.</p> : null}
        {mine && !paused ? <p className="flex items-center text-base text-muted-foreground">Pausa com motivo; a falta de material avisa o almoxarifado.<FloorDica nome="Pausa" texto={FAB_DICAS.pausa} /></p> : null}
      </section>

      <Attachments stepId={d.step.stepId} items={d.attachments} />

      {sheet === 'pause' ? (
        <PauseSheet onOpenChange={(o) => setSheet(o ? 'pause' : null)} suggested={d.suggestedPauseReason}
          onConfirm={async (reason, notes) => { await act(() => pauseShopfloorStep(d.step.stepId, { reason, notes }, tap()), 'Etapa pausada') }} />
      ) : null}
      <FloorConfirm open={sheet === 'finish'} onOpenChange={(o) => setSheet(o ? 'finish' : null)} verb="Finalizar etapa" title={`Finalizar etapa · ${myHours} h apontadas`}
        impact={finishImpact(d).map((l) => <p key={l}>{l}</p>)} onConfirm={() => finish(false)} />
      <FloorConfirm open={sheet === 'all'} onOpenChange={(o) => setSheet(o ? 'all' : null)} verb="Concluir etapa para todos" title={`Concluir etapa para todos · ${myHours} h apontadas`}
        impact={finishImpact(d, others.map((p) => p.name)).map((l) => <p key={l}>{l}</p>)} onConfirm={() => finish(true)} />
      <FloorConfirm open={sheet === 'leave'} onOpenChange={(o) => setSheet(o ? 'leave' : null)} verb="Sair da etapa" title={`Sair da etapa (só eu) · ${myHours} h apontadas`}
        impact={<><p>Seu apontamento fecha agora; o checklist não é exigido.</p><p>A etapa continua com {others.map((p) => p.name).join(', ')}.</p></>}
        onConfirm={async () => { await leaveShopfloorStep(d.step.stepId, tap()); notifySuccess('Você saiu da etapa'); await onChanged() }} />
    </div>
  )
}

/** Motivo da pausa na folha de tela cheia (CA 11): o mais usado pelo operador primeiro, texto opcional de até 120. */
function PauseSheet({ onOpenChange, suggested, onConfirm }: { onOpenChange: (open: boolean) => void; suggested: PauseReason; onConfirm: (reason: PauseReason, notes: string | null) => Promise<void> }) {
  const [reason, setReason] = useState<PauseReason>(suggested)
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)
  return (
    <FloorSheet open onOpenChange={onOpenChange} title="Pausar a etapa" description="Escolha o motivo. O cronômetro para até você tocar em Retomar."
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => onOpenChange(false)}>Voltar</FloorButton>
          <FloorButton className="flex-1" disabled={busy} onClick={() => { setBusy(true); void onConfirm(reason, notes.trim() || null).finally(() => { setBusy(false); onOpenChange(false) }) }}>Pausar</FloorButton>
        </div>
      )}>
      <fieldset className="space-y-[8px]">
        <legend className="mb-2 font-semibold">Motivo</legend>
        {orderedReasons(suggested).map((r) => (
          <label key={r.value} data-floor-target="" className="flex min-h-[56px] cursor-pointer items-center gap-4 rounded-md border-2 border-input bg-card px-4 has-[:checked]:border-primary has-[:checked]:bg-primary-soft">
            <input type="radio" name="motivo" value={r.value} checked={reason === r.value} onChange={() => setReason(r.value)} className="size-7 accent-primary" />
            <span>{r.label}{r.value === 'Material' ? <span className="block text-base text-muted-foreground">Avisa o almoxarifado.</span> : null}</span>
          </label>
        ))}
      </fieldset>
      <label className="block space-y-1">
        <span className="font-semibold">Observação (opcional)</span>
        <textarea rows={2} maxLength={MAX_PAUSE_NOTES} value={notes} onChange={(e) => setNotes(e.target.value)} className="w-full rounded-md border-2 border-input bg-card p-3 text-[18px]" />
        <span className="block text-right text-base text-muted-foreground tabular-nums">{notes.length}/{MAX_PAUSE_NOTES}</span>
      </label>
    </FloorSheet>
  )
}

/** Anexos técnicos da OS (CA 6), só visualização: pdf, png e jpg abrem na tela; dwg e dxf só baixam. */
function Attachments({ stepId, items }: { stepId: string; items: readonly ShopfloorAttachment[] }) {
  const [viewing, setViewing] = useState<{ item: ShopfloorAttachment; url: string } | null>(null)
  useEffect(() => () => { if (viewing) URL.revokeObjectURL(viewing.url) }, [viewing])
  const url = (a: ShopfloorAttachment) => getDownloadShopfloorAttachmentUrl(stepId, a.id)
  const open = async (a: ShopfloorAttachment) => {
    try {
      setViewing({ item: a, url: await apiBlobUrl(url(a)) })
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível abrir o arquivo.')
    }
  }
  if (items.length === 0) return null
  const kind = viewing ? viewableKind(viewing.item.contentType) : null
  return (
    <section aria-labelledby="desenhos" className="space-y-2">
      <h2 id="desenhos" className="text-[20px] font-bold">Desenhos e documentos</h2>
      <ul className="space-y-[8px]">
        {items.map((a) => (
          <li key={a.id} className="flex flex-wrap items-center gap-[8px] rounded-md border border-border bg-card p-2">
            <span className="min-w-0 flex-1 truncate px-2">{a.fileName}</span>
            {a.viewable ? <FloorButton tone="secondary" onClick={() => void open(a)}>Abrir</FloorButton> : null}
            <FloorButton tone="secondary" onClick={() => void apiDownload(url(a), a.fileName).catch(() => notifyError('Não foi possível baixar o arquivo.'))}>Baixar</FloorButton>
          </li>
        ))}
      </ul>
      <FloorSheet open={!!viewing} onOpenChange={(o) => { if (!o) setViewing(null) }} title={viewing?.item.fileName ?? 'Arquivo'}
        actions={<FloorButton tone="secondary" className="w-full" onClick={() => setViewing(null)}>Fechar</FloorButton>}>
        {viewing && kind === 'image' ? <img src={viewing.url} alt={viewing.item.fileName} className="mx-auto max-h-[70dvh] w-auto max-w-full rounded-md border border-border" /> : null}
        {viewing && kind === 'pdf' ? <iframe title={viewing.item.fileName} src={viewing.url} sandbox="allow-same-origin" className="h-[70dvh] w-full rounded-md border border-border" /> : null}
      </FloorSheet>
    </section>
  )
}

/** Depois de finalizar (propositivo): "Iniciar próxima: {etapa} da {OS}" e a volta à fila. */
function Finished({ result, text }: { result: ShopfloorActionResult; text: string }) {
  const navigate = useNavigate()
  const next: ShopfloorQueueStep | null | undefined = result.nextInQueue
  const [busy, setBusy] = useState(false)
  const startNext = async () => {
    if (!next) return
    setBusy(true)
    try {
      await startShopfloorStep(next.stepId, tap())
      notifySuccess('Etapa iniciada')
      await navigate({ to: '/fab/etapa/$stepId', params: { stepId: next.stepId } })
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível iniciar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <div className="mx-auto max-w-3xl space-y-4 p-4">
      <FloorState title={text} message={result.closedFor.length > 0 ? `Apontamentos encerrados também: ${result.closedFor.join(', ')}.` : undefined} />
      {next ? <FloorButton className="w-full" disabled={busy} onClick={() => void startNext()}>Iniciar próxima: {next.stepName} da {next.orderNumber}</FloorButton> : null}
      <Link to="/fab" className={cn(backLink, 'w-full')}>Voltar à fila</Link>
    </div>
  )
}