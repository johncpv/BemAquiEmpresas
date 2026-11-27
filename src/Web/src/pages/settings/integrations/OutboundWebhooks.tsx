import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useEffect, useRef, useState } from 'react'
import {
  getGetOutboundDeliveryQueryKey,
  getGetOutboundWebhookSummaryQueryKey,
  getListOutboundDeliveriesQueryKey,
  resendOutboundDelivery,
  rotateOutboundSecret,
  testOutboundSubscription,
  useGetOutboundDelivery,
  useGetOutboundWebhookSummary,
  useListOutboundDeliveries,
} from '@/api/generated/bemaqui'
import type { OutboundDeliveryRow } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { defaultStatus } from './events'
import { DELIVERY_STATUS, DELIVERY_STATUS_OPTIONS, HELD_HINT, healthState, PURGED_HINT, SKIPPED_HINT, STALLED_HINT } from './outbound'

const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'min-h-11 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'
const SENT_LINK = '/app/configuracoes/integracoes/eventos'

function DeliveryStatus({ status }: { status: string }) {
  const s = DELIVERY_STATUS[status] ?? { label: status, tone: 'neutral' as const }
  const hint = status === 'Held' ? HELD_HINT : status === 'Skipped' ? SKIPPED_HINT : status === 'DeadLettered' ? STALLED_HINT : null
  return (
    <span className="inline-flex items-center">
      <StatusChip tone={s.tone}>{s.label}</StatusChip>
      {hint ? <Dica texto={hint} nome={s.label} /> : null}
    </span>
  )
}

/** Resumo de saúde dos avisos na página do Conecta (INT-02 CA 19), com o caminho para os parados e o registro detalhado. */
export function OutboundHealthSection() {
  const summary = useGetOutboundWebhookSummary({ query: { retry: false } })
  if (summary.isPending) return <Skeleton className="h-20" />
  if (summary.isError) return <ErrorState title="Não foi possível carregar a situação dos avisos." onRetry={() => void summary.refetch()} />
  const state = healthState(summary.data)
  if (!state) return null
  return (
    <section aria-labelledby="conecta-avisos" className={'rounded-md border border-border bg-card p-4 ' + (state.tone === 'crit' ? 'border-l-4 border-l-crit' : '')}>
      <div className="flex flex-wrap items-center gap-2">
        <h2 id="conecta-avisos" className="text-base font-semibold">Avisos ao Conecta</h2>
        <StatusChip tone={state.tone}>{state.tone === 'ok' ? 'Funcionando' : 'Parado'}</StatusChip>
        <Dica texto="O ERP avisa o Conecta quando um pedido é pago, um boleto é emitido, pago ou vence, ou uma nota sai. O Conecta usa o aviso para mandar a mensagem ao cliente." />
      </div>
      <p className="mt-2 text-sm">{state.text}</p>
      {summary.data.lastSuccessAt ? <p className="mt-1 text-sm tabular-nums text-muted-foreground">Último aviso entregue em {formatDateTime(summary.data.lastSuccessAt)}</p> : null}
      <div className="mt-3 flex flex-wrap gap-2">
        {summary.data.deadLettered > 0 ? (
          <Link to={SENT_LINK} search={{ aba: 'enviados', situacao: 'DeadLettered' } as never} className={primary + ' inline-flex items-center'}>Reenviar os parados</Link>
        ) : null}
        <Link to={SENT_LINK} search={{ aba: 'enviados' } as never} className={secondary + ' inline-flex items-center'}>Avançado</Link>
      </div>
    </section>
  )
}

