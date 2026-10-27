import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { ShieldCheck } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { getListWebhookEventsQueryKey, reprocessWebhookEvent, useGetWebhookEventContent, useListWebhookEvents } from '@/api/generated/bemaqui'
import type { WebhookEventRow } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters, useTableSearch } from '@/components/data-table/table-search'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { IntegrationsNav } from './IntegrationsNav'
import { CONTENT_HINT, defaultStatus, EVENT_STATUS, STALLED_HINT, STATUS_OPTIONS } from './events'

function Status({ row }: { row: WebhookEventRow }) {
  const s = EVENT_STATUS[row.status] ?? { label: row.status, tone: 'neutral' as const }
  return (
    <span className="inline-flex items-center">
      <StatusChip tone={s.tone}>{s.label}</StatusChip>
      {row.status === 'DeadLettered' ? <Dica texto={STALLED_HINT} nome="Parado" /> : null}
    </span>
  )
}

/** Painel "Ver conteúdo" (CA 15): JSON mascarado, nunca em cache, com a faixa de dados mascarados. */
function ContentPanel({ id, onClose }: { id: string; onClose: () => void }) {
  const content = useGetWebhookEventContent(id, { query: { gcTime: 0, staleTime: 0, retry: false } })
  return (
    <SidePanel open onOpenChange={(open) => { if (!open) onClose() }} title="Conteúdo do aviso" description={content.data ? `${content.data.provider} · ${content.data.eventType}` : undefined}>
      <p role="note" className="mb-3 flex items-center gap-2 rounded-md bg-info-soft px-3 py-2 text-sm font-semibold text-info-ink">
        <ShieldCheck aria-hidden="true" className="size-4" />Dados pessoais mascarados
      </p>
      {content.isPending ? <Skeleton className="h-40" /> : content.isError ? (
        <ErrorState message={content.error instanceof ApiError ? content.error.message : undefined} onRetry={() => content.refetch()} />
      ) : content.data.purged ? (
        <p className="text-sm text-muted-foreground">O conteúdo foi apagado depois de 90 dias. Ficaram o provedor, o tipo, as datas e a situação.</p>
      ) : (
        <pre aria-label="Conteúdo mascarado" className="max-h-[60dvh] overflow-auto whitespace-pre-wrap break-all rounded-md border border-border bg-muted p-3 text-xs tabular-nums">{content.data.maskedJson}</pre>
      )}
    </SidePanel>
  )
}

/**
 * Configurações → Integrações → Eventos recebidos (FND-16 CA 15 e 16): provedor, tipo, recebido em e situação, com tentativas e
 * erro na linha expansível; filtros na URL; "Reprocessar" para o parado (sem reenvio enquanto processa) e "Ver conteúdo" só com a
 * aprovação do módulo dono. Com algum parado, abre no filtro "Parados".
 */
