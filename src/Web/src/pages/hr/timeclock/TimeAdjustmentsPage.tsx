import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { approveTimeAdjustments, useListEmployees, useListTimeAdjustments } from '@/api/generated/bemaqui'
import type { TimeAdjustmentBatchResult, TimeAdjustmentStatus, TimeAdjustmentView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { AdjustmentPanel, AdjustmentStatus } from './AdjustmentPanel'
import { InternalControlBanner } from './TimeClockPanel'
import { isStatus, selectable, shortDay, STATUS_FILTERS } from './time-adjustments'
import { TimeClockTabs } from './TimeClockTabs'

const text = (v: unknown) => (typeof v === 'string' && v ? v : undefined)
const field = 'min-h-11 rounded-md border border-input bg-background px-2 text-sm'

/**
 * RH → Ponto → Ajustes para aprovar (/app/rh/ponto/ajustes, RH-01b CA 10 a 15): os pedidos com filtro de situação e funcionário na URL
 * (?situacao, ?funcionario), o detalhe no painel lateral (?pedido) e "Aprovar selecionados" (D, R-PRP-6), com hr.view para ver e
 * hr.approve para decidir. A 400 px, os pedidos em cartões e o painel em tela cheia.
 */
export function TimeAdjustmentsPage() {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const status: TimeAdjustmentStatus | undefined = search.situacao === 'todos' ? undefined : isStatus(search.situacao) ? search.situacao : 'Pending'
  const employeeId = text(search.funcionario)
  const open = text(search.pedido) ?? null
  const canApprove = useCan('hr.approve')
  const employees = useListEmployees({ pageSize: 100, sort: 'name' })
  const query = useListTimeAdjustments({ status, employeeId }, { query: { retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2 } })
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirming, setConfirming] = useState(false)
  const [result, setResult] = useState<TimeAdjustmentBatchResult | null>(null)

  const set = (patch: Record<string, string | undefined>) =>
    void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => {
      const next = { ...prev, ...patch } as Record<string, unknown>
      for (const k of Object.keys(next)) if (!next[k]) delete next[k]
      return next
    } } as never)

  const items = query.data?.items ?? []
  const choosable = selectable(items)
  const chosen = choosable.filter((a) => selected.has(a.id))
  const toggle = (id: string) => setSelected((s) => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n })

  let content
  if (query.isPending) content = <div className="space-y-2"><Skeleton className="h-20" /><Skeleton className="h-20" /></div>
  else if (query.isError) content = <ErrorState title="Não foi possível carregar os pedidos de ajuste." onRetry={() => query.refetch()} />
  else if (items.length === 0) {
    content = (
      <EmptyState title={status === 'Pending' ? 'Nenhum ajuste aguardando aprovação.' : 'Nenhum pedido com este filtro.'}
        description="Os pedidos chegam pelo Meu ponto (o funcionário pede) ou pelo lançamento do RH na aba Ponto do funcionário."
        action={<Link to="/app/rh/funcionarios" className="text-sm font-semibold text-primary">Abrir os funcionários</Link>} />
    )
  } else {
    content = (
      <ul aria-label="Pedidos de ajuste" className="space-y-2">
        {items.map((a) => <AdjustmentCard key={a.id} adjustment={a} canSelect={canApprove && a.status === 'Pending' && a.canDecide} selected={selected.has(a.id)} onToggle={() => toggle(a.id)} onOpen={() => set({ pedido: a.id })} />)}
      </ul>
    )
  }

  return (
    <>
      <PageHeading title="RH: Ponto">
        <p className="mt-1 text-muted-foreground">Ajustes pedidos pelos funcionários e lançados pelo RH: nada muda no ponto antes da aprovação.</p>
      </PageHeading>
      <HrTabs />
      <div className="max-w-4xl space-y-4">
        <TimeClockTabs />
        <InternalControlBanner />
        <form aria-label="Filtros dos ajustes" className="flex flex-wrap items-end gap-3" onSubmit={(e) => e.preventDefault()}>
          <label className="flex min-w-48 flex-col text-xs font-semibold">Situação
            <select className={field} value={status ?? ''} onChange={(e) => { setSelected(new Set()); set({ situacao: e.target.value || 'todos' }) }}>
              {STATUS_FILTERS.map((s) => <option key={s.label} value={s.value}>{s.label}</option>)}
            </select>
          </label>
          <label className="flex min-w-48 flex-col text-xs font-semibold">Funcionário
            <select className={field} value={employeeId ?? ''} onChange={(e) => set({ funcionario: e.target.value || undefined })}>
              <option value="">Todos</option>
              {(employees.data?.items ?? []).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </label>
          {canApprove && choosable.length > 0 ? (
            <div className="flex flex-wrap items-center gap-2">
              <button type="button" onClick={() => setSelected(new Set(chosen.length === choosable.length ? [] : choosable.map((a) => a.id)))} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">
                {chosen.length === choosable.length ? 'Limpar seleção' : 'Selecionar todos'}
              </button>
              <button type="button" disabled={chosen.length === 0} onClick={() => setConfirming(true)} className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground disabled:opacity-60">
                Aprovar selecionados{chosen.length > 0 ? ` (${chosen.length})` : ''}
              </button>
            </div>
          ) : null}
        </form>
        {result ? <BatchResult result={result} items={items} onClose={() => setResult(null)} /> : null}
        {content}
      </div>
      <AdjustmentPanel id={open} onClose={() => set({ pedido: undefined })} onChanged={() => setSelected(new Set())} />
      <ConfirmDialog verb="Aprovar" done="Ajustes aprovados" title={`Aprovar ${chosen.length === 1 ? '1 ajuste' : `${chosen.length} ajustes`}?`} size="lg" open={confirming} onOpenChange={setConfirming}
        description="Cada pedido é decidido na sua transação. Os que não puderem ser aprovados ficam de fora com o motivo."
        impactList={{ label: 'Vão ser aprovados', items: chosen.map((a) => ({ key: a.id, primary: `${a.employeeName} · ${shortDay(a.day)}`, secondary: a.summary })) }}
        onConfirm={async () => {
          const saved = await approveTimeAdjustments({ items: chosen.map((a) => ({ id: a.id, version: a.version })) })
          setResult(saved)
          setSelected(new Set())
          await client.invalidateQueries()
        }} />
    </>
  )
}

