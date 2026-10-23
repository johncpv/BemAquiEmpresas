import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import {
  approveFabricationStep, completeFabricationStep, correctLaborEntry, getGetOrderLaborCostsQueryKey, getListOrderLaborEntriesQueryKey, recordLaborEntry, useListLaborEmployees,
  useListOrderLaborEntries,
} from '@/api/generated/bemaqui'
import type { FabricationOrderDetail, LaborEntryView, LaborPauseInput, PauseReason } from '@/api/generated/model'
import { IDEMPOTENCY_HEADER } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { formatDateTime, formatMoney } from '@/lib/format'
import { useBomRecovery } from '@/pages/engineering/boms/bom-recovery'
import { useCan } from '@/pages/use-can'
import { PAUSE_REASONS, pauseLabel } from '@/pages/shopfloor/shopfloor'
import { Dica } from '@/help/Dica'
import { hours } from './fabrication'
import { fromLocalInput, laborMarks, toLocalInput } from './labor'
import { LABOR_COST_DICAS, useOrderLaborCosts } from './labor-cost'
import { LaborCostCell, MissingCostNotice } from './labor-costs'

const button = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

interface PauseRow { reason: PauseReason; startedAt: string; endedAt: string }

/**
 * Aba Apontamentos da OS (PRD-03a CA 15, 18 a 21, 31): funcionário, etapa, início, fim, pausas com motivo, horas líquidas, quem
 * registrou e as marcas; "Apontar por funcionário" (production.edit + shopfloor.create), "Corrigir apontamento" e "Aprovar etapa"
 * (production.approve). Os links dos apontamentos abertos chegam com ?corrigir= ou ?concluir= e abrem a ação direto.
 */
