import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { type FormEvent, useEffect, useState } from 'react'
import { getExportLotTraceUrl, useGetLotTrace, useGetSerialTrace, useSearchTraceability } from '@/api/generated/bemaqui'
import type { LotTrace, SerialTrace, TraceSearchResult } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { TreeTable } from '@/components/ui/tree-table'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { TRACE_SERIAL_STATUS, TRACE_TEXT, type TraceNode, expiryChip, lotTree, reconciliationText, serialTree, todaySaoPaulo } from './traceability'

type TraceSearch = { q?: string; item?: string; serie?: string; lote?: string }

/** Rótulo do nó com o link, o chip, a Dica e, a 400 px, o detalhe e a quantidade embaixo (as colunas somem). */
function NodeLabel({ node }: { node: TraceNode }) {
  return (
    <span className="inline-flex flex-col">
      <span className="inline-flex flex-wrap items-center gap-1.5">
        <span className="font-semibold">{node.label}</span>
        {node.dica === 'lotesDaOs' ? <Dica nome="Lotes da OS" texto={TRACE_TEXT.dicaLotesDaOs} /> : null}
        {node.dica === 'iniciais' ? <Dica nome="Iniciais do cliente" texto={TRACE_TEXT.dicaIniciais} /> : null}
        {node.chip ? <StatusChip tone={node.chip.tone}>{node.chip.label}</StatusChip> : null}
        {node.link ? (
          <Link to={node.link.to as never} params={node.link.params as never} search={node.link.search as never} className="text-xs font-semibold text-primary underline">{node.link.label}</Link>
        ) : null}
      </span>
      {node.detail ? <span className="text-xs text-muted-foreground tabular-nums">{node.detail}</span> : null}
      {node.quantity ? <span className="text-xs tabular-nums sm:hidden">{node.quantity}</span> : null}
    </span>
  )
}

function Tree({ nodes, label, id }: { nodes: TraceNode[]; label: string; id: string }) {
  return (
    <TreeTable key={id} nodes={nodes} label={label} labelHeader="Rastro" searchable={false} defaultExpanded="all" renderLabel={(n) => <NodeLabel node={n} />}
      columns={[{ header: 'Quantidade', numeric: true, className: 'hidden sm:table-cell whitespace-nowrap', cell: (n) => n.quantity ?? '' }]} />
  )
}

function SerialView({ trace }: { trace: SerialTrace }) {
  const status = TRACE_SERIAL_STATUS[trace.status]
  return (
    <section aria-label={`Série ${trace.serialNumber}`} className="space-y-3">
      <header className="flex flex-wrap items-center gap-2">
        <h2 className="font-display text-lg font-bold tabular-nums">Série {trace.serialNumber}</h2>
        <StatusChip tone={status.tone}>{status.label}</StatusChip>
        <span className="text-sm text-muted-foreground">{trace.itemCode} {trace.itemName}</span>
      </header>
      <Tree id={trace.serialId} label={`De onde veio a série ${trace.serialNumber}`} nodes={serialTree(trace, todaySaoPaulo())} />
    </section>
  )
}