function AdjustmentCard({ adjustment: a, canSelect, selected, onToggle, onOpen }: { adjustment: TimeAdjustmentView; canSelect: boolean; selected: boolean; onToggle: () => void; onOpen: () => void }) {
  return (
    <li className="flex items-start gap-3 rounded-md border border-border bg-card p-3">
      {canSelect ? (
        <input type="checkbox" checked={selected} onChange={onToggle} aria-label={`Selecionar: ${a.employeeName}, ${a.summary}`} className="mt-1 size-5 shrink-0" />
      ) : <span aria-hidden="true" className="size-5 shrink-0" />}
      <div className="min-w-0 flex-1 space-y-1">
        <p className="flex flex-wrap items-center justify-between gap-2">
          <span className="font-semibold">{a.employeeName}</span>
          <AdjustmentStatus adjustment={a} />
        </p>
        <p className="text-sm"><span className="tabular-nums">{shortDay(a.day)}</span> · {a.summary}</p>
        <p className="text-xs text-muted-foreground">{a.protected ? 'Ausência justificada' : a.justification}{a.hasDocument ? ' · Documento anexado' : ''}</p>
        <p className="text-xs text-muted-foreground">Pedido por {a.requestedByName ?? 'usuário'}{a.source === 'HR' ? ' (RH)' : ''}</p>
      </div>
      <button type="button" onClick={onOpen} className="min-h-11 shrink-0 rounded-md border border-border px-3 text-sm font-semibold">Ver</button>
    </li>
  )
}

/** Resultado do lote (CA 15): os aprovados e os que ficaram de fora com o motivo. */
function BatchResult({ result, items, onClose }: { result: TimeAdjustmentBatchResult; items: readonly TimeAdjustmentView[]; onClose: () => void }) {
  const name = (id: string) => items.find((a) => a.id === id)
  const failed = result.items.filter((i) => !i.approved)
  return (
    <section role="status" aria-label="Resultado da aprovação" className="space-y-2 rounded-md border border-border bg-card p-3 text-sm">
      <p className="font-semibold">{result.approved === 1 ? '1 ajuste aprovado' : `${result.approved} ajustes aprovados`}{result.failed > 0 ? `, ${result.failed} ficaram de fora` : ''}.</p>
      {failed.length > 0 ? (
        <ul className="space-y-1 text-warn-ink">
          {failed.map((f) => <li key={f.id}>{name(f.id)?.employeeName ?? 'Pedido'}{name(f.id) ? ` · ${name(f.id)!.summary}` : ''}: {f.message}</li>)}
        </ul>
      ) : null}
      <button type="button" onClick={onClose} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Fechar</button>
    </section>
  )
}
