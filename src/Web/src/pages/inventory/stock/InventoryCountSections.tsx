import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import {
  addInventoryCountLine, countInventoryLine, getItemStockContext,
  recountInventoryLines, searchItems, useGetInventoryCountDivergences, useGetInventoryCountHistory, useGetInventoryCounting,
} from '@/api/generated/bemaqui'
import type { InventoryCountDetail, InventoryCountingLine, StockItemContext } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { inputClass } from '@/components/form/form-utils'
import { QuantityInput } from '@/components/form/inputs'
import { describeChanges } from '@/components/history/describe-changes'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { INVENTORY_DICAS, lineLabel } from './inventory-counts'
import { formatQuantity } from './stock'
import { useRefreshCount } from './use-refresh-count'

const searchStockItems = async (term: string): Promise<ComboOption[]> =>
  (await searchItems({ q: term || undefined })).filter((i) => i.type !== 'Service').map((i) => ({ id: i.id, label: `${i.code} — ${i.name}` }))

/** Erro da contagem com o "Recarregar" quando outra pessoa contou (CA 16). */
function useCountError() {
  const [errors, setErrors] = useState<Record<string, { message: string; reload: boolean }>>({})
  const fail = (key: string, e: unknown) =>
    setErrors((all) => ({ ...all, [key]: { message: e instanceof ApiError ? e.message : 'Não foi possível salvar. Tente de novo.', reload: e instanceof ApiError && e.status === 409 } }))
  const clear = (key: string) => setErrors((all) => {
    const next = { ...all }
    delete next[key]
    return next
  })
  return { errors, fail, clear }
}

/** Um cartão da contagem no backoffice: item, lote, quantidade e unidade, "Salvar contagem"; sem saldo do sistema (CA 3). */
function CountCard({ count, line, canCount }: { count: InventoryCountDetail; line: InventoryCountingLine; canCount: boolean }) {
  const [value, setValue] = useState<string | null>(line.countedQty === null ? null : String(line.enteredQuantity ?? line.countedQty))
  const [unit, setUnit] = useState(line.units.find((u) => u.code === line.enteredUnitCode)?.id ?? line.stockUnitId)
  const [busy, setBusy] = useState(false)
  const { errors, fail, clear } = useCountError()
  const refresh = useRefreshCount(count.id)
  const save = async () => {
    setBusy(true)
    clear(line.id)
    try {
      await countInventoryLine(count.id, line.id, { quantity: Number(value), unitId: unit === line.stockUnitId ? null : unit, version: line.version })
      notifySuccess('Contagem salva')
      await refresh()
    } catch (e) {
      fail(line.id, e)
    } finally {
      setBusy(false)
    }
  }
  const error = errors[line.id]
  return (
    <li className={line.countedQty === null ? 'space-y-2 rounded-md border border-l-4 border-border border-l-warn bg-card p-3 text-sm' : 'space-y-2 rounded-md border border-border bg-card p-3 text-sm'}>
      <p className="flex flex-wrap items-center gap-2"><b>{lineLabel(line)}</b>{line.isRecount ? <Tag>Recontagem</Tag> : null}{line.addedDuringCount ? <Tag>Incluído na contagem</Tag> : null}</p>
      <p className="text-muted-foreground">{line.countedQty === null ? 'Falta contar.' : `Contado ${formatQuantity(line.countedQty)} ${line.unitCode} por ${line.countedByName ?? '—'} · ${line.countedAt ? formatDateTime(line.countedAt) : ''}`}</p>
      {canCount ? (
        <div className="flex flex-wrap items-end gap-2">
          <label className="block">
            <span className="font-medium">Quantidade contada</span>
            <span className="flex items-center gap-2">
              <QuantityInput value={value} onChange={setValue} aria-label={`Quantidade contada de ${line.itemCode}`} />
              {line.units.length > 1 ? (
                <select aria-label={`Unidade de ${line.itemCode}`} value={unit} onChange={(e) => setUnit(e.target.value)} className={`${inputClass} w-24`}>
                  {line.units.map((u) => <option key={u.id} value={u.id}>{u.code}</option>)}
                </select>
              ) : <span className="text-muted-foreground">{line.unitCode}</span>}
            </span>
          </label>
          <button type="button" disabled={busy || value === null || value === ''} onClick={() => void save()} className="min-h-[44px] rounded-md bg-primary px-3 py-2 font-semibold text-primary-foreground disabled:opacity-60">
            {busy ? 'Salvando…' : 'Salvar contagem'}
          </button>
        </div>
      ) : null}
      {error ? (
        <p role="alert" className="text-crit-ink">{error.message} {error.reload ? <button type="button" className="font-semibold underline" onClick={() => { clear(line.id); void refresh() }}>Recarregar</button> : null}</p>
      ) : null}
    </li>
  )
}