function LotView({ trace }: { trace: LotTrace }) {
  const expiry = expiryChip(trace.expiryState, trace.daysToExpiry)
  const [error, setError] = useState<string | null>(null)
  const exportCsv = async () => {
    setError(null)
    try {
      await apiDownload(getExportLotTraceUrl(trace.itemId, trace.lotId), `rastreio-lote-${trace.lotNumber}.csv`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível exportar. Tente de novo.')
    }
  }
  const exportButton = <button type="button" onClick={() => void exportCsv()} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Exportar CSV</button>
  return (
    <section aria-label={`Lote ${trace.lotNumber}`} className="space-y-3">
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="font-display text-lg font-bold tabular-nums">Lote {trace.lotNumber}</h2>
            {expiry ? <StatusChip tone={expiry.tone}>{expiry.label}</StatusChip> : null}
          </div>
          <p className="text-sm text-muted-foreground">
            <Link to={'/app/estoque/itens/$id' as never} params={{ id: trace.itemId } as never} search={{ aba: 'lotes' } as never} className="font-semibold text-primary underline">{trace.itemCode} {trace.itemName}</Link>
            {trace.supplierName ? ` · Fornecedor ${trace.supplierName}` : ' · Sem fornecedor'}
            {trace.expiry ? ` · Validade ${formatDate(trace.expiry)}` : ''}
          </p>
        </div>
        <div className="hidden sm:block">{exportButton}</div>
        {/* 400 px: exportar fica no menu (padrão 8). */}
        <details className="relative sm:hidden">
          <summary className="cursor-pointer rounded-md border border-border px-3 py-2 text-sm font-semibold">Mais ações</summary>
          <div className="absolute right-0 z-10 mt-1 rounded-md border border-border bg-card p-2 shadow">{exportButton}</div>
        </details>
      </header>
      {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
      <p className="text-sm tabular-nums" aria-live="polite">
        <StatusChip tone={trace.balanced ? 'ok' : 'crit'}>{trace.balanced ? 'Conta fechada' : 'Diferença na conta'}</StatusChip>{' '}
        {reconciliationText(trace)}
      </p>
      <Tree id={trace.lotId} label={`Para onde foi o lote ${trace.lotNumber}`} nodes={lotTree(trace)} />
    </section>
  )
}

/** Resultado da busca com mais de um acerto: séries primeiro se o termo tem o formato da série. */
function Hits({ result, onSerial, onLot }: { result: TraceSearchResult; onSerial: (item: string, id: string) => void; onLot: (item: string, id: string) => void }) {
  const serials = (
    <section key="s" aria-label="Séries encontradas" className="space-y-1">
      <h2 className="text-sm font-semibold text-muted-foreground">Séries ({result.serials.length})</h2>
      <ul className="divide-y divide-border rounded-md border border-border bg-card">
        {result.serials.map((s) => (
          <li key={s.id}>
            <button type="button" onClick={() => onSerial(s.itemId, s.id)} className="flex w-full flex-wrap items-center gap-2 px-3 py-2 text-left text-sm hover:bg-muted">
              <span className="font-semibold tabular-nums">{s.serialNumber}</span>
              <StatusChip tone={TRACE_SERIAL_STATUS[s.status].tone}>{TRACE_SERIAL_STATUS[s.status].label}</StatusChip>
              <span className="text-muted-foreground">{s.itemCode} {s.itemName}</span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
  const lots = (
    <section key="l" aria-label="Lotes encontrados" className="space-y-1">
      <h2 className="text-sm font-semibold text-muted-foreground">Lotes ({result.lots.length})</h2>
      <ul className="divide-y divide-border rounded-md border border-border bg-card">
        {result.lots.map((l) => (
          <li key={l.id}>
            <button type="button" onClick={() => onLot(l.itemId, l.id)} className="flex w-full flex-wrap items-center gap-2 px-3 py-2 text-left text-sm hover:bg-muted">
              <span className="font-semibold tabular-nums">Lote {l.lotNumber}</span>
              <span className="text-muted-foreground">{l.itemCode} {l.itemName}{l.expiry ? ` · validade ${formatDate(l.expiry)}` : ''}</span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
  const parts = [result.serials.length > 0 ? serials : null, result.lots.length > 0 ? lots : null]
  return <div className="space-y-4">{result.looksLikeSerial ? parts : parts.reverse()}</div>
}

function Loading() {
  return <div className="space-y-2" aria-busy="true" aria-label="Carregando"><Skeleton className="h-8 w-1/2" /><Skeleton className="h-64" /></div>
}

function errorMessage(error: unknown) {
  return error instanceof ApiError && error.status === 404 ? 'Ela pode ter sido apagada ou ser de outro item. Busque de novo.' : undefined
}

/**
 * Estoque → Rastreabilidade (EST-04): busca única "Série ou lote" (exata ou por prefixo, até 50), série → origem (OS, projeto, cliente,
 * romaneio e os lotes da OS com fornecedor e recebimento) e lote → destino (entradas, saldo, consumo por OS com as séries e os romaneios,
 * outras saídas e a conta fechada), em árvore acessível (treegrid). Termo e escolha ficam na URL; um só acerto abre direto.
 */
export function TraceabilityPage() {
  const search = useSearch({ strict: false }) as TraceSearch
  const navigate = useNavigate()
  const [term, setTerm] = useState(search.q ?? '')
  const q = (search.q ?? '').trim()
  const serialId = search.item && search.serie ? search.serie : undefined
  const lotId = search.item && search.lote ? search.lote : undefined
  const results = useSearchTraceability({ q }, { query: { enabled: q.length > 0 && !serialId && !lotId } })
  const serial = useGetSerialTrace(search.item ?? '', serialId ?? '', { query: { enabled: !!serialId } })
  const lot = useGetLotTrace(search.item ?? '', lotId ?? '', { query: { enabled: !!lotId } })
  const go = (next: TraceSearch, replace = false) => void navigate({ to: '/app/estoque/rastreabilidade' as never, search: next as never, replace })

  useEffect(() => setTerm(search.q ?? ''), [search.q])
  // Propositivo: um só acerto abre direto o rastro.
  useEffect(() => {
    const data = results.data
    if (!data || serialId || lotId) return
    if (data.serials.length + data.lots.length !== 1) return
    if (data.serials[0]) go({ q, item: data.serials[0].itemId, serie: data.serials[0].id }, true)
    else if (data.lots[0]) go({ q, item: data.lots[0].itemId, lote: data.lots[0].id }, true)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [results.data])

  const submit = (e: FormEvent) => {
    e.preventDefault()
    go({ q: term.trim() || undefined })
  }

  let body
  if (serialId) {
    body = serial.isPending ? <Loading /> : serial.isError || !serial.data
      ? <ErrorState title="Não foi possível carregar a série." message={errorMessage(serial.error)} onRetry={() => serial.refetch()} />
      : <SerialView trace={serial.data} />
  } else if (lotId) {
    body = lot.isPending ? <Loading /> : lot.isError || !lot.data
      ? <ErrorState title="Não foi possível carregar o lote." message={errorMessage(lot.error)} onRetry={() => lot.refetch()} />
      : <LotView trace={lot.data} />
  } else if (!q) {
    body = <EmptyState title={TRACE_TEXT.inicial} description="Série no formato código-ano-sequência (ELV-0001-2026-00001) ou o número do lote. Vale o começo do número." />
  } else if (results.isPending) {
    body = <Loading />
  } else if (results.isError || !results.data) {
    const invalid = results.error instanceof ApiError && results.error.status === 422
    body = <ErrorState title={invalid ? 'Termo longo demais.' : 'Não foi possível buscar.'} message={invalid ? 'Digite até 60 caracteres.' : 'Confira a conexão e tente de novo.'} onRetry={invalid ? undefined : () => results.refetch()} />
  } else if (results.data.serials.length + results.data.lots.length === 0) {
    body = <EmptyState title={TRACE_TEXT.semResultado(q)} description="Confira o número. A busca é pelo número exato ou pelo começo dele." action={<Link to={'/app/estoque/saldos' as never} className="text-sm font-semibold text-primary">Ver saldos</Link>} />
  } else {
    body = <Hits result={results.data} onSerial={(item, id) => go({ q, item, serie: id })} onLot={(item, id) => go({ q, item, lote: id })} />
  }

  return (
    <>
      <PageHeading title="Estoque: Rastreabilidade">
        <p className="mt-2 text-muted-foreground">De qual lote e fornecedor veio cada equipamento, e em quais equipamentos e clientes cada lote foi parar.</p>
      </PageHeading>
      <InventoryTabs />
      <form role="search" onSubmit={submit} className="mb-4 flex flex-wrap items-end gap-2">
        <label className="grow text-sm font-semibold" htmlFor="trace-term">
          Série ou lote
          <input id="trace-term" type="search" value={term} maxLength={60} onChange={(e) => setTerm(e.target.value)} placeholder="ELV-0001-2026-00001 ou L2026-01" className={`${inputClass} mt-1 w-full font-normal`} />
        </label>
        <button type="submit" className="rounded-md bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground">Rastrear</button>
        {serialId || lotId ? <button type="button" onClick={() => go({ q: q || undefined })} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar à busca</button> : null}
      </form>
      {body}
    </>
  )
}
