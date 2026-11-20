import { useQueryClient } from '@tanstack/react-query'
import { useSearch } from '@tanstack/react-router'
import { Download } from 'lucide-react'
import { useState } from 'react'
import {
  getExportSyncDivergencesUrl, getListSyncDivergencesQueryKey, resolveSyncDivergence, useGetOperatorExceptions, useListStores, useListSyncDivergences,
} from '@/api/generated/bemaqui'
import type { SyncDivergenceKind, SyncDivergenceStatus, SyncDivergenceView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip, type StatusTone } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { registerLabel } from './stores'
import { DIVERGENCE_KINDS, KIND_LABEL, KIND_TONE, resolutionsFor, RESOLUTION_LABEL, type SyncResolution, todayInBrazil } from './pos-pending'

const EMPTY = 'Nenhuma pendência. Todas as vendas chegaram sem divergência.'
const QUARANTINE_DICA = 'Em quarentena: o evento chegou e está guardado, mas ainda não teve efeito (a venda não entrou nas vendas). Liberar faz ele entrar; manter fora deixa só no registro.'

/**
 * Loja → Pendências do PDV (OFF-02 CA 14 e 15): as divergências que a sincronização do caixa achou, por tipo, loja e caixa, com a venda, o
 * operador e o que divergiu. Quem tem `sales.approve` resolve com o motivo ("Aceitar como está"; na quarentena, "Liberar da quarentena" ou
 * "Manter fora"), auditado. Embaixo, as exceções por operador (remoções, reduções, descartes e eventos recusados). Carregando, vazio e erro.
 */