export function IntegrationEventsPage() {
  const [search] = useTableSearch()
  const { provedor, situacao, de, ate } = useTableFilters(['provedor', 'situacao', 'de', 'ate'])
  const navigate = useNavigate()
  const client = useQueryClient()
  const [viewing, setViewing] = useState<string>()
  const applied = useRef(false)
  const setSearch = (patch: Record<string, string | undefined>) =>
    void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ...prev, ...patch, page: undefined }) } as never)
  const query = useListWebhookEvents({ provider: provedor || undefined, status: situacao || undefined, from: de || undefined, to: ate || undefined, page: search.page, pageSize: search.pageSize })

  const deadLettered = query.data?.deadLettered
  useEffect(() => {
    if (deadLettered === undefined) return
    const next = defaultStatus(situacao, deadLettered, applied.current)
    applied.current = true
    if (next) void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ...prev, situacao: next, page: undefined }) } as never)
  }, [deadLettered, situacao, navigate])

  const reprocess = async (row: WebhookEventRow) => {
    await reprocessWebhookEvent(row.id)
    await client.invalidateQueries({ queryKey: getListWebhookEventsQueryKey() })
  }

  const columns: ColumnDef<WebhookEventRow>[] = [
    { id: 'provider', header: 'Provedor', cell: ({ row: { original: r } }) => r.providerName },
    { id: 'eventType', header: 'Tipo', cell: ({ row: { original: r } }) => <span className="break-all">{r.eventType}</span> },
    { id: 'receivedAt', header: 'Recebido em', cell: ({ row: { original: r } }) => <span className="tabular-nums">{formatDateTime(r.receivedAt)}</span> },
    { id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => <Status row={r} /> },
    { id: 'attempts', header: 'Tentativas', meta: { secondary: true, numeric: true }, cell: ({ row: { original: r } }) => r.attempts },
    { id: 'lastError', header: 'Erro', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.lastError ?? '—' },
    {
      id: 'actions', header: () => <span className="sr-only">Ações</span>, cell: ({ row: { original: r } }) => (
        <span className="flex flex-wrap justify-end gap-2">
          {r.canViewContent ? (
            <span className="inline-flex items-center">
              <button type="button" onClick={() => setViewing(r.id)} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Ver conteúdo</button>
              <Dica texto={CONTENT_HINT} nome="Ver conteúdo" />
            </span>
          ) : null}
          {r.canReprocess ? (
            <ConfirmDialog verb="Reprocessar" done="Evento reenviado para processamento" title={`Reprocessar o aviso de ${r.providerName}?`}
              description="O aviso volta para a fila e é processado de novo. Confira antes se a causa do erro foi corrigida."
              onConfirm={() => reprocess(r)}
              trigger={<button type="button" className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">Reprocessar</button>} />
          ) : null}
        </span>
      ),
    },
  ]

  return (
    <>
      <PageHeading title="Avisos recebidos de sistemas externos">
        <p className="mt-2 text-muted-foreground">Os avisos que a nota fiscal e o banco mandam ao BemAqui, na ordem em que chegaram. O conteúdo não aparece na lista.</p>
      </PageHeading>
      <SettingsTabs />
      <IntegrationsNav />
      <div role="group" aria-label="Período" className="mb-3 flex flex-wrap items-end gap-3 text-sm">
        <label className="space-y-1 font-medium">De<DateInput value={de ?? ''} onChange={(v) => setSearch({ de: v || undefined })} /></label>
        <label className="space-y-1 font-medium">Até<DateInput value={ate ?? ''} onChange={(v) => setSearch({ ate: v || undefined })} /></label>
        {de || ate ? <button type="button" onClick={() => setSearch({ de: undefined, ate: undefined })} className="min-h-11 font-semibold text-primary">Limpar período</button> : null}
      </div>
      <DataTable
        label="Eventos recebidos"
        columns={columns}
        query={{ ...query, data: query.data?.page }}
        getRowId={(r) => r.id}
        searchable={false}
        filters={[
          { key: 'provedor', label: 'Provedor', options: (query.data?.providers ?? []).map((p) => ({ value: p.code, label: p.name })) },
          { key: 'situacao', label: 'Situação', options: STATUS_OPTIONS },
        ]}
        rowClassName={(r) => (r.status === 'DeadLettered' ? 'border-l-4 border-l-crit' : r.status === 'Failed' ? 'border-l-4 border-l-warn' : undefined)}
        expandable={{ label: (r) => `aviso de ${r.providerName} recebido em ${formatDateTime(r.receivedAt)}`, render: (r) => (
          <p className="text-sm"><span className="tabular-nums">{r.attempts} {r.attempts === 1 ? 'tentativa' : 'tentativas'}</span>{r.lastError ? ` · ${r.lastError}` : ''}</p>
        ) }}
        empty={{ title: 'Nenhum aviso recebido ainda.', description: 'Os avisos aparecem aqui quando a nota fiscal ou o banco responderem.' }}
        filteredEmpty={situacao === 'DeadLettered' ? { title: 'Nenhum aviso parado.', description: 'Tudo o que chegou foi processado ou está na fila.' } : undefined}
      />
      {viewing ? <ContentPanel id={viewing} onClose={() => setViewing(undefined)} /> : null}
    </>
  )
}