/** Painel do aviso (CA 15): o corpo e cada tentativa, com "Reenviar". */
function DeliveryPanel({ id, onClose, onResend }: { id: string; onClose: () => void; onResend: (row: OutboundDeliveryRow) => Promise<unknown> }) {
  const detail = useGetOutboundDelivery(id, { query: { gcTime: 0, staleTime: 0, retry: false } })
  return (
    <SidePanel open onOpenChange={(open) => { if (!open) onClose() }} title="Aviso enviado" description={detail.data ? `${detail.data.delivery.typeLabel} · ${detail.data.delivery.type}` : undefined}>
      {detail.isPending ? <Skeleton className="h-40" /> : detail.isError ? (
        <ErrorState message={detail.error instanceof ApiError ? detail.error.message : undefined} onRetry={() => detail.refetch()} />
      ) : (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <DeliveryStatus status={detail.data.delivery.status} />
            <ResendButton row={detail.data.delivery} onResend={onResend} />
          </div>
          <section aria-labelledby="aviso-tentativas">
            <h3 id="aviso-tentativas" className="text-sm font-semibold">Tentativas</h3>
            {detail.data.attempts.length === 0 ? <p className="text-sm text-muted-foreground">Ainda não houve envio.</p> : (
              <ol className="mt-1 space-y-1 text-sm">
                {detail.data.attempts.map((a) => (
                  <li key={`${a.number}-${a.at}`} className="tabular-nums">
                    {formatDateTime(a.at)} · {a.statusCode ? `HTTP ${a.statusCode}` : 'sem resposta'} · {a.durationMs} ms{a.error ? ` · ${a.error}` : ''}
                  </li>
                ))}
              </ol>
            )}
          </section>
          <section aria-labelledby="aviso-corpo">
            <h3 id="aviso-corpo" className="text-sm font-semibold">Conteúdo enviado</h3>
            {detail.data.body ? (
              <pre aria-label="Conteúdo do aviso" className="mt-1 max-h-[50dvh] overflow-auto whitespace-pre-wrap break-all rounded-md border border-border bg-muted p-3 text-xs tabular-nums">{detail.data.body}</pre>
            ) : <p className="text-sm text-muted-foreground">{PURGED_HINT}. Ficaram o tipo, as datas, a situação e o código.</p>}
          </section>
        </div>
      )}
    </SidePanel>
  )
}

function ResendButton({ row, onResend }: { row: OutboundDeliveryRow; onResend: (row: OutboundDeliveryRow) => Promise<unknown> }) {
  if (row.status !== 'Delivered' && row.status !== 'DeadLettered') return null
  return (
    <Can perm="conecta.edit">
      {row.contentPurged ? (
        <span className="inline-flex items-center">
          <button type="button" disabled className={secondary}>Reenviar</button>
          <Dica texto={PURGED_HINT} nome="Reenviar" />
        </span>
      ) : (
        <ConfirmDialog verb="Reenviar" done="Reenviado" title={`Reenviar o aviso "${row.typeLabel}"?`}
          description="O aviso sai de novo com o mesmo id e o mesmo conteúdo. O Conecta reconhece o id e não faz nada duas vezes."
          onConfirm={() => onResend(row)} trigger={<button type="button" className={secondary}>Reenviar</button>} />
      )}
    </Can>
  )
}

/** Cabeçalho do "Avançado": "Enviar teste" e "Trocar segredo" (CA 6 e 12), com <c>conecta.edit</c>. */
function SentHeader({ subscriptionId, onChanged }: { subscriptionId: string; onChanged: () => Promise<unknown> }) {
  const [immediate, setImmediate] = useState(false)
  return (
    <Can perm="conecta.edit">
      <div className="mb-3 flex flex-wrap gap-2">
        <ConfirmDialog verb="Enviar teste" done="Teste enviado" title="Enviar um aviso de teste ao Conecta?"
          description="Sai um aviso do tipo ping pela mesma fila. O resultado aparece na lista em alguns segundos."
          onConfirm={async () => { await testOutboundSubscription(subscriptionId); await onChanged() }}
          trigger={<button type="button" className={primary}>Enviar teste</button>} />
        <ConfirmDialog verb="Trocar segredo" done="Segredo trocado" title="Trocar o segredo de assinatura dos avisos?"
          description="O Conecta recebe o segredo novo sozinho, na próxima renovação do acesso (até 15 minutos). O anterior continua valendo por 24 horas."
          onOpen={() => setImmediate(false)}
          onConfirm={async () => { await rotateOutboundSecret(subscriptionId, { immediate }); await onChanged() }}
          trigger={<button type="button" className={secondary}>Trocar segredo</button>}>
          <label className="flex min-h-11 items-center gap-2 text-sm">
            <input type="checkbox" checked={immediate} onChange={(e) => setImmediate(e.target.checked)} className="size-5" />
            Invalidar o anterior agora (suspeita de vazamento)
          </label>
          {immediate ? <p className="text-sm text-muted-foreground">Os avisos vão falhar e tentar de novo até o Conecta renovar o acesso.</p> : null}
        </ConfirmDialog>
      </div>
    </Can>
  )
}

/**
 * Aba "Enviados" (INT-02 CA 11, 15 e 19): tipo, situação e hora, com tentativas e erro na linha expansível; filtros na URL; com algum parado,
 * abre no filtro "Parados"; painel do aviso com o corpo e as tentativas.
 */