export function PosPendingPage() {
  const search = useSearch({ strict: false }) as { registerId?: string }
  const [status, setStatus] = useState<SyncDivergenceStatus | ''>('Open')
  const [kind, setKind] = useState<SyncDivergenceKind | ''>('')
  const [storeId, setStoreId] = useState('')
  const [registerId, setRegisterId] = useState(search.registerId ?? '')
  const [resolving, setResolving] = useState<SyncDivergenceView | null>(null)
  const [downloading, setDownloading] = useState(false)
  const stores = useListStores()
  const canResolve = useCan('sales.approve')
  const params = { status: status || undefined, kind: kind || undefined, storeId: storeId || undefined, registerId: registerId || undefined }
  const list = useListSyncDivergences(params)
  const items = list.data?.items ?? []
  const registers = (stores.data ?? []).filter((s) => !storeId || s.id === storeId).flatMap((s) => s.registers.map((r) => ({ id: r.id, label: `${s.name} · Caixa ${registerLabel(r.number)}` })))

  const exportCsv = async () => {
    setDownloading(true)
    try {
      await apiDownload(getExportSyncDivergencesUrl(params), `pendencias-do-pdv-${todayInBrazil()}.csv`)
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível exportar. Tente de novo.')
    } finally {
      setDownloading(false)
    }
  }

  const actions = (
    <button type="button" disabled={downloading || items.length === 0} onClick={() => void exportCsv()}
      className="inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60">
      <Download aria-hidden="true" className="size-4" />{downloading ? 'Exportando…' : 'Exportar'}
    </button>
  )

  return (
    <>
      <PageHeading title="Pendências do PDV" description="O que a sincronização dos caixas achou: preço desatualizado, relógio, sequência, pagamento e vendas em quarentena." actions={actions} />
      {list.data && Object.keys(list.data.openByKind).length > 0 ? (
        <ul aria-label="Pendências abertas por tipo" className="mb-3 flex flex-wrap gap-2">
          {DIVERGENCE_KINDS.filter((k) => (list.data?.openByKind[k] ?? 0) > 0).map((k) => (
            <li key={k}>
              <button type="button" onClick={() => { setKind(k); setStatus('Open') }} className="rounded-full focus-visible:outline-2">
                <StatusChip tone={KIND_TONE[k]}>{KIND_LABEL[k]}: <span className="tabular-nums">{list.data?.openByKind[k]}</span></StatusChip>
              </button>
            </li>
          ))}
        </ul>
      ) : null}
      <FormGrid>
        <Field label="Situação" size="sm">
          {(p) => (
            <select {...p} value={status} onChange={(e) => setStatus(e.target.value as SyncDivergenceStatus | '')} className={inputClass}>
              <option value="Open">Abertas</option>
              <option value="Resolved">Resolvidas</option>
              <option value="">Todas</option>
            </select>
          )}
        </Field>
        <Field label="Tipo" size="md">
          {(p) => (
            <select {...p} value={kind} onChange={(e) => setKind(e.target.value as SyncDivergenceKind | '')} className={inputClass}>
              <option value="">Todos os tipos</option>
              {DIVERGENCE_KINDS.map((k) => <option key={k} value={k}>{KIND_LABEL[k]}</option>)}
            </select>
          )}
        </Field>
        <Field label="Loja" size="md">
          {(p) => (
            <select {...p} value={storeId} onChange={(e) => { setStoreId(e.target.value); setRegisterId('') }} className={inputClass}>
              <option value="">Todas as lojas</option>
              {(stores.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Caixa" size="md">
          {(p) => (
            <select {...p} value={registerId} onChange={(e) => setRegisterId(e.target.value)} className={inputClass}>
              <option value="">Todos os caixas</option>
              {registers.map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
            </select>
          )}
        </Field>
      </FormGrid>
      <div className="mt-4">
        {list.isPending ? <Skeleton className="h-48" />
          : list.isError ? <ErrorState title="Não foi possível carregar as pendências." message={list.error instanceof ApiError ? list.error.message : undefined} onRetry={() => list.refetch()} />
          : items.length === 0 ? <EmptyState title={EMPTY} description="As vendas dos caixas aparecem aqui só quando algo não fecha. Mude os filtros para ver as resolvidas." />
          : (
            <div className="overflow-x-auto rounded-md border border-border">
              <table className="w-full text-sm">
                <caption className="sr-only">Pendências do PDV</caption>
                <thead className="bg-muted text-left">
                  <tr>
                    <th className="p-2">Tipo</th><th className="p-2">Loja e caixa</th><th className="p-2">Venda</th><th className="p-2">Operador</th>
                    <th className="p-2">O que divergiu</th><th className="p-2">Quando</th><th className="p-2"><span className="sr-only">Ação</span></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {items.map((d) => (
                    <tr key={d.id}>
                      <td className="p-2 align-top">
                        <div className="flex flex-col items-start gap-1">
                          <StatusChip tone={KIND_TONE[d.kind]}>{d.kindLabel}</StatusChip>
                          {d.quarantined && d.status === 'Open' ? <StatusChip tone="crit">Em quarentena<Dica nome="Quarentena" texto={QUARANTINE_DICA} /></StatusChip> : null}
                          {d.status === 'Resolved' ? <StatusChip tone="neutral">{d.resolution ? RESOLUTION_LABEL[d.resolution] : 'Resolvida'}</StatusChip> : null}
                        </div>
                      </td>
                      <td className="p-2 align-top">{d.storeName} · Caixa {registerLabel(d.registerNumber)}</td>
                      <td className="p-2 align-top tabular-nums">{d.saleNumber ?? '—'}</td>
                      <td className="p-2 align-top">{d.operatorName ?? '—'}</td>
                      <td className="p-2 align-top">
                        {d.summary}
                        {d.status === 'Resolved' && d.resolutionReason ? <p className="mt-1 text-muted-foreground">Motivo: {d.resolutionReason}{d.resolvedByName ? ` (${d.resolvedByName})` : ''}</p> : null}
                      </td>
                      <td className="p-2 align-top tabular-nums">{formatInstantDate(d.occurredAt)}</td>
                      <td className="p-2 align-top text-right">
                        {canResolve && d.status === 'Open' ? (
                          <button type="button" onClick={() => setResolving(d)} className="rounded-md bg-primary px-3 py-1.5 text-sm font-semibold text-primary-foreground">Resolver</button>
                        ) : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {list.data?.truncated ? <p className="p-2 text-sm text-muted-foreground">Mostrando as {items.length} mais recentes de {list.data.total}. Use os filtros para achar as outras.</p> : null}
            </div>
          )}
      </div>
      <OperatorExceptions storeId={storeId} />
      <ResolvePanel divergence={resolving} onClose={() => setResolving(null)} />
    </>
  )
}

/** CA 15: a resolução com o motivo, auditada. "Liberar da quarentena" faz o evento ter efeito (a venda entra). */
function ResolvePanel({ divergence, onClose }: { divergence: SyncDivergenceView | null; onClose: () => void }) {
  const client = useQueryClient()
  const options = divergence ? resolutionsFor(divergence.quarantined) : []
  const [resolution, setResolution] = useState<SyncResolution | ''>('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const chosen = resolution || options[0] || ''

  const close = () => {
    setResolution('')
    setReason('')
    setError(null)
    onClose()
  }

  const save = async () => {
    if (!divergence || !chosen) return false
    if (!reason.trim()) {
      setError('Informe o motivo.')
      return false
    }

    setBusy(true)
    setError(null)
    try {
      await resolveSyncDivergence(divergence.id, { resolution: chosen, reason: reason.trim(), version: divergence.version })
      notifySuccess(chosen === 'ReleaseQuarantine' ? 'Liberada da quarentena' : 'Pendência resolvida')
      await client.invalidateQueries({ queryKey: getListSyncDivergencesQueryKey() })
      close()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível resolver. Tente de novo.')
      return false
    } finally {
      setBusy(false)
    }
  }

  return (
    <SidePanel open={!!divergence} onOpenChange={(open) => { if (!open) close() }} title="Resolver pendência" description={divergence?.summary}
      footer={(
        <div className="flex justify-end gap-2">
          <button type="button" onClick={close} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Cancelar</button>
          <button type="button" disabled={busy} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">
            {busy ? 'Resolvendo…' : chosen ? RESOLUTION_LABEL[chosen] : 'Resolver'}
          </button>
        </div>
      )}>
      {divergence ? (
        <div className="space-y-4">
          <p className="text-sm text-muted-foreground">{divergence.kindLabel} · {divergence.storeName} · Caixa {registerLabel(divergence.registerNumber)}{divergence.saleNumber ? ` · Venda ${divergence.saleNumber}` : ''}</p>
          <fieldset className="space-y-2">
            <legend className="text-sm font-semibold">Resolução</legend>
            {options.map((o) => (
              <label key={o} className="flex items-center gap-2 text-sm">
                <input type="radio" name="resolution" value={o} checked={chosen === o} onChange={() => setResolution(o)} />
                {RESOLUTION_LABEL[o]}
              </label>
            ))}
          </fieldset>
          <Field label="Motivo" required>
            {(p) => <textarea {...p} value={reason} maxLength={300} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
          </Field>
          <FormError message={error ?? undefined} />
        </div>
      ) : null}
    </SidePanel>
  )
}

/** Exceções por operador (requisito da PDV-02): remoções, reduções, descartes e eventos recusados na conferência, por período. */
function OperatorExceptions({ storeId }: { storeId: string }) {
  const today = todayInBrazil()
  const [from, setFrom] = useState(`${today.slice(0, 8)}01`)
  const [to, setTo] = useState(today)
  const valid = !!from && !!to && from <= to
  const report = useGetOperatorExceptions({ from, to, storeId: storeId || undefined }, { query: { enabled: valid } })
  const rows = report.data?.rows ?? []
  const tone = (n: number): StatusTone => (n > 0 ? 'warn' : 'neutral')
  return (
    <section aria-labelledby="excecoes" className="mt-8 space-y-3">
      <h2 id="excecoes" className="font-display text-lg font-semibold">Exceções por operador<Dica nome="Exceções por operador" texto="Contadas quando as vendas do caixa sobem: itens removidos, quantidades reduzidas, vendas descartadas e eventos recusados na conferência do servidor." /></h2>
      <FormGrid>
        <Field label="De" size="sm">{(p) => <input {...p} type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={inputClass} />}</Field>
        <Field label="Até" size="sm">{(p) => <input {...p} type="date" value={to} onChange={(e) => setTo(e.target.value)} className={inputClass} />}</Field>
      </FormGrid>
      {!valid ? <p role="alert" className="text-sm text-crit-ink">A data final tem de ser igual ou depois da inicial.</p>
        : report.isPending ? <Skeleton className="h-24" />
        : report.isError ? <ErrorState title="Não foi possível carregar as exceções." onRetry={() => report.refetch()} />
        : rows.length === 0 ? <EmptyState title="Nenhuma exceção no período." description="Remoções, reduções, descartes e recusas aparecem aqui quando os caixas sincronizam." />
        : (
          <div className="overflow-x-auto rounded-md border border-border">
            <table className="w-full text-sm">
              <caption className="sr-only">Exceções por operador</caption>
              <thead className="bg-muted text-left">
                <tr><th className="p-2">Operador</th><th className="p-2 text-right">Itens removidos</th><th className="p-2 text-right">Quantidades reduzidas</th><th className="p-2 text-right">Vendas descartadas</th><th className="p-2 text-right">Recusados na conferência</th></tr>
              </thead>
              <tbody className="divide-y divide-border">
                {rows.map((r) => (
                  <tr key={r.operatorId ?? 'sem-operador'}>
                    <td className="p-2">{r.operatorName}</td>
                    <td className="p-2 text-right tabular-nums">{r.itemsRemoved}</td>
                    <td className="p-2 text-right tabular-nums">{r.quantitiesReduced}</td>
                    <td className="p-2 text-right tabular-nums">{r.salesDiscarded}</td>
                    <td className="p-2 text-right"><StatusChip tone={tone(r.eventsRejected)}><span className="tabular-nums">{r.eventsRejected}</span></StatusChip></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
    </section>
  )
}
