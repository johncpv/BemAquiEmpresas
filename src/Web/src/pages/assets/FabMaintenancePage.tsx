import { Link, useParams } from '@tanstack/react-router'
import { useEffect, useRef, useState } from 'react'
import { uploadMaintenanceOrderFile, useGetMaintenanceOrder } from '@/api/generated/bemaqui'
import type { MaintenanceChecklistResult } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FloorButton, FloorConfirm, FloorDica, FloorHeader, FloorSheet, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { StatusChip } from '@/components/ui/status-chip'
import { useHelp } from '@/help/help-context'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { MAINTENANCE_DICAS, missingText, ORDER_STATUS_LABELS, ORDER_STATUS_TONES, ORDER_TYPE_LABELS, parseNumber } from './maintenance'
import { useOrderActions } from './maintenance-hooks'

const RESULTS: { value: MaintenanceChecklistResult; label: string }[] = [{ value: 'Done', label: 'Feito' }, { value: 'NotApplicable', label: 'Não se aplica' }]

/**
 * OS de manutenção no celular (`/fab/manutencao/$id`, PAT-03a CA 5 e 6): o problema, "Iniciar", o checklist com alvos de 56 px ("Feito" e "Não
 * se aplica"), a foto pela câmera e "Concluir manutenção" (desabilitado com "Marque os {n} itens que faltam"). Desenhada para 400 px.
 */