/** "Encontrei outro item" (CA 3): item e lote que não estavam na lista, com a quantidade contada. */
function FoundItem({ count }: { count: InventoryCountDetail }) {
  const [item, setItem] = useState<ComboOption | null>(null)
  const [context, setContext] = useState<StockItemContext | null>(null)
  const [lot, setLot] = useState('')
  const [value, setValue] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const refresh = useRefreshCount(count.id)
  const choose = async (option: ComboOption | null) => {
    setItem(option)
    setContext(option ? await getItemStockContext(option.id) : null)
    setLot('')
  }
  const lots = context ? [...new Map(context.lots.map((l) => [l.lotId, l])).values()] : []
  const add = async () => {
    setError(null)
    try {
      await addInventoryCountLine(count.id, { itemId: item!.id, lotId: context?.tracksLot ? lot : null, quantity: Number(value), unitId: null })
      notifySuccess('Contagem salva')
      setItem(null)
      setContext(null)
      setValue(null)
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível incluir. Tente de novo.')
    }
  }
  return (
    <details className="rounded-md border border-dashed border-border p-3 text-sm">
      <summary className="cursor-pointer font-semibold text-primary">Encontrei outro item</summary>
      <div className="mt-2 space-y-2">
        <Combobox queryKey={['count-found-items']} search={searchStockItems} value={item?.id ?? null} selected={item} placeholder="Busque o item pelo código ou nome" onChange={(o) => void choose(o)} aria-label="Item encontrado" />
        {context?.tracksLot ? (
          <select aria-label="Lote do item encontrado" value={lot} onChange={(e) => setLot(e.target.value)} className={inputClass}>
            <option value="">Escolha o lote</option>
            {lots.map((l) => <option key={l.lotId} value={l.lotId}>{l.lotNumber}</option>)}
          </select>
        ) : null}
        <span className="flex items-center gap-2">
          <QuantityInput value={value} onChange={setValue} aria-label="Quantidade contada do item encontrado" />
          <span className="text-muted-foreground">{context?.unitCode ?? ''}</span>
        </span>
        <button type="button" disabled={!item || value === null || value === '' || (context?.tracksLot && !lot)} onClick={() => void add()} className="min-h-[44px] rounded-md bg-primary px-3 py-2 font-semibold text-primary-foreground disabled:opacity-60">Incluir e salvar contagem</button>
        {error ? <p role="alert" className="text-crit-ink">{error}</p> : null}
      </div>
    </details>
  )
}

/** Aba Contagem (CA 3): a contagem cega no backoffice, um cartão por item e lote, e o "Encontrei outro item". */
export function CountingSection({ count }: { count: InventoryCountDetail }) {
  const query = useGetInventoryCounting(count.id)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar a contagem." message="Verifique a conexão e tente de novo." onRetry={() => query.refetch()} />
  const view = query.data
  return (
    <div className="space-y-3">
      <p className="inline-flex items-center text-sm text-muted-foreground" aria-live="polite">{view.progress}<Dica nome="Contagem cega" texto={INVENTORY_DICAS.cega} /></p>
      {view.lines.length === 0 ? <EmptyState title="Nada para você contar agora." description="As linhas que faltam são recontagens de itens que você contou: outra pessoa reconta." /> : null}
      <ul className="space-y-2" aria-label="Itens da contagem">
        {view.lines.map((l) => <CountCard key={`${l.id}-${l.version}`} count={count} line={l} canCount={view.canCount} />)}
      </ul>
      {view.canCount ? <FoundItem count={count} /> : null}
    </div>
  )
}

