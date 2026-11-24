import { useQueryClient } from '@tanstack/react-query'
import { useSearch } from '@tanstack/react-router'
import { Download, FileText } from 'lucide-react'
import { useState } from 'react'
import {
  approveCashSession, checkCashSessionPix, getExportCashSessionsUrl, getGetCashSessionQueryKey, getListCashSessionsQueryKey, reopenCashSession, useGetCashSession,
  useListCashSessions, useListStores,
} from '@/api/generated/bemaqui'
import type { CashSessionDetail, CashSessionStatus, PixCheckStatus } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatMoney } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { BREAK_DICA, BLIND_DICA, differenceText, EMPTY, reportUrl, STATUS_TONE, statusOptions } from './cash-sessions'
import { useCan } from '@/pages/use-can'
import { CashSessionFinance } from './CashSessionFinance'
import { registerLabel } from './stores'
import { todayInBrazil } from './pos-pending'

/**
 * Loja → Caixas (CX-02 CA 7 a 13): as sessões de caixa por loja, caixa, operador e situação, com o chip "Em conferência". O quadro do fechamento
 * (esperado, contado e diferença por forma) e as ações (aprovar, reabrir, conferir Pix) são de quem tem `sales.approve`: o servidor só manda o
 * esperado para essa permissão. Carregando, vazio e erro.
 */