export function SentEventsTab() {
  const [search] = useTableSearch()
  const { tipo, situacao, de, ate } = useTableFilters(['tipo', 'situacao', 'de', 'ate'])
  const navigate = useNavigate()
  const client = useQueryClient()
  const [viewing, setViewing] = useState<string>()
  const applied = useRef(false)
  const summary = useGetOutboundWebhookSummary({ query: { retry: false } })
  const query = useListOutboundDeliveries({ type: tipo || undefined, status: situacao || undefined, from: de || undefined, to: ate || undefined, page: search.page, pageSize: search.pageSize })
  const setSearch = (patch: Record<string, string | undefined>) =>
    void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ...prev, ...patch, page: undefined }) } as never)

  const deadLettered = query.data?.deadLettered
  useEffect(() => {
    if (deadLettered === undefined) return
    const next = defaultStatus(situacao, deadLettered, applied.current)
    applied.current = true
    if (next) void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ...prev, situacao: next, page: undefined }) } as never)
  }, [deadLettered, situacao, navigate])

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getListOutboundDeliveriesQueryKey() })
    await client.invalidateQueries({ queryKey: getGetOutboundWebhookSummaryQueryKey() })
  }

  const resend = async (row: OutboundDeliveryRow) => {
    await resendOutboundDelivery(row.id)
    await client.invalidateQueries({ queryKey: getGetOutboundDeliveryQueryKey(row.id) })
    await refresh()
  }

  if (summary.isSuccess && !summary.data.connected) {
    return (
      <EmptyState title="Conecte o Conecta para enviar avisos" description="Os avisos de pedido, boleto e nota vão ao Conecta depois que ele estiver conectado."
        action={<Link to="/app/configuracoes/integracoes/conecta" className={primary + ' inline-flex items-center'}>Abrir o Conecta</Link>} />
    )
  }

  const columns: ColumnDef<OutboundDeliveryRow>[] = [
    { id: 'type', header: 'Tipo', cell: ({ row: { original: r } }) => <button type="button" onClick={() => setViewing(r.id)} className="min-h-11 text-left font-semibold text-primary">{r.typeLabel}</button> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => <DeliveryStatus status={r.status} /> },
    { id: 'createdAt', header: 'Hora', cell: ({ row: { original: r } }) => <span className="tabular-nums">{formatDateTime(r.createdAt)}</span> },
    { id: 'attempts', header: 'Tentativas', meta: { secondary: true, numeric: true }, cell: ({ row: { original: r } }) => r.attempts },
    { id: 'lastError', header: 'Erro', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.lastError ?? '—' },
    { id: 'actions', header: () => <span className="sr-only">Ações</span>, cell: ({ row: { original: r } }) => <span className="flex justify-end"><ResendButton row={r} onResend={resend} /></span> },
  ]

  return (
    <>
      {summary.data?.subscriptionId ? <SentHeader subscriptionId={summary.data.subscriptionId} onChanged={refresh} /> : null}
      <div role="group" aria-label="Período" className="mb-3 flex flex-wrap items-end gap-3 text-sm">
        <label className="space-y-1 font-medium">De<DateInput value={de ?? ''} onChange={(v) => setSearch({ de: v || undefined })} /></label>
        <label className="space-y-1 font-medium">Até<DateInput value={ate ?? ''} onChange={(v) => setSearch({ ate: v || undefined })} /></label>
        {de || ate ? <button type="button" onClick={() => setSearch({ de: undefined, ate: undefined })} className="min-h-11 font-semibold text-primary">Limpar período</button> : null}
      </div>
      <DataTable
        label="Avisos enviados"
        columns={columns}
        query={{ ...query, data: query.data?.page }}
        getRowId={(r) => r.id}
        searchable={false}
        filters={[
          { key: 'tipo', label: 'Tipo', options: (query.data?.types ?? []).map((t) => ({ value: t.type, label: t.label })) },
          { key: 'situacao', label: 'Situação', options: DELIVERY_STATUS_OPTIONS },
        ]}
        rowClassName={(r) => (r.status === 'DeadLettered' ? 'border-l-4 border-l-crit' : r.status === 'Retrying' ? 'border-l-4 border-l-warn' : undefined)}
        expandable={{ label: (r) => `aviso ${r.typeLabel} de ${formatDateTime(r.createdAt)}`, render: (r) => (
          <p className="text-sm"><span className="tabular-nums">{r.attempts} {r.attempts === 1 ? 'tentativa' : 'tentativas'}</span>{r.lastError ? ` · ${r.lastError}` : ''}</p>
        ) }}
        empty={{ title: 'Nenhum aviso enviado ainda.', description: 'Eles aparecem aqui quando um pedido, boleto ou nota mudar.' }}
        filteredEmpty={situacao === 'DeadLettered' ? { title: 'Nenhum aviso parado.', description: 'Tudo o que saiu foi entregue ou está na fila.' } : undefined}
      />
      {viewing ? <DeliveryPanel id={viewing} onClose={() => setViewing(undefined)} onResend={resend} /> : null}
    </>
  )
}