/** Aba Divergências (CA 5 e 7): linhas com diferença pelo valor, movimento durante a contagem e o pedido de recontagem. */
export function DivergencesSection({ count }: { count: InventoryCountDetail }) {
  const query = useGetInventoryCountDivergences(count.id, {})
  const [marked, setMarked] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const refresh = useRefreshCount(count.id)
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar as divergências." message="Verifique a conexão e tente de novo." onRetry={() => query.refetch()} />
  const d = query.data
  const recount = async () => {
    setError(null)
    try {
      await recountInventoryLines(count.id, { lineIds: marked, version: count.version })
      notifySuccess(marked.length === 1 ? 'Linha enviada para a recontagem' : 'Linhas enviadas para a recontagem')
      setMarked([])
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível pedir a recontagem.')
    }
  }
  return (
    <div className="space-y-3">
      <p className="text-sm">Sobras <Money value={d.surplus} className="font-semibold" /> · Faltas <Money value={d.shortage} className="font-semibold" /> · Líquido <Money value={d.net} className="font-semibold" />
        {d.uncounted > 0 ? <span className="text-muted-foreground"> · {d.uncounted} sem contagem (decida no fechamento)</span> : null}</p>
      {d.lines.length === 0 ? <EmptyState title="Nenhuma divergência até agora." description="As linhas contadas batem com o saldo do sistema, já descontados os movimentos durante a contagem." /> : (
        <ul className="space-y-2" aria-label="Divergências">
          {d.lines.map((l) => (
            <li key={l.id} className={l.difference < 0 ? 'space-y-1 rounded-md border border-l-4 border-border border-l-crit bg-card p-3 text-sm' : 'space-y-1 rounded-md border border-l-4 border-border border-l-info bg-card p-3 text-sm'}>
              <p className="flex flex-wrap items-center gap-2">
                {l.canRequestRecount ? <input type="checkbox" className="size-5" aria-label={`Recontar ${l.itemCode}`} checked={marked.includes(l.id)} onChange={(e) => setMarked((all) => (e.target.checked ? [...all, l.id] : all.filter((x) => x !== l.id)))} /> : null}
                <b>{lineLabel(l)}</b>
                {l.hadMovement ? <span className="inline-flex items-center"><Tag>Teve movimento durante a contagem</Tag><Dica nome="Movimento durante a contagem" texto={INVENTORY_DICAS.movimento} /></span> : null}
                {l.isRecount ? <Tag>Recontagem</Tag> : null}
              </p>
              <p className="tabular-nums">
                Diferença <b>{formatQuantity(l.difference, true)} {l.unitCode}</b> · <Money value={l.value} />
                <span className="text-muted-foreground"> · foto {formatQuantity(l.systemQty)}{l.hadMovement ? ` · movimentos ${formatQuantity(l.movementDelta, true)}` : ''} · contado {l.counted ? formatQuantity(l.countedQty ?? 0) : '— (não contado)'}</span>
              </p>
              {l.previousCountedQty !== null ? <p className="text-muted-foreground">Primeira contagem: {formatQuantity(l.previousCountedQty)} por {l.previousCountedByName ?? '—'}</p> : null}
              <p className="text-muted-foreground">{l.counted ? `Contado por ${l.countedByName ?? '—'}${l.countedAt ? ` · ${formatDateTime(l.countedAt)}` : ''}` : ''}</p>
            </li>
          ))}
        </ul>
      )}
      {count.status === 'Counting' && count.canManage ? (
        <div className="flex flex-wrap items-center gap-2">
          <button type="button" disabled={marked.length === 0} onClick={() => void recount()} className="min-h-[44px] rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60">Pedir recontagem</button>
          <span className="inline-flex items-center text-sm text-muted-foreground">Marque as linhas para outra pessoa recontar.<Dica nome="Recontagem por outra pessoa" texto={INVENTORY_DICAS.recontagem} /></span>
        </div>
      ) : null}
      {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
    </div>
  )
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Excluiu' }

/** Aba Histórico de alterações (CA 17): o inventário e cada contagem e recontagem, com quem e quando. */
export function CountHistorySection({ count }: { count: InventoryCountDetail }) {
  const [page, setPage] = useState(1)
  const query = useGetInventoryCountHistory(count.id, { page, pageSize: 20 })
  if (query.isPending) return <Skeleton className="h-32" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="Cada contagem, recontagem e o fechamento aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-2">
        {data.items.map((e) => (
          <li key={e.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p><b>{ACTIONS[e.action] ?? e.action}</b> <span className="text-muted-foreground">{e.entity === 'InventoryCountLine' ? 'linha da contagem' : 'inventário'} · {e.userId ? 'usuário ' + e.userId.slice(0, 8) : e.origin ?? 'sistema'} · {formatDateTime(e.at)}</span></p>
            <ul className="mt-1 text-muted-foreground">{describeChanges(e.changes).map((t) => <li key={t}>{t}</li>)}</ul>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="mt-3 flex items-center justify-between text-xs text-muted-foreground">
          <span className="tabular-nums">Página {data.page} de {pages}</span>
          <span className="flex gap-1">
            <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="rounded-sm border border-border px-2 py-1 disabled:opacity-50">Anterior</button>
            <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="rounded-sm border border-border px-2 py-1 disabled:opacity-50">Próxima</button>
          </span>
        </div>
      ) : null}
      <p className="mt-2 text-xs text-muted-foreground">A decisão do ajuste fica no histórico do ajuste{count.adjustmentId ? <> (<Link to={'/app/estoque/ajustes/$id' as never} params={{ id: count.adjustmentId } as never} search={{ aba: 'decisao' } as never} className="font-semibold text-primary">Ver decisão</Link>)</> : null}.</p>
    </div>
  )
}