export function CashSessionsPage() {
  const search = useSearch({ strict: false }) as { sessionId?: string }
  const [storeId, setStoreId] = useState('')
  const [registerId, setRegisterId] = useState('')
  const [status, setStatus] = useState<CashSessionStatus | ''>('')
  const [open, setOpen] = useState<string | null>(search.sessionId ?? null)
  const [downloading, setDownloading] = useState(false)
  const stores = useListStores()
  const params = { storeId: storeId || undefined, registerId: registerId || undefined, status: status || undefined }
  const list = useListCashSessions(params)
  const items = list.data?.items ?? []
  const registers = (stores.data ?? []).filter((s) => !storeId || s.id === storeId).flatMap((s) => s.registers.map((r) => ({ id: r.id, label: `${s.name} · Caixa ${registerLabel(r.number)}` })))

  const exportCsv = async () => {
    setDownloading(true)
    try {
      await apiDownload(getExportCashSessionsUrl(params), `caixas-${todayInBrazil()}.csv`)
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
      <PageHeading title="Caixas" description="Os turnos dos caixas: abertura, contagem cega, quebra e conferência do gerente." actions={actions} />
      {list.data && list.data.pendingReview > 0 ? (
        <button type="button" onClick={() => setStatus('PendingReview')} className="mb-3 rounded-full focus-visible:outline-2">
          <StatusChip tone="warn">Em conferência: <span className="tabular-nums">{list.data.pendingReview}</span></StatusChip>
        </button>
      ) : null}
      <FormGrid>
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
        <Field label="Situação" size="md">
          {(p) => (
            <select {...p} value={status} onChange={(e) => setStatus(e.target.value as CashSessionStatus | '')} className={inputClass}>
              <option value="">Todas</option>
              {statusOptions(list.data?.expectedVisible ?? false).map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          )}
        </Field>
      </FormGrid>
      <div className="mt-4">
        {list.isPending ? <Skeleton className="h-48" />
          : list.isError ? <ErrorState title="Não foi possível carregar os caixas." message={list.error instanceof ApiError ? list.error.message : undefined} onRetry={() => list.refetch()} />
          : items.length === 0 ? <EmptyState title={EMPTY} description="Os turnos aparecem aqui quando os caixas sincronizam a abertura. Mude os filtros para ver outros períodos." />
          : (
            <div className="overflow-x-auto rounded-md border border-border">
              <table className="w-full text-sm">
                <caption className="sr-only">Sessões de caixa</caption>
                <thead className="bg-muted text-left">
                  <tr>
                    <th className="p-2">Situação</th><th className="p-2">Loja e caixa</th><th className="p-2">Operador</th><th className="p-2">Abertura</th><th className="p-2">Fechamento</th>
                    {list.data?.expectedVisible ? <th className="p-2 text-right">Quebra<Dica nome="Quebra de caixa" texto={BREAK_DICA} /></th> : null}
                    <th className="p-2"><span className="sr-only">Ação</span></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {items.map((s) => (
                    <tr key={s.id}>
                      <td className="p-2 align-top">
                        <div className="flex flex-col items-start gap-1">
                          <StatusChip tone={STATUS_TONE[s.status]}>{s.statusLabel}</StatusChip>
                          {s.awaitingSales ? <StatusChip tone="info">Aguardando envio das vendas</StatusChip> : null}
                          {s.reopenCount > 0 ? <StatusChip tone="neutral">Reaberto</StatusChip> : null}
                        </div>
                      </td>
                      <td className="p-2 align-top">{s.storeName} · Caixa {registerLabel(s.registerNumber)}</td>
                      <td className="p-2 align-top">{s.operatorName}</td>
                      <td className="p-2 align-top tabular-nums">{formatDateTime(s.openedAt)}</td>
                      <td className="p-2 align-top tabular-nums">{s.closedAt ? formatDateTime(s.closedAt) : '—'}</td>
                      {list.data?.expectedVisible ? <td className="p-2 text-right align-top tabular-nums">{s.totalDifference == null ? '—' : differenceText(s.totalDifference)}</td> : null}
                      <td className="p-2 text-right align-top">
                        <button type="button" onClick={() => setOpen(s.id)} className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold">Abrir</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {list.data?.truncated ? <p className="p-2 text-sm text-muted-foreground">Mostrando os {items.length} turnos mais recentes. Use os filtros para achar os outros.</p> : null}
            </div>
          )}
      </div>
      {open ? <SessionPanel id={open} onClose={() => setOpen(null)} /> : null}
    </>
  )
}

type Decision = 'approve' | 'reopen'

/** O quadro do fechamento (CA 7), o Pix a conferir (CA 13), aprovar (CA 7 e 8), reabrir (CA 10) e o relatório (CA 9). */
function SessionPanel({ id, onClose }: { id: string; onClose: () => void }) {
  const client = useQueryClient()
  const query = useGetCashSession(id)
  const [decision, setDecision] = useState<Decision | null>(null)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const s = query.data
  const canSeeFinance = useCan('finance.view')

  const refresh = async (next: CashSessionDetail) => {
    client.setQueryData(getGetCashSessionQueryKey(id), next)
    await client.invalidateQueries({ queryKey: getListCashSessionsQueryKey() })
  }

  const decide = async () => {
    if (!s || !decision) return false
    if (!reason.trim()) { setError('Informe o motivo.'); return false }
    setBusy(true)
    setError(null)
    try {
      const input = { reason: reason.trim(), version: s.version }
      await refresh(decision === 'approve' ? await approveCashSession(id, input) : await reopenCashSession(id, input))
      notifySuccess(decision === 'approve' ? 'Fechamento aprovado' : 'Caixa reaberto')
      setDecision(null)
      setReason('')
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar. Tente de novo.')
      return false
    } finally {
      setBusy(false)
    }
  }

  const checkPix = async (paymentId: string, status: PixCheckStatus) => {
    try {
      await refresh(await checkCashSessionPix(id, { paymentId, status }))
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível salvar a conferência. Tente de novo.')
    }
  }

  const download = async (copy: 'operator' | 'manager') => {
    try {
      await apiDownload(reportUrl(id, copy), `fechamento-caixa${copy === 'manager' ? '-gerente' : ''}.pdf`)
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível gerar o relatório. Tente de novo.')
    }
  }

  const footer = s ? (
    <div className="flex flex-wrap justify-end gap-2">
      <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
      <button type="button" onClick={() => void download('operator')} className="inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-semibold">
        <FileText aria-hidden="true" className="size-4" />Via do operador
      </button>
      {s.expectedVisible ? (
        <button type="button" onClick={() => void download('manager')} className="inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-semibold">
          <FileText aria-hidden="true" className="size-4" />Via do gerente
        </button>
      ) : null}
      {s.canApprove ? <button type="button" onClick={() => { setDecision('approve'); setError(null) }} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Aprovar fechamento</button> : null}
      {s.canReopen ? <button type="button" onClick={() => { setDecision('reopen'); setError(null) }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reabrir caixa</button> : null}
    </div>
  ) : null

  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={s ? `${s.storeName} · Caixa ${registerLabel(s.registerNumber)}` : 'Sessão de caixa'}
      description={s ? `${s.operatorName} · aberto em ${formatDateTime(s.openedAt)}` : undefined} footer={footer}>
      {query.isPending ? <Skeleton className="h-64" />
        : query.isError || !s ? <ErrorState title="Não foi possível carregar o caixa." message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => query.refetch()} />
        : (
          <div className="space-y-5">
            <div className="flex flex-wrap items-center gap-2">
              <StatusChip tone={STATUS_TONE[s.status]}>{s.statusLabel}</StatusChip>
              {s.awaitingSales ? <StatusChip tone="info">Aguardando envio das vendas</StatusChip> : null}
              {s.reviewReasons.map((r) => <StatusChip key={r} tone="warn">{r}</StatusChip>)}
            </div>
            {!s.expectedVisible ? <p className="rounded-md border border-border bg-muted p-3 text-sm">O esperado e a diferença ficam com quem aprova vendas.<Dica nome="Conferência cega" texto={BLIND_DICA} /></p> : null}
            <section aria-labelledby="quadro" className="space-y-2">
              <h3 id="quadro" className="font-semibold">Contagem por forma</h3>
              {s.lines.length === 0 ? <p className="text-sm text-muted-foreground">O caixa ainda não foi contado.</p> : (
                <div className="overflow-x-auto rounded-md border border-border">
                  <table className="w-full text-sm">
                    <thead className="bg-muted text-left">
                      <tr>
                        <th className="p-2">Forma</th>
                        {s.expectedVisible ? <th className="p-2 text-right">Esperado</th> : null}
                        <th className="p-2 text-right">Contado</th>
                        {s.expectedVisible ? <th className="p-2 text-right">Diferença</th> : null}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border">
                      {s.lines.map((l) => (
                        <tr key={l.paymentMethodId ?? 'dinheiro'}>
                          <td className="p-2">{l.methodName}</td>
                          {s.expectedVisible ? <td className="p-2 text-right tabular-nums">{l.expected == null ? '—' : formatMoney(l.expected)}</td> : null}
                          <td className="p-2 text-right tabular-nums">{formatMoney(l.counted)}{l.pixNotFound ? <span className="block text-xs text-muted-foreground">Pix não localizado: {formatMoney(l.pixNotFound)}</span> : null}</td>
                          {s.expectedVisible ? (
                            <td className="p-2 text-right tabular-nums">
                              {l.difference == null ? '—' : l.aboveTolerance ? <StatusChip tone="crit">{differenceText(l.difference)}</StatusChip> : differenceText(l.difference)}
                            </td>
                          ) : null}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              {s.expectedVisible ? <p className="text-xs text-muted-foreground">Tolerância da loja: {formatMoney(s.tolerance)} por forma. Sobra numa forma não compensa falta em outra.</p> : null}
            </section>
            <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-3">
              <div><dt className="text-muted-foreground">Vendas</dt><dd className="tabular-nums">{s.sales.sales}{s.sales.total != null ? ` · ${formatMoney(s.sales.total)}` : ''}</dd></div>
              <div><dt className="text-muted-foreground">Canceladas</dt><dd className="tabular-nums">{s.sales.cancelled}{s.sales.cancelledTotal ? ` · ${formatMoney(s.sales.cancelledTotal)}` : ''}</dd></div>
              <div><dt className="text-muted-foreground">Feitas sem rede</dt><dd className="tabular-nums">{s.sales.offline}</dd></div>
              {s.sales.discounts != null ? <div><dt className="text-muted-foreground">Descontos</dt><dd className="tabular-nums">{formatMoney(s.sales.discounts)}</dd></div> : null}
              <div><dt className="text-muted-foreground">Recolhimento ao cofre</dt><dd className="tabular-nums">{s.depositAmount == null ? '—' : formatMoney(s.depositAmount)}</dd></div>
              <div><dt className="text-muted-foreground">Fundo que fica</dt><dd className="tabular-nums">{s.keptAmount == null ? '—' : formatMoney(s.keptAmount)}</dd></div>
            </dl>
            {s.offline ? (
              <section aria-labelledby="sem-rede" className="space-y-1 rounded-md border border-border p-3 text-sm">
                <h3 id="sem-rede" className="font-semibold">Sem rede</h3>
                <p>
                  {[s.offline.openedOffline ? 'Aberto sem rede' : null, s.offline.closedOffline ? 'contado sem rede' : null,
                    `${s.offline.approvals} ${s.offline.approvals === 1 ? 'aprovação' : 'aprovações'} de supervisor sem rede (${formatMoney(s.offline.approvalsAmount)})`]
                    .filter(Boolean).join(' · ')}.
                </p>
                {s.offline.sales.length > 0
                  ? <p className="tabular-nums">Vendas feitas sem rede: {s.offline.sales.join(', ')}</p>
                  : <p className="text-muted-foreground">Nenhuma venda feita sem rede.</p>}
              </section>
            ) : null}
            <section aria-labelledby="movimentos" className="space-y-2">
              <h3 id="movimentos" className="font-semibold">Fundo, sangrias, suprimentos e recolhimento</h3>
              <ul className="divide-y divide-border rounded-md border border-border text-sm">
                {s.movements.map((m) => (
                  <li key={m.id} className="flex flex-wrap items-baseline justify-between gap-2 p-2">
                    <span>{m.kindLabel}{m.reason ? ` · ${m.reason}` : ''} <span className="text-muted-foreground">({m.operatorName}, {formatDateTime(m.occurredAt)})</span>
                      {m.aboveExpected ? <StatusChip tone="warn">Acima do calculado</StatusChip> : null}</span>
                    <span className="tabular-nums">{formatMoney(m.amount)}</span>
                  </li>
                ))}
              </ul>
            </section>
            {s.pix.length > 0 ? (
              <section aria-labelledby="pix" className="space-y-2">
                <h3 id="pix" className="font-semibold">Pix conferido pelo operador<Dica nome="Pix a conferir" texto="O operador confirmou estes Pix olhando o celular do cliente. Confira cada um no extrato do banco; o não localizado vira quebra da forma Pix." /></h3>
                <ul className="divide-y divide-border rounded-md border border-border text-sm">
                  {s.pix.map((p) => (
                    <li key={p.paymentId} className="flex flex-wrap items-center justify-between gap-2 p-2">
                      <span className="tabular-nums">{p.confirmedAt ? formatDateTime(p.confirmedAt) : '—'} · {formatMoney(p.amount)} · {p.operatorName ?? '—'} · Venda {p.saleNumber}</span>
                      <span className="flex gap-2">
                        {p.status ? <StatusChip tone={p.status === 'Found' ? 'ok' : 'crit'}>{p.status === 'Found' ? 'Conferido' : 'Não localizado'}</StatusChip> : null}
                        {/* Confere quem pode aprovar este fechamento (não quem operou ou contou), antes de fechar; no fechado, reabra. */}
                        {s.canApprove ? (
                          <>
                            <button type="button" onClick={() => void checkPix(p.paymentId, 'Found')} className="rounded-md border border-border px-2 py-1 font-semibold">Conferido</button>
                            <button type="button" onClick={() => void checkPix(p.paymentId, 'NotFound')} className="rounded-md border border-border px-2 py-1 font-semibold">Não localizado</button>
                          </>
                        ) : null}
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}
            {/* CX-03: o que o fechamento lançou no Financeiro, só para quem vê o Financeiro. */}
            {canSeeFinance && (s.status === 'Closed' || s.reopenCount > 0) ? <CashSessionFinance sessionId={id} /> : null}
            {s.approvalReason ? <p className="text-sm">Aprovado por {s.approvedByName}{s.approvedAt ? ` em ${formatDateTime(s.approvedAt)}` : ''}: {s.approvalReason}</p> : null}
            {s.reopenCount > 0 && s.reopenReason ? <p className="text-sm">Reaberto por {s.reopenedByName}{s.reopenedAt ? ` em ${formatDateTime(s.reopenedAt)}` : ''}: {s.reopenReason}</p> : null}
            {decision ? (
              <div className="space-y-2 rounded-md border border-border p-3">
                <p className="text-sm font-semibold">{decision === 'approve' ? 'Aprovar o fechamento com a quebra como está?' : 'Reabrir o caixa? O operador volta a vender e conta de novo; o Proprietário é avisado.'}</p>
                <Field label="Motivo" required>
                  {(p) => <textarea {...p} value={reason} maxLength={300} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
                </Field>
                <FormError message={error ?? undefined} />
                <div className="flex justify-end gap-2">
                  <button type="button" onClick={() => setDecision(null)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Cancelar</button>
                  <button type="button" disabled={busy} onClick={() => void decide()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">
                    {busy ? 'Salvando…' : decision === 'approve' ? 'Aprovar fechamento' : 'Reabrir caixa'}
                  </button>
                </div>
              </div>
            ) : null}
          </div>
        )}
    </SidePanel>
  )
}