export function FabMaintenancePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const help = useHelp()
  const query = useGetMaintenanceOrder(id, { query: { retry: false } })
  const canEdit = useCan('assets.edit')
  const actions = useOrderActions(query.data)
  const [sheet, setSheet] = useState<'start' | 'complete' | null>(null)
  const [workDone, setWorkDone] = useState('')
  const [meter, setMeter] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const photo = useRef<HTMLInputElement>(null)
  useEffect(() => { document.title = 'OS de manutenção · BemAqui Empresas' }, [])

  const order = query.data
  const header = <FloorHeader onHelp={() => help.setOpen(true)} title={order ? order.number : 'OS de manutenção'} subtitle={order ? `${order.assetTag} · ${order.assetName}` : 'Patrimônio'} />
  if (query.isPending) return <>{header}<div className="mx-auto max-w-3xl p-4"><FloorSkeleton /></div></>
  if (query.isError || !order) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return (
      <>{header}<div className="mx-auto max-w-3xl p-4">
        {notFound ? <FloorState title="OS de manutenção não encontrada." message="Confira o link ou abra a OS pela ficha do bem." />
          : <FloorState tone="error" title="Não foi possível carregar a OS." message="Verifique a conexão e tente de novo." action={<FloorButton onClick={() => void query.refetch()}>Tentar de novo</FloorButton>} />}
      </div></>
    )
  }

  const running = order.status === 'InProgress'
  const { missing, text } = missingText(order.checklist)
  const mark = async (itemId: string, result: MaintenanceChecklistResult | null) => {
    try { await actions.mark(itemId, result) } catch (e) { notifyError(e instanceof ApiError ? e.message : 'Não foi possível marcar o item.') }
  }
  const sendPhoto = async (file: File) => {
    try {
      await uploadMaintenanceOrderFile(order.id, { file })
      notifySuccess('Foto anexada')
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível enviar a foto.')
    } finally {
      if (photo.current) photo.current.value = ''
    }
  }
  const complete = async () => {
    setBusy(true)
    setError(null)
    try {
      await actions.complete(workDone.trim(), parseNumber(meter))
      notifySuccess('Manutenção concluída')
      setSheet(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível concluir. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {header}
      <div className="mx-auto max-w-3xl space-y-4 p-4">
        <article className="space-y-3 rounded-md border-2 border-border bg-card p-4">
          <p className="flex flex-wrap items-center gap-2">
            <StatusChip tone={ORDER_STATUS_TONES[order.status]}>{ORDER_STATUS_LABELS[order.status]}</StatusChip>
            <span className="text-muted-foreground">{ORDER_TYPE_LABELS[order.type]} · agendada para {formatDate(order.scheduledOn)}</span>
          </p>
          {order.machineStopped && (running || order.status === 'Open') ? <StatusChip tone="crit">Máquina parada</StatusChip> : null}
          <p className="whitespace-pre-wrap text-[20px] font-semibold">{order.problem}</p>
          <p className="text-muted-foreground">Executor: {order.supplierName ?? 'Equipe interna'}</p>
          {order.workDone ? <p className="whitespace-pre-wrap">Feito: {order.workDone}</p> : null}
        </article>
        {canEdit && order.status === 'Open' ? <FloorButton className="w-full" onClick={() => setSheet('start')}>Iniciar</FloorButton> : null}
        {order.checklist.length ? (
          <section aria-labelledby="checklist-titulo" className="space-y-3">
            <h2 id="checklist-titulo" className="text-[20px] font-bold">Checklist ({order.checklist.length - missing}/{order.checklist.length})</h2>
            {order.status === 'Open' ? <p className="text-muted-foreground">Inicie a OS para marcar o checklist.</p> : null}
            <ol className="space-y-3">
              {order.checklist.map((i) => (
                <li key={i.id} className="space-y-2 rounded-md border-2 border-border bg-card p-3">
                  <p className="font-medium">{i.sequence}. {i.text}</p>
                  <div role="group" aria-label={`Resultado do item ${i.sequence}`} className="grid grid-cols-2 gap-[8px]">
                    {RESULTS.map((r) => (
                      <FloorButton key={r.value} tone={i.result === r.value ? 'ok' : 'secondary'} aria-pressed={i.result === r.value} disabled={!canEdit || !running}
                        onClick={() => void mark(i.id, i.result === r.value ? null : r.value)}>{r.label}</FloorButton>
                    ))}
                  </div>
                </li>
              ))}
            </ol>
          </section>
        ) : null}
        {canEdit && (running || order.status === 'Open') ? (
          <>
            <input ref={photo} id="foto-os" type="file" accept="image/png,image/jpeg,application/pdf" capture="environment" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void sendPhoto(f) }} />
            <label htmlFor="foto-os" data-floor-target="" className="flex min-h-[56px] w-full cursor-pointer items-center justify-center rounded-md border-2 border-input bg-card px-5 text-[18px] font-semibold">Tirar foto ou anexar relatório</label>
          </>
        ) : null}
        {canEdit && running && order.requiresPayableConfirmation ? (
          // PAT-03b CA 3: com prestador, a conclusão confirma o título a pagar no diálogo com o impacto financeiro, na OS completa.
          <a href={`/app/patrimonio/manutencao/${order.id}`} data-floor-target="" className="flex min-h-[56px] w-full items-center justify-center rounded-md border-2 border-input bg-card px-5 text-center text-[18px] font-semibold">
            Concluir na OS completa (confirma o título do prestador)
          </a>
        ) : null}
        {canEdit && running && !order.requiresPayableConfirmation ? (
          <div className="space-y-1">
            <FloorButton tone="ok" className="w-full" disabled={missing > 0} aria-describedby={missing > 0 ? 'faltam' : undefined} onClick={() => { setWorkDone(''); setMeter(''); setError(null); setSheet('complete') }}>
              Concluir manutenção
            </FloorButton>
            {text ? <p id="faltam" className="text-center text-muted-foreground">{text}</p> : null}
          </div>
        ) : null}
        <Link to="/app/patrimonio/manutencao/$id" params={{ id: order.id }} data-floor-target="" className="flex min-h-[56px] w-full items-center justify-center rounded-md border-2 border-input bg-card px-5 text-[18px] font-semibold">Ver OS completa</Link>
      </div>
      <FloorConfirm open={sheet === 'start'} onOpenChange={(o) => setSheet(o ? 'start' : null)} title={`Iniciar ${order.number}`} verb="Iniciar"
        impact={<p>{order.assetTag} passa a "Em manutenção" até a conclusão.</p>}
        onConfirm={async () => { await actions.start(); notifySuccess('Manutenção iniciada') }} />
      <FloorSheet open={sheet === 'complete'} onOpenChange={(o) => setSheet(o ? 'complete' : null)} title={`Concluir ${order.number}`}
        description="Descreva o que foi feito. O bem volta a Ativo se não houver outra OS em andamento."
        actions={(
          <div className="flex gap-[16px]">
            <FloorButton tone="secondary" className="flex-1" onClick={() => setSheet(null)} disabled={busy}>Voltar</FloorButton>
            <FloorButton tone="ok" className="flex-1" disabled={busy || !workDone.trim()} onClick={() => void complete()}>{busy ? 'Gravando…' : 'Concluir manutenção'}</FloorButton>
          </div>
        )}>
        <label className="block space-y-2">
          <span className="font-semibold">O que foi feito</span>
          <textarea rows={4} value={workDone} maxLength={2000} onChange={(e) => setWorkDone(e.target.value)} className="w-full rounded-md border-2 border-input bg-card p-3 text-[18px]" />
        </label>
        {order.meterPlan ? (
          <div className="space-y-2">
            <div className="flex items-center">
              <label htmlFor="horimetro" className="font-semibold">Horímetro na conclusão (opcional)</label>
              <FloorDica nome="Horímetro" texto={MAINTENANCE_DICAS.horimetro} />
            </div>
            <input id="horimetro" inputMode="decimal" value={meter} onChange={(e) => setMeter(e.target.value)} className="min-h-[56px] w-full rounded-md border-2 border-input bg-card px-3 text-[18px] tabular-nums" />
          </div>
        ) : null}
        {error ? <div role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-crit-ink">{error}</div> : null}
      </FloorSheet>
    </>
  )
}