export function LaborEntriesTab({ order, onChanged }: { order: FabricationOrderDetail; onChanged: () => Promise<void> }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as { corrigir?: string; concluir?: string }
  const query = useListOrderLaborEntries(order.id)
  // PRD-04 CA 14: custo só com projects.view_cost, num DTO separado; sem a permissão a aba mostra só as horas.
  const { canSee: canSeeCost, query: costs } = useOrderLaborCosts(order.id)
  const costOf = new Map((costs.data?.entries ?? []).map((c) => [c.entryId, c]))
  // Decisão do PO (22/10/2026): custo-hora e custo por apontamento só com o parâmetro "Exibir custo-hora em cada apontamento".
  const showEntryCost = canSeeCost && !!costs.data?.showsEntryCost
  const canEdit = useCan('production.edit')
  const canPoint = useCan('shopfloor.create')
  const canRecord = canEdit && canPoint
  const [concludeReason, setConcludeReason] = useState('')
  const canApprove = useCan('production.approve')
  // ENG-02 CA 16: a recusa por BOM não liberada leva à aba BOM.
  const bomRecovery = useBomRecovery(order.id)
  const [recording, setRecording] = useState(false)
  const [picked, setCorrecting] = useState<LaborEntryView | null>(null)
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getListOrderLaborEntriesQueryKey(order.id) })
    await client.invalidateQueries({ queryKey: getGetOrderLaborCostsQueryKey(order.id) })
    await onChanged()
  }
  const clearSearch = () => void navigate({ to: '.', search: { aba: 'apontamentos' }, replace: true } as never)

  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os apontamentos." onRetry={() => query.refetch()} />
  const data = query.data
  const recordButton = canRecord && order.status !== 'Cancelled' && order.status !== 'Completed'
    ? <button type="button" className={primary} onClick={() => setRecording(true)}>Apontar por funcionário</button> : null
  // Vindo dos apontamentos abertos (CA 22): ?corrigir={apontamento} ou ?concluir={etapa} abrem a ação direto.
  const correcting = picked ?? (canApprove && search.corrigir ? data.entries.find((e) => e.id === search.corrigir) ?? null : null)
  const concludeStep = canApprove ? order.steps.find((s) => s.id === search.concluir && s.status !== 'Done') : undefined
  return (
    <div className="space-y-3">
      {data.awaitingApprovalStepId ? (
        <div role="note" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-l-4 border-info-soft border-l-info bg-info-soft px-3 py-2 text-sm text-info-ink">
          <span>A etapa {data.awaitingApprovalStepName} foi finalizada e aguarda aprovação.</span>
          {canApprove ? (
            <ConfirmDialog verb="Aprovar etapa" done="Etapa aprovada" title={`Aprovar a etapa ${data.awaitingApprovalStepName}?`}
              description="A etapa fica concluída e a próxima fica pronta na fila da fábrica." onReload={refresh}
              onConfirm={async () => { await approveFabricationStep(order.id, data.awaitingApprovalStepId!, { version: order.version }); await refresh() }}
              trigger={<button type="button" className={primary}>Aprovar etapa</button>} />
          ) : null}
        </div>
      ) : null}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground tabular-nums">{data.entries.length} {data.entries.length === 1 ? 'apontamento' : 'apontamentos'} · {hours(data.totalHours)} h líquidas{canSeeCost && costs.data ? ` · ${formatMoney(costs.data.laborCost)} de mão de obra` : ''}</p>
        {data.entries.length > 0 ? recordButton : null}
      </div>
      {canSeeCost && costs.data ? <MissingCostNotice orderId={order.id} positions={costs.data.missingPositions} onDone={refresh} /> : null}
      {data.entries.length === 0 ? (
        <EmptyState title="Nenhum apontamento nesta OS." description="Os operadores apontam pelo app do chão de fábrica. Quem supervisiona pode apontar por um funcionário que não conseguiu." action={recordButton} />
      ) : (
        <div className="overflow-x-auto rounded-md border border-border bg-card">
          <table className="w-full text-sm">
            <caption className="sr-only">Apontamentos da {data.orderNumber}</caption>
            <thead className="border-b border-border text-left text-xs uppercase text-muted-foreground">
              <tr><th className="p-2">Funcionário</th><th className="p-2">Etapa</th><th className="p-2">Início</th><th className="p-2">Fim</th><th className="p-2 max-sm:hidden">Pausas</th><th className="p-2 text-right">Horas</th>{showEntryCost ? <th className="p-2 text-right"><span className="inline-flex items-center">Custo<Dica nome="Custo-hora" texto={LABOR_COST_DICAS.custoHora} /></span></th> : null}<th className="p-2 max-sm:hidden">Registrado por</th><th className="p-2"><span className="sr-only">Ações</span></th></tr>
            </thead>
            <tbody className="divide-y divide-border">
              {data.entries.map((e) => (
                <tr key={e.id} className="align-top">
                  <td className="p-2 font-medium">{e.employeeName}<span className="mt-1 flex flex-wrap gap-1">{laborMarks(e).map((m) => m === 'Autoaprovado' ? <Tag key={m}>{m}</Tag> : <StatusChip key={m} tone={m === 'Em aberto' ? 'info' : 'neutral'}>{m}</StatusChip>)}</span></td>
                  <td className="p-2">{e.stepCode} · {e.stepName}</td>
                  <td className="p-2 tabular-nums">{formatDateTime(e.startedAt)}</td>
                  <td className="p-2 tabular-nums">{e.endedAt ? formatDateTime(e.endedAt) : '—'}</td>
                  <td className="p-2 max-sm:hidden">{e.pauses.length === 0 ? '—' : e.pauses.map((p) => <span key={p.id} className="block">{pauseLabel(p.reason)}{p.notes ? ` (${p.notes})` : ''}{p.endedAt ? '' : ', em aberto'}</span>)}</td>
                  <td className="p-2 text-right tabular-nums">{hours(e.hours)}</td>
                  {showEntryCost ? <td className="p-2 text-right">{costs.isError ? '—' : <LaborCostCell orderId={order.id} cost={costOf.get(e.id)} />}</td> : null}
                  <td className="p-2 max-sm:hidden">{e.recordedByName ?? '—'}{e.correctionReason ? <span className="block text-xs text-muted-foreground">Correção: {e.correctionReason}{e.correctedByName ? `, por ${e.correctedByName}` : ''}</span> : null}</td>
                  <td className="p-2">{canApprove ? <button type="button" className={button} onClick={() => setCorrecting(e)}>Corrigir apontamento</button> : null}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {recording ? <RecordLaborDialog order={order} onClose={() => setRecording(false)} onDone={refresh} /> : null}
      {correcting ? <CorrectLaborDialog orderId={order.id} entry={correcting} onClose={() => { setCorrecting(null); if (search.corrigir) clearSearch() }} onDone={refresh} /> : null}
      {concludeStep ? (
        <ConfirmDialog verb="Concluir etapa" done="Etapa concluída" title={`Concluir a etapa ${concludeStep.name} da ${order.number}?`} open onOpenChange={(o) => { if (!o) clearSearch() }}
          description="Os apontamentos abertos da etapa fecham agora, com a marca Encerrado pelo supervisor, e a próxima etapa fica pronta." onReload={refresh} recovery={bomRecovery}
          preconditions={[{ ok: concludeReason.trim().length > 0, text: 'Informe o motivo.' }]}
          onConfirm={async () => { await completeFabricationStep(order.id, concludeStep.id, { reason: concludeReason.trim(), version: order.version }); await refresh() }}>
          <label className="block space-y-1 text-sm font-medium">Motivo
            <textarea rows={3} maxLength={200} value={concludeReason} onChange={(e) => setConcludeReason(e.target.value)} className={inputClass} />
          </label>
        </ConfirmDialog>
      ) : null}
    </div>
  )
}

/** Pausas do formulário do supervisor: motivo, início e fim. */
function PausesEditor({ rows, onChange }: { rows: PauseRow[]; onChange: (rows: PauseRow[]) => void }) {
  const set = (i: number, change: Partial<PauseRow>) => onChange(rows.map((r, n) => (n === i ? { ...r, ...change } : r)))
  return (
    <fieldset className="space-y-2">
      <legend className="text-sm font-medium">Pausas</legend>
      {rows.length === 0 ? <p className="text-sm text-muted-foreground">Sem pausas.</p> : null}
      {rows.map((r, i) => (
        <div key={i} className="grid gap-2 rounded-md border border-border p-2 sm:grid-cols-[1fr_1fr_1fr_auto]">
          <label className="space-y-1 text-xs font-medium">Motivo
            <select value={r.reason} onChange={(e) => set(i, { reason: e.target.value as PauseReason })} className={inputClass}>
              {PAUSE_REASONS.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
            </select>
          </label>
          <label className="space-y-1 text-xs font-medium">Início da pausa<input type="datetime-local" value={r.startedAt} onChange={(e) => set(i, { startedAt: e.target.value })} className={inputClass} /></label>
          <label className="space-y-1 text-xs font-medium">Fim da pausa<input type="datetime-local" value={r.endedAt} onChange={(e) => set(i, { endedAt: e.target.value })} className={inputClass} /></label>
          <button type="button" className={`${button} self-end`} onClick={() => onChange(rows.filter((_, n) => n !== i))}>Remover</button>
        </div>
      ))}
      <button type="button" className={button} onClick={() => onChange([...rows, { reason: 'Break', startedAt: '', endedAt: '' }])}>Adicionar pausa</button>
    </fieldset>
  )
}

const toPauses = (rows: PauseRow[]): LaborPauseInput[] =>
  rows.filter((r) => r.startedAt && r.endedAt).map((r) => ({ reason: r.reason, notes: null, startedAt: fromLocalInput(r.startedAt), endedAt: fromLocalInput(r.endedAt) }))

/**
 * "Apontar por funcionário" (CA 18, 19): funcionário ativo, etapa, início e fim ("agora" por padrão) e as pausas. O servidor
 * recusa início no futuro, fim antes do início, mais de 7 dias e sobreposição com outro apontamento do funcionário.
 */
function RecordLaborDialog({ order, onClose, onDone }: { order: FabricationOrderDetail; onClose: () => void; onDone: () => Promise<void> }) {
  const employees = useListLaborEmployees()
  const bomRecovery = useBomRecovery(order.id)
  const steps = order.steps.filter((s) => !s.isQualityStep && (s.status === 'Ready' || s.status === 'InProgress' || s.status === 'Paused') && !s.awaitingApproval)
  const [key] = useState(() => crypto.randomUUID())
  const [employeeId, setEmployeeId] = useState('')
  const [stepId, setStepId] = useState(steps[0]?.id ?? '')
  const [start, setStart] = useState(() => toLocalInput(new Date(Date.now() - 60 * 60 * 1000).toISOString()))
  const [now, setNow] = useState(true)
  const [end, setEnd] = useState(() => toLocalInput(new Date().toISOString()))
  const [pauses, setPauses] = useState<PauseRow[]>([])
  return (
    <ConfirmDialog size="lg" open onOpenChange={(o) => { if (!o) onClose() }} recovery={bomRecovery} verb="Apontar por funcionário" done="Apontamento registrado" title={`Apontar por funcionário na ${order.number}`}
      description="O apontamento fica com a marca Apontado por você e entra nas horas da etapa. Retroativo só até 7 dias."
      preconditions={[{ ok: !!employeeId, text: 'Escolha o funcionário.' }, { ok: !!stepId, text: steps.length === 0 ? 'Nenhuma etapa pronta ou em andamento nesta OS.' : 'Escolha a etapa.' }, { ok: !!start, text: 'Informe o início.' }]}
      onConfirm={async () => {
        await recordLaborEntry(order.id, { employeeId, stepId, startedAt: fromLocalInput(start), endedAt: now ? null : fromLocalInput(end), pauses: toPauses(pauses), notes: null }, { headers: { [IDEMPOTENCY_HEADER]: key } })
        await onDone()
      }}>
      <label className="block space-y-1 text-sm font-medium">Funcionário
        <select value={employeeId} onChange={(e) => setEmployeeId(e.target.value)} className={inputClass}>
          <option value="">{employees.isPending ? 'Carregando…' : 'Escolha'}</option>
          {(employees.data ?? []).map((e) => <option key={e.id} value={e.id}>{e.name}{e.registration ? ` (${e.registration})` : ''} · {e.positionName}</option>)}
        </select>
      </label>
      <label className="block space-y-1 text-sm font-medium">Etapa
        <select value={stepId} onChange={(e) => setStepId(e.target.value)} className={inputClass}>
          {steps.map((s) => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}
        </select>
      </label>
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="block space-y-1 text-sm font-medium">Início<input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} className={inputClass} /></label>
        <div className="space-y-1">
          <label className="block space-y-1 text-sm font-medium">Fim<input type="datetime-local" value={end} disabled={now} onChange={(e) => setEnd(e.target.value)} className={inputClass} /></label>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={now} onChange={(e) => setNow(e.target.checked)} className="size-4 accent-primary" />Termina agora</label>
        </div>
      </div>
      <PausesEditor rows={pauses} onChange={setPauses} />
    </ConfirmDialog>
  )
}

/** "Corrigir apontamento" (CA 20, 21): início, fim e pausas com motivo obrigatório; a correção fica no histórico com antes e depois. */
function CorrectLaborDialog({ orderId, entry, onClose, onDone }: { orderId: string; entry: LaborEntryView; onClose: () => void; onDone: () => Promise<void> }) {
  const [start, setStart] = useState(() => toLocalInput(entry.startedAt))
  const [end, setEnd] = useState(() => toLocalInput(entry.endedAt ?? new Date().toISOString()))
  const [pauses, setPauses] = useState<PauseRow[]>(() => entry.pauses.map((p) => ({ reason: p.reason, startedAt: toLocalInput(p.startedAt), endedAt: toLocalInput(p.endedAt ?? new Date().toISOString()) })))
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog size="lg" open onOpenChange={(o) => { if (!o) onClose() }} verb="Corrigir apontamento" done="Apontamento corrigido" title={`Corrigir o apontamento de ${entry.employeeName}`}
      description={entry.endedAt ? 'O custo da etapa é refeito pelas horas corrigidas.' : 'O apontamento está aberto: corrigir fecha com a marca Encerrado pelo supervisor.'}
      impact={<p>Antes: {formatDateTime(entry.startedAt)} → {entry.endedAt ? formatDateTime(entry.endedAt) : 'em aberto'} · {hours(entry.hours)} h</p>}
      preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo da correção.' }]}
      onConfirm={async () => {
        await correctLaborEntry(orderId, entry.id, { startedAt: fromLocalInput(start), endedAt: fromLocalInput(end), pauses: toPauses(pauses), reason: reason.trim(), version: entry.version })
        await onDone()
      }}>
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="block space-y-1 text-sm font-medium">Início<input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} className={inputClass} /></label>
        <label className="block space-y-1 text-sm font-medium">Fim<input type="datetime-local" value={end} onChange={(e) => setEnd(e.target.value)} className={inputClass} /></label>
      </div>
      <PausesEditor rows={pauses} onChange={setPauses} />
      <label className="block space-y-1 text-sm font-medium">Motivo
        <textarea rows={2} maxLength={200} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} />
      </label>
    </ConfirmDialog>
  )
}