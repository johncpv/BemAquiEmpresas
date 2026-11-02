import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { type ReactNode, useRef, useState } from 'react'
import {
  getDownloadMaintenanceOrderFileUrl,
  uploadMaintenanceOrderFile, useGetMaintenanceOrder, useListMaintenanceOrderFiles,
} from '@/api/generated/bemaqui'
import type { MaintenanceChecklistResult, MaintenanceOrderDetail } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput, QuantityInput } from '@/components/form/inputs'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDate, formatDateTime, formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { STATUS_LABELS, STATUS_TONES } from './assets'
import { MAINTENANCE_DICAS, missingText, ORDER_STATUS_LABELS, ORDER_STATUS_TONES, ORDER_TYPE_LABELS } from './maintenance'
import { COST_DICAS, formatHours } from './maintenance-cost'
import { useOrderActions } from './maintenance-hooks'
import { CostTab, ProtectedCost, ScheduleDialog } from './MaintenanceCost'

const button = 'inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'

/**
 * OS de manutenção (`/app/patrimonio/manutencao/$id`, PAT-03a): abas Resumo, Checklist, Documentos e Histórico; "Iniciar", "Concluir manutenção"
 * (desabilitado com "Marque os {n} itens que faltam") e "Cancelar OS" com `assets.edit`. `?nova=1`: os próximos passos da abertura.
 */
export function MaintenanceOrderPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const query = useGetMaintenanceOrder(id)
  const canEdit = useCan('assets.edit')
  const canFinance = useCan('finance.view')
  const [created, setCreated] = useState(search.nova === '1')
  const [completed, setCompleted] = useState(false)
  const [dialog, setDialog] = useState<'start' | 'complete' | 'cancel' | 'schedule' | null>(null)
  const actions = useOrderActions(query.data)

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-96" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'OS de manutenção não encontrada.' : 'Não foi possível carregar a OS de manutenção.'} message={notFound ? 'Volte à lista de manutenção.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const o = query.data
  const open = o.status === 'Open'
  const running = o.status === 'InProgress'
  const { missing, text: missingLabel } = missingText(o.checklist)
  const createdSteps = [
    ...(canEdit && open ? [{ label: 'Iniciar agora', onSelect: () => { setCreated(false); setDialog('start') } }] : []),
    { label: 'Abrir no celular', onSelect: () => void navigate({ to: '/fab/manutencao/$id', params: { id: o.id } }) },
    ...(canEdit ? [{ label: 'Anexar foto', onSelect: () => { setCreated(false); void navigate({ to: '/app/patrimonio/manutencao/$id', params: { id: o.id }, search: { aba: 'documentos' } as never }) } }] : []),
  ]
  // A conclusão oferece o próximo passo (PAT-03b): lançar o custo da manutenção na aba Custo.
  const doneSteps = [
    ...(canEdit ? [{ label: 'Lançar o custo da manutenção', onSelect: () => { setCompleted(false); void navigate({ to: '/app/patrimonio/manutencao/$id', params: { id: o.id }, search: { aba: 'custo' } as never }) } }] : []),
    { label: 'Ver o bem', onSelect: () => void navigate({ to: '/app/patrimonio/bens/$id', params: { id: o.assetId }, search: { aba: 'manutencao' } as never }) },
    { label: 'Voltar à manutenção', onSelect: () => void navigate({ to: '/app/patrimonio/manutencao' }) },
  ]

  return (
    <>
      <PageHeading title={`${o.number} · ${o.assetName}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-wrap items-center gap-1.5">
            <StatusChip tone={ORDER_STATUS_TONES[o.status]}>{ORDER_STATUS_LABELS[o.status]}</StatusChip>
            <span className="text-sm text-muted-foreground">{ORDER_TYPE_LABELS[o.type]}</span>
            {o.machineStopped && (open || running) ? <StatusChip tone="crit">Máquina parada</StatusChip> : null}
            <Link to="/app/patrimonio/manutencao" className="ml-2 inline-flex min-h-11 items-center text-sm font-semibold text-primary">Voltar à lista</Link>
          </div>
          {canEdit && (open || running) ? (
            <div className="flex flex-wrap items-center gap-2">
              {open ? <button type="button" onClick={() => setDialog('start')} className={primary}>Iniciar</button> : null}
              <button type="button" onClick={() => setDialog('schedule')} className={button}>Planejar</button>
              {running ? (
                <span className="flex flex-col items-end">
                  <button type="button" disabled={missing > 0} aria-describedby={missing > 0 ? 'faltam-itens' : undefined} onClick={() => setDialog('complete')} className={primary}>Concluir manutenção</button>
                  {missingLabel ? <span id="faltam-itens" className="text-xs text-muted-foreground">{missingLabel}</span> : null}
                </span>
              ) : null}
              <button type="button" onClick={() => setDialog('cancel')} className={button + ' text-crit-ink'}>Cancelar OS</button>
            </div>
          ) : null}
        </div>
      </PageHeading>
      {created ? <div className="mb-4"><NextSteps title="OS de manutenção aberta" onDismiss={() => setCreated(false)} steps={createdSteps} /></div> : null}
      {completed ? <div className="mb-4"><NextSteps title="Manutenção concluída" onDismiss={() => setCompleted(false)} steps={doneSteps} /></div> : null}
      <StartDialog order={o} open={dialog === 'start'} onOpenChange={(v) => setDialog(v ? 'start' : null)} onConfirm={actions.start} />
      <CompleteDialog order={o} open={dialog === 'complete'} onOpenChange={(v) => setDialog(v ? 'complete' : null)} onConfirm={async (w, m, v) => { await actions.complete(w, m, v); setCompleted(true) }} />
      {canEdit ? <ScheduleDialog order={o} open={dialog === 'schedule'} onOpenChange={(v) => setDialog(v ? 'schedule' : null)} onSaved={actions.apply} /> : null}
      <CancelDialog order={o} open={dialog === 'cancel'} onOpenChange={(v) => setDialog(v ? 'cancel' : null)} onConfirm={actions.cancel} />
      <UrlTabs label="Seções da OS de manutenção" fallback="resumo" tabs={[
        { id: 'resumo', label: 'Resumo', content: <OrderSummary order={o} canFinance={canFinance} /> },
        { id: 'checklist', label: `Checklist${o.checklist.length ? ` (${o.checklist.length - missing}/${o.checklist.length})` : ''}`, content: <ChecklistTab order={o} canEdit={canEdit} onMark={actions.mark} /> },
        { id: 'custo', label: 'Custo', content: canEdit ? <CostTab order={o} canEdit={canEdit} /> : <ProtectedCost /> },
        { id: 'documentos', label: 'Documentos', content: <OrderFiles order={o} canEdit={canEdit} /> },
        { id: 'historico', label: 'Histórico', content: <RecordHistory entity="MaintenanceOrder" id={o.id} /> },
      ]} />
    </>
  )
}

function OrderSummary({ order: o, canFinance }: { order: MaintenanceOrderDetail; canFinance: boolean }) {
  const rows: [string, ReactNode][] = [
    ['Bem', <Link key="bem" to="/app/patrimonio/bens/$id" params={{ id: o.assetId }} search={{ aba: 'manutencao' } as never} className="font-semibold text-primary hover:underline">{o.assetTag} · {o.assetName}</Link>],
    ['Situação do bem', <StatusChip key="sit" tone={STATUS_TONES[o.assetStatus]}>{STATUS_LABELS[o.assetStatus]}</StatusChip>],
    ['Centro de trabalho', o.workCenterName ?? '—'],
    ['Plano', o.planDescription ?? (o.type === 'Preventive' ? 'Preventiva avulsa' : '—')],
    [o.type === 'Corrective' ? 'Problema' : 'O que será feito', o.problem],
    ['Agendada para', formatDate(o.scheduledOn)],
    ['Executor', o.supplierName ?? 'Equipe interna'],
    ...(o.contractValue != null ? [['Valor combinado', <span key="valor" className="inline-flex flex-wrap items-center gap-2">{formatMoney(o.contractValue)}{o.payable?.isEstimate ? <Tag title={COST_DICAS.valorCombinado}>Estimado</Tag> : null}</span>] as [string, ReactNode]] : []),
    ...(o.payable ? [['Título a pagar', canFinance
      ? <Link key="titulo" to={'/app/financeiro/pagar/$id' as never} params={{ id: o.payable.id } as never} className="font-semibold text-primary hover:underline">{o.payable.number}{o.payable.dueDate ? ` · vence em ${formatDate(o.payable.dueDate)}` : ''}</Link>
      : `${o.payable.number}${o.payable.dueDate ? ` · vence em ${formatDate(o.payable.dueDate)}` : ''}`] as [string, ReactNode]] : []),
    ...(o.cost != null && o.status === 'Done' ? [['Custo da manutenção', formatMoney(o.cost)] as [string, ReactNode]] : []),
    ...(o.downtimeFrom && o.hoursPerDay != null ? [['Parada no centro', `${formatHours(o.hoursPerDay)} por dia, de ${formatDate(o.downtimeFrom)}${o.downtimeTo && o.downtimeTo !== o.downtimeFrom ? ` a ${formatDate(o.downtimeTo)}` : ''}${o.machineStopped || o.status === 'InProgress' ? ' (até a conclusão)' : ''}`] as [string, ReactNode]] : []),
    ['Aberta', `${formatDateTime(o.createdAt)}${o.createdByName ? ` por ${o.createdByName}` : ' pelo plano'}`],
    ...(o.startedAt ? [['Iniciada', `${formatDateTime(o.startedAt)}${o.startedByName ? ` por ${o.startedByName}` : ''}`] as [string, ReactNode]] : []),
    ...(o.doneAt ? [['Concluída', `${formatDateTime(o.doneAt)}${o.doneByName ? ` por ${o.doneByName}` : ''}`] as [string, ReactNode], ['O que foi feito', o.workDone ?? '—'] as [string, ReactNode]] : []),
    ...(o.cancelledAt ? [['Cancelada', formatDateTime(o.cancelledAt)] as [string, ReactNode], ['Motivo', o.cancelReason ?? '—'] as [string, ReactNode]] : []),
  ]
  return (
    <dl className="max-w-3xl divide-y divide-border rounded-md border border-border bg-card text-sm">
      {rows.map(([label, value]) => (
        <div key={label} className="grid gap-1 p-3 sm:grid-cols-[200px_1fr]">
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="whitespace-pre-wrap tabular-nums">{value}</dd>
        </div>
      ))}
    </dl>
  )
}

const RESULT_LABELS: Record<NonNullable<MaintenanceChecklistResult>, string> = { Done: 'Feito', NotApplicable: 'Não se aplica' }

/** Aba Checklist (CA 6): "Feito" ou "Não se aplica" em cada item, só com a OS em andamento; tocar de novo desmarca. */
function ChecklistTab({ order: o, canEdit, onMark }: { order: MaintenanceOrderDetail; canEdit: boolean; onMark: (itemId: string, r: MaintenanceChecklistResult | null) => Promise<void> }) {
  const [error, setError] = useState<string>()
  if (o.checklist.length === 0) return <EmptyState title="Esta OS não tem checklist." description="Os itens vêm do plano preventivo. Descreva o que foi feito na conclusão." />
  const editable = canEdit && o.status === 'InProgress'
  const mark = async (itemId: string, r: MaintenanceChecklistResult | null) => {
    setError(undefined)
    try { await onMark(itemId, r) } catch (e) { setError(e instanceof ApiError ? e.message : 'Não foi possível marcar o item.') }
  }
  return (
    <div className="max-w-3xl space-y-3">
      {o.status === 'Open' && canEdit ? <p className="text-sm text-muted-foreground">Inicie a OS para marcar o checklist.</p> : null}
      <FormError message={error} />
      <ol className="space-y-2">
        {o.checklist.map((i) => (
          <li key={i.id} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-3 text-sm">
            <span className="flex-1"><b className="tabular-nums">{i.sequence}.</b> {i.text}
              {i.result ? <span className="block text-xs text-muted-foreground">{RESULT_LABELS[i.result]}{i.markedByName ? ` · ${i.markedByName}` : ''}{i.markedAt ? ` · ${formatDateTime(i.markedAt)}` : ''}</span> : null}
            </span>
            {editable ? (
              <span className="flex gap-2">
                {(['Done', 'NotApplicable'] as const).map((r) => (
                  <button key={r} type="button" aria-pressed={i.result === r} onClick={() => void mark(i.id, i.result === r ? null : r)}
                    className={`min-h-11 rounded-md border px-3 font-semibold ${i.result === r ? 'border-primary bg-primary text-primary-foreground' : 'border-border'}`}>{RESULT_LABELS[r]}</button>
                ))}
              </span>
            ) : i.result ? <StatusChip tone={i.result === 'Done' ? 'ok' : 'neutral'}>{RESULT_LABELS[i.result]}</StatusChip> : <StatusChip tone="warn">Pendente</StatusChip>}
          </li>
        ))}
      </ol>
    </div>
  )
}

/** Aba Documentos (CA 6): fotos e relatório do prestador (PNG, JPG ou PDF até 10 MB); a foto é regravada sem a localização. */
export function OrderFiles({ order: o, canEdit }: { order: MaintenanceOrderDetail; canEdit: boolean }) {
  const files = useListMaintenanceOrderFiles(o.id, { query: { gcTime: 0, staleTime: 0 } })
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string>()
  const [sending, setSending] = useState(false)
  if (files.isPending) return <Skeleton className="h-48 max-w-3xl" />
  if (files.isError || !files.data) return <ErrorState title="Não foi possível carregar os documentos da OS." onRetry={() => files.refetch()} />
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadMaintenanceOrderFile(o.id, { file })
      notifySuccess('Documento anexado')
      await files.refetch()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível enviar o arquivo.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }
  const attach = canEdit ? (
    <>
      <input ref={input} id={`anexo-${o.id}`} type="file" accept=".png,.jpg,.jpeg,.pdf" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor={`anexo-${o.id}`} className={button + ' cursor-pointer'}>{sending ? 'Enviando…' : 'Anexar foto ou relatório'}</label>
    </>
  ) : undefined
  return (
    <div className="max-w-3xl space-y-3">
      <p className="text-sm text-muted-foreground">Fotos e o relatório do prestador, em PNG, JPG ou PDF até 10 MB. A localização gravada na foto é apagada.</p>
      <FormError message={error} />
      {files.data.files.length === 0 ? <EmptyState title="Nenhum documento anexado." description="Anexe a foto do antes e do depois ou o relatório do prestador." action={attach} /> : (
        <>
          <ul aria-label="Documentos da OS" className="divide-y divide-border rounded-md border border-border bg-card text-sm">
            {files.data.files.map((f) => (
              <li key={f.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span className="flex flex-wrap items-center gap-2">
                  <button type="button" disabled={f.scanStatus !== 1} onClick={() => void apiDownload(getDownloadMaintenanceOrderFileUrl(o.id, f.id), f.fileName)} className="min-h-11 break-all font-semibold text-primary hover:underline disabled:text-muted-foreground">{f.fileName}</button>
                  {f.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}
                </span>
                <span className="text-xs text-muted-foreground tabular-nums">{formatDateTime(f.createdAt)}</span>
              </li>
            ))}
          </ul>
          {attach}
        </>
      )}
    </div>
  )
}

export function StartDialog({ order, open, onOpenChange, onConfirm }: { order: MaintenanceOrderDetail; open: boolean; onOpenChange: (o: boolean) => void; onConfirm: () => Promise<void> }) {
  return (
    <ConfirmDialog verb="Iniciar" done="Manutenção iniciada" title={`Iniciar ${order.number}?`} open={open} onOpenChange={onOpenChange} onConfirm={onConfirm}
      description={`A OS fica em andamento com a hora de agora e ${order.assetTag} passa a "Em manutenção" até a conclusão.`} />
  )
}

export function CompleteDialog({ order, open, onOpenChange, onConfirm }: { order: MaintenanceOrderDetail; open: boolean; onOpenChange: (o: boolean) => void; onConfirm: (workDone: string, meter: number | null, value: number | null) => Promise<void> }) {
  const [workDone, setWorkDone] = useState('')
  const [value, setValue] = useState<string | null>(null)
  const [meter, setMeter] = useState<string | null>(null)
  const [errors, setErrors] = useState<Partial<Record<string, string>>>({})
  const { missing, text } = missingText(order.checklist)
  return (
    <ConfirmDialog verb="Concluir manutenção" done="Manutenção concluída" title={`Concluir ${order.number}`} size="lg" open={open} onOpenChange={onOpenChange}
      description={order.planId ? 'A conclusão grava a última execução do plano e o próximo vencimento conta de hoje.' : 'O bem volta a Ativo se não houver outra OS em andamento.'}
      onOpen={() => { setWorkDone(''); setMeter(null); setValue(order.contractValue != null ? String(order.contractValue) : null); setErrors({}) }}
      preconditions={[{ ok: missing === 0, text: text ?? '' }, { ok: workDone.trim().length > 0, text: 'Descreva o que foi feito.' },
        ...(order.requiresPayableConfirmation ? [{ ok: Number(value) > 0, text: 'Informe o valor final do prestador.' }] : [])]}
      onFieldErrors={(e) => { const known = Object.fromEntries(Object.entries(e.fields).filter(([k]) => ['workDone', 'meterValue', 'contractValue'].includes(k)).map(([k, v]) => [k, v[0]])); setErrors(known); return Object.keys(known).length > 0 }}
      onConfirm={() => onConfirm(workDone.trim(), meter == null ? null : Number(meter), order.requiresPayableConfirmation && value ? Number(value) : null)}>
      <Field label="O que foi feito" required error={errors.workDone} hint={`${workDone.length}/2000`}>
        {(p) => <textarea {...p} rows={4} value={workDone} maxLength={2000} onChange={(e) => { setErrors({}); setWorkDone(e.target.value) }} className={inputClass} />}
      </Field>
      {order.meterPlan ? (
        <Field label="Horímetro na conclusão" error={errors.meterValue} hint="Opcional. O próximo vencimento conta deste valor." dica={MAINTENANCE_DICAS.horimetro}>
          {(p) => <QuantityInput {...p} value={meter} onChange={(v) => { setErrors({}); setMeter(v) }} suffix="h" />}
        </Field>
      ) : null}
      {order.requiresPayableConfirmation ? (
        <div className="space-y-2 rounded-md border border-warn-soft bg-card p-3 text-sm">
          <Field label="Valor final do prestador" required error={errors.contractValue} dica={COST_DICAS.valorCombinado}>
            {(p) => <MoneyInput {...p} value={value} onChange={(v) => { setErrors({}); setValue(v) }} />}
          </Field>
          <p className="font-semibold" aria-live="polite">
            {Number(value) > 0
              ? `Confirma o título a pagar de ${formatMoney(Number(value))} ao prestador ${order.supplierName ?? ''}, com vencimento em ${order.completionDueOn ? formatDate(order.completionDueOn) : 'a data da conclusão'}, que segue para ordem de pagamento.`
              : 'Informe o valor final para ver o título a pagar.'}
          </p>
        </div>
      ) : null}
    </ConfirmDialog>
  )
}

export function CancelDialog({ order, open, onOpenChange, onConfirm }: { order: MaintenanceOrderDetail; open: boolean; onOpenChange: (o: boolean) => void; onConfirm: (reason: string) => Promise<void> }) {
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog verb="Cancelar OS" done="OS cancelada" destructive title={`Cancelar ${order.number}?`} open={open} onOpenChange={onOpenChange}
      description={order.planId ? 'A última execução do plano não muda. O bem volta a Ativo se nenhuma outra OS o segurar.' : 'O bem volta a Ativo se nenhuma outra OS o segurar.'}
      onOpen={() => setReason('')}
      preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo.' }]}
      onConfirm={() => onConfirm(reason.trim())}>
      <Field label="Motivo" required hint={`${reason.length}/200`}>{(p) => <input {...p} value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
    </ConfirmDialog>
  )
}
