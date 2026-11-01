import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { FileText, Keyboard, Loader2, Mail, Plus, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import {
  checkIncomingInvoiceReading, getDownloadIncomingInvoiceReadingPdfUrl, getGetIncomingInvoiceReadingQueryKey, getListIncomingInvoicesQueryKey, requestIncomingInvoiceXml,
  switchIncomingInvoiceReadingToManual, useGetIncomingInvoiceReading,
} from '@/api/generated/bemaqui'
import type { CheckReadingRequest, IncomingReadingView } from '@/api/generated/model'
import { apiBlobUrl, ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import { PageHeading } from '@/shell/PageHeading'
import { formatAccessKey, formatCnpj } from './incoming'
import {
  DIGITADO, digits, failedTitle, type FieldDef, type FieldKind, type FieldState, firstToCheck, HEADER_FIELDS, isValidAccessKey, ITEM_FIELDS, itemKey, pendingCount, pendingLabel,
  READING_DICAS, removeConfirmed, removeItem, requiredKeys, STATE_LABEL, sumMismatch, toInput, toRequest, TOTAL_FIELDS,
} from './reading'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'
const secondary = 'inline-flex min-h-11 items-center gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-50'

/** Depois disso, "Lendo o PDF…" mostra o estado de demora (CA 1). */
export const SLOW_AFTER_MS = 30_000

const fieldId = (key: string) => `campo-${key.replace(/\./g, '-')}`

/**
 * Leitura assistida (FIS-05 CA 1 a 9, `/app/fiscal/entrada/leitura/$id`): "Lendo o PDF…" (com a demora depois de 30 s), a leitura que não
 * serviu ("Pedir o XML ao fornecedor" e "Digitar a nota"), e o formulário em duas colunas, o PDF e os campos lidos com a situação em
 * texto, o foco no primeiro campo a conferir e o contador fixo no rodapé. "Conferir nota" só com todo campo conferido.
 */
export function IncomingReadingPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const client = useQueryClient()
  const query = useGetIncomingInvoiceReading(id, { query: { retry: false, refetchInterval: (q) => (q.state.data?.status === 'Reading' ? 2000 : false) } })
  const set = (view: IncomingReadingView) => client.setQueryData(getGetIncomingInvoiceReadingQueryKey(id), view)

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-24" /><Skeleton className="h-64" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Leitura não encontrada.' : undefined} message={notFound ? 'Volte para as notas de entrada e importe o PDF de novo.' : undefined} onRetry={notFound ? undefined : () => void query.refetch()} />
  }

  const reading = query.data
  const title = reading.source === 'Manual' ? 'Digitar a nota' : 'Leitura do PDF da DANFE'
  if (reading.status === 'Reading') return <><PageHeading title={title} /><ReadingNow createdAt={reading.createdAt} onManual={async () => set(await switchIncomingInvoiceReadingToManual(reading.id))} /></>
  if (reading.status === 'Checked') {
    return (
      <>
        <PageHeading title={title} />
        <div role="status" className="rounded-md border border-border bg-card p-4 text-sm">
          Esta leitura já foi conferida e virou nota.{' '}
          {reading.invoiceId ? <Link to="/app/fiscal/entrada/$id" params={{ id: reading.invoiceId }} className="font-semibold text-primary">Abrir a nota</Link> : null}
        </div>
      </>
    )
  }

  if (reading.status === 'Failed') return <><PageHeading title={title} /><Failed reading={reading} onChange={set} /></>
  return <ReadingForm key={`${reading.id}-${reading.source}`} reading={reading} />
}

/** "Lendo o PDF…" e, depois de 30 s, o estado de demora com a saída "Digitar a nota". */
function ReadingNow({ createdAt, onManual }: { createdAt: string; onManual: () => Promise<unknown> }) {
  const [slow, setSlow] = useState(() => Date.now() - new Date(createdAt).getTime() > SLOW_AFTER_MS)
  useEffect(() => {
    if (slow) return
    const timer = window.setTimeout(() => setSlow(true), Math.max(0, SLOW_AFTER_MS - (Date.now() - new Date(createdAt).getTime())))
    return () => window.clearTimeout(timer)
  }, [createdAt, slow])
  return (
    <div role="status" aria-live="polite" className="flex flex-col items-center gap-3 rounded-md border border-border bg-card px-6 py-10 text-center">
      <Loader2 aria-hidden="true" className="size-6 animate-spin text-muted-foreground" />
      <p className="font-semibold">Lendo o PDF…</p>
      {slow ? (
        <>
          <p className="max-w-md text-sm text-muted-foreground">A leitura está demorando mais que o normal. Ela para sozinha em pouco tempo; se não servir, você pode digitar a nota.</p>
          <button type="button" onClick={() => void onManual()} className={secondary}><Keyboard aria-hidden="true" className="size-4" />Digitar a nota</button>
        </>
      ) : <p className="text-sm text-muted-foreground">O sistema está lendo a chave, os totais e os itens. Leva alguns segundos.</p>}
    </div>
  )
}

/** Leitura que não serviu (CA 3): o motivo, "Pedir o XML ao fornecedor" e "Digitar a nota". */
function Failed({ reading, onChange }: { reading: IncomingReadingView; onChange: (v: IncomingReadingView) => void }) {
  const [error, setError] = useState<string | null>(null)
  const manual = async () => {
    setError(null)
    try {
      onChange(await switchIncomingInvoiceReadingToManual(reading.id))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível abrir o formulário. Tente de novo.')
    }
  }
  return (
    <div role="alert" className="flex flex-col items-center gap-3 rounded-md border border-crit-soft bg-card px-6 py-10 text-center">
      <p className="font-semibold">{failedTitle(reading.result)}</p>
      <p className="max-w-md text-sm text-muted-foreground">{reading.message}</p>
      <div className="flex flex-wrap justify-center gap-2">
        {reading.can.import ? <XmlRequest reading={reading} /> : null}
        {reading.can.import ? <button type="button" onClick={() => void manual()} className={primary}><Keyboard aria-hidden="true" className="size-4" />Digitar a nota</button> : null}
      </div>
      {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
    </div>
  )
}

/** "Pedir o XML ao fornecedor" (CA 6): o e-mail vai pela fila ao contato do fornecedor; "Pedido enviado". */
function XmlRequest({ reading }: { reading: IncomingReadingView }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState<string | null>(reading.xmlRequestedAt ? `Pedido enviado em ${formatDateTime(reading.xmlRequestedAt)}.` : null)
  const run = async () => {
    setBusy(true)
    setError(null)
    try {
      const result = await requestIncomingInvoiceXml(reading.id)
      setSent(`Pedido enviado para ${result.to}.`)
      notifySuccess('Pedido enviado')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível pedir o XML. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <span className="inline-flex flex-col items-start gap-1">
      <button type="button" disabled={busy} onClick={() => void run()} className={secondary}><Mail aria-hidden="true" className="size-4" />Pedir o XML ao fornecedor</button>
      {sent ? <span className="text-xs text-muted-foreground">{sent}</span> : null}
      {error ? <span role="alert" className="text-xs text-crit-ink">{error}</span> : null}
    </span>
  )
}

const itemCount = (reading: IncomingReadingView) => Math.max(reading.source === 'Manual' ? 1 : 0, reading.fields.filter((f) => /^items\.\d+\.code$/.test(f.key)).length)

/** O formulário da leitura assistida e da nota digitada (CA 7 a 9). */
function ReadingForm({ reading }: { reading: IncomingReadingView }) {
  const navigate = useNavigate()
  const client = useQueryClient()
  const manual = reading.source === 'Manual'
  const [count, setCount] = useState(() => Math.max(1, itemCount(reading)))
  const [values, setValues] = useState<Record<string, string>>(() => {
    const kinds = new Map<string, FieldKind>([...HEADER_FIELDS, ...TOTAL_FIELDS].map((f) => [f.key, f.kind]))
    const initial: Record<string, string> = {}
    for (const f of reading.fields) {
      const kind = kinds.get(f.key) ?? ITEM_FIELDS.find((i) => f.key.endsWith(`.${i.field}`))?.kind ?? 'text'
      initial[f.key] = toInput(kind, f.value)
    }
    if (manual && !initial.recipientDocument && reading.tenantDocument) initial.recipientDocument = reading.tenantDocument
    return initial
  })
  const [states, setStates] = useState<Record<string, FieldState>>(() => Object.fromEntries(reading.fields.map((f) => [f.key, f.state as FieldState])))
  const [confirmed, setConfirmed] = useState<Set<string>>(() => new Set())
  const [tab, setTab] = useState<'campos' | 'pdf'>('campos')
  const keys = useMemo(() => requiredKeys(count), [count])
  const pending = pendingCount(keys, confirmed)
  const mismatch = sumMismatch(values, count)
  const keyDigits = digits(values.accessKey ?? '')
  const keyInvalid = keyDigits.length > 0 && !isValidAccessKey(keyDigits)
  const recipientWrong = !!reading.tenantDocument && digits(values.recipientDocument ?? '').length === 14 && digits(values.recipientDocument ?? '') !== reading.tenantDocument

  // O foco vai para o primeiro campo incerto ou não encontrado, uma vez (CA 7).
  const focused = useRef(false)
  useEffect(() => {
    if (focused.current) return
    focused.current = true
    const first = firstToCheck(keys, states, confirmed)
    if (first) document.getElementById(fieldId(first))?.focus()
  }, [keys, states, confirmed])

  const edit = (key: string, value: string) => {
    setValues((v) => ({ ...v, [key]: value }))
    setConfirmed((c) => new Set(c).add(key))
  }
  const confirm = (key: string, ok: boolean) => setConfirmed((c) => {
    const next = new Set(c)
    if (ok) next.add(key)
    else next.delete(key)
    return next
  })
  const addItem = () => setCount((n) => n + 1)
  const dropItem = (index: number) => {
    setValues((v) => removeItem(v, index, count))
    setStates((s) => removeItem(s, index, count))
    setConfirmed((c) => removeConfirmed(c, index, count))
    setCount((n) => Math.max(1, n - 1))
  }

  const blocked = pending > 0 ? pendingLabel(pending) : mismatch ?? (keyInvalid ? 'A chave de acesso não confere (dígito verificador).' : recipientWrong ? 'A nota não é destinada a esta empresa.' : null)
  const submit = async () => {
    const result = await checkIncomingInvoiceReading(reading.id, toRequest(values, count, confirmed) as CheckReadingRequest)
    await client.invalidateQueries({ queryKey: getListIncomingInvoicesQueryKey() })
    await navigate({ to: '/app/fiscal/entrada/$id', params: { id: result.invoiceId } })
  }

  const field = (def: FieldDef) => (
    <FieldBox key={def.key} def={def} value={values[def.key] ?? ''} state={manual ? undefined : states[def.key] ?? 'NotFound'} confirmed={confirmed.has(def.key)}
      onChange={(v) => edit(def.key, v)} onConfirm={(ok) => confirm(def.key, ok)} />
  )

  return (
    <>
      <PageHeading title={manual ? 'Digitar a nota' : 'Leitura do PDF da DANFE'}>
        <p className="mt-2 flex flex-wrap items-center gap-1 text-muted-foreground">
          {manual ? 'Digite os dados da DANFE e confira cada campo.' : 'Os campos foram lidos do PDF: confira cada um com a DANFE, corrija o que precisar e marque Conferido.'}
          <StatusChip tone="warn">{manual ? 'Digitada, sem XML' : 'Lida do PDF, sem XML'}</StatusChip>
          <Dica nome={manual ? 'Digitada, sem XML' : 'Lida do PDF, sem XML'} texto={manual ? READING_DICAS.manual : READING_DICAS.pdf} />
        </p>
      </PageHeading>

      {reading.xmlByKeyMessage && reading.can.import ? (
        <div role="note" className="mb-3 flex flex-wrap items-center gap-3 rounded-md border border-info-soft bg-info-soft p-3 text-sm text-info-ink">
          <span className="font-semibold">{reading.xmlByKeyMessage}</span>
          <XmlRequest reading={reading} />
        </div>
      ) : null}
      {reading.warnings.map((w) => <p key={w} role="alert" className="mb-2 rounded-md border border-warn-soft bg-warn-soft p-2 text-sm text-warn-ink">{w}</p>)}
      {reading.duplicateInvoiceId ? (
        <p role="alert" className="mb-3 rounded-md border border-crit-soft bg-crit-soft p-3 text-sm text-crit-ink">
          {reading.duplicateMessage}{' '}
          <Link to="/app/fiscal/entrada/$id" params={{ id: reading.duplicateInvoiceId }} className="font-semibold text-primary">Abrir a nota</Link>
        </p>
      ) : null}

      <div role="tablist" aria-label="Leitura" className="mb-3 flex gap-2 lg:hidden">
        <button type="button" role="tab" aria-selected={tab === 'campos'} onClick={() => setTab('campos')} className={cn(secondary, tab === 'campos' && 'border-primary text-primary')}>Campos</button>
        {reading.hasPdf ? <button type="button" role="tab" aria-selected={tab === 'pdf'} onClick={() => setTab('pdf')} className={cn(secondary, tab === 'pdf' && 'border-primary text-primary')}><FileText aria-hidden="true" className="size-4" />Ver PDF</button> : null}
      </div>

      <div className={cn('grid gap-4', reading.hasPdf && 'lg:grid-cols-2')}>
        {reading.hasPdf ? <div className={cn(tab !== 'pdf' && 'hidden lg:block')}><PdfPane readingId={reading.id} /></div> : null}
        <div className={cn('space-y-4', tab !== 'campos' && 'hidden lg:block')}>
          {reading.key ? (
            <p className="rounded-md border border-border bg-muted p-2 text-sm">
              Da chave: NF-e <span className="tabular-nums">{reading.key.number}</span> · série <span className="tabular-nums">{reading.key.series}</span> · emitente CNPJ{' '}
              <span className="tabular-nums">{formatCnpj(reading.key.issuerDocument)}</span> · <span className="tabular-nums">{String(reading.key.month).padStart(2, '0')}/{reading.key.year}</span>
              {reading.supplier ? <> · {reading.supplier.state === 'Found' ? `fornecedor ${reading.supplier.name}` : 'fornecedor sem cadastro (cadastre na nota)'}</> : null}
            </p>
          ) : null}
          <section aria-label="Cabeçalho" className="grid gap-2 sm:grid-cols-2">{HEADER_FIELDS.map(field)}</section>
          {keyDigits.length === 44 ? <p className="text-xs text-muted-foreground tabular-nums">{formatAccessKey(keyDigits)}</p> : null}
          <section aria-label="Totais">
            <h2 className="mb-2 font-semibold">Totais</h2>
            <div className="grid gap-2 sm:grid-cols-2">{TOTAL_FIELDS.map(field)}</div>
          </section>
          <section aria-label="Itens">
            <h2 className="mb-2 font-semibold">Itens</h2>
            <ol className="space-y-3">
              {Array.from({ length: count }, (_, i) => (
                <li key={i} className="rounded-md border border-border bg-card p-3">
                  <div className="mb-2 flex items-center justify-between gap-2">
                    <b className="font-semibold">Item {i + 1}</b>
                    {count > 1 ? <button type="button" onClick={() => dropItem(i)} className={secondary}><Trash2 aria-hidden="true" className="size-4" />Tirar item</button> : null}
                  </div>
                  <div className="grid gap-2 sm:grid-cols-2">
                    {ITEM_FIELDS.map((f) => field({ key: itemKey(i, f.field), label: f.label, kind: f.kind }))}
                  </div>
                </li>
              ))}
            </ol>
            <button type="button" onClick={addItem} className={`${secondary} mt-2`}><Plus aria-hidden="true" className="size-4" />Adicionar item</button>
          </section>
        </div>
      </div>

      <div className="sticky bottom-20 z-10 mt-4 flex flex-wrap items-center justify-between gap-3 rounded-md border border-border bg-card px-4 py-2 shadow-sm">
        <p aria-live="polite" className={cn('text-sm font-semibold', pending > 0 ? 'text-warn-ink' : 'text-ok-ink')}>{pendingLabel(pending)}</p>
        {mismatch ? <p role="alert" className="text-sm text-crit-ink">{mismatch}</p> : null}
        {reading.can.import ? (
          blocked ? (
            <span className="flex flex-wrap items-center gap-2">
              <button type="button" disabled aria-describedby="conferir-leitura-motivo" className={primary}>Conferir nota</button>
              <span id="conferir-leitura-motivo" className="sr-only">{blocked}</span>
            </span>
          ) : (
            <ConfirmDialog verb="Conferir nota" done="Nota conferida" title="Conferir a nota?"
              description="A nota nasce com os valores conferidos, marcada sem XML. Depois, ligue os itens e o pedido e confira a nota como uma nota do XML. Nada entra no estoque nem no financeiro antes do recebimento."
              onConfirm={submit} trigger={<button type="button" className={primary}>Conferir nota</button>} />
          )
        ) : null}
      </div>
    </>
  )
}

/** Um campo: rótulo, valor, situação em texto e "Conferido" (44 px). Corrigir o valor já marca como conferido. */
function FieldBox({ def, value, state, confirmed, onChange, onConfirm }: {
  def: FieldDef; value: string; state: FieldState | undefined; confirmed: boolean; onChange: (v: string) => void; onConfirm: (ok: boolean) => void
}) {
  const id = fieldId(def.key)
  const info = state ? STATE_LABEL[state] : DIGITADO
  const numeric = def.kind === 'money' || def.kind === 'quantity' || def.kind === 'price'
  return (
    <div className={cn('rounded-md border border-border bg-card p-2', !confirmed && state === 'Uncertain' && 'border-l-4 border-l-warn', !confirmed && state === 'NotFound' && 'border-l-4 border-l-crit')}>
      <div className="flex flex-wrap items-center justify-between gap-1">
        <label htmlFor={id} className="text-sm font-medium">{def.label}</label>
        <span className="inline-flex items-center gap-1">
          <StatusChip tone={confirmed ? 'ok' : info.tone}><span id={`${id}-estado`}>{confirmed ? 'Conferido' : info.label}</span></StatusChip>
          {!confirmed && state === 'Uncertain' ? <Dica nome="Leitura incerta" texto={READING_DICAS.incerta} /> : null}
        </span>
      </div>
      <input id={id} value={value} onChange={(e) => onChange(e.target.value)} type={def.kind === 'date' ? 'date' : 'text'}
        inputMode={numeric ? 'decimal' : def.kind === 'key' || def.kind === 'document' || def.kind === 'ncm' ? 'numeric' : undefined}
        aria-describedby={`${id}-estado`} className={cn(inputClass, 'mt-1', numeric && 'tabular-nums')} />
      {def.hint ? <p className="mt-1 text-xs text-muted-foreground">{def.hint}</p> : null}
      <label className="mt-1 inline-flex min-h-11 cursor-pointer items-center gap-2 text-sm">
        <input type="checkbox" checked={confirmed} onChange={(e) => onConfirm(e.target.checked)} className="size-5" />
        Conferido
      </label>
    </div>
  )
}

/** O PDF ao lado dos campos (só o que passou pela conferência de segurança do worker). */
function PdfPane({ readingId }: { readingId: string }) {
  const [url, setUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let alive = true
    let created: string | null = null
    apiBlobUrl(getDownloadIncomingInvoiceReadingPdfUrl(readingId))
      .then((u) => {
        created = u
        if (alive) setUrl(u)
        else URL.revokeObjectURL(u)
      })
      .catch((e: unknown) => { if (alive) setError(e instanceof ApiError ? e.message : 'Não foi possível abrir o PDF.') })
    return () => {
      alive = false
      if (created) URL.revokeObjectURL(created)
    }
  }, [readingId])
  if (error) return <ErrorState title="Não foi possível abrir o PDF." message={error} />
  if (!url) return <Skeleton className="h-[70vh]" />
  return <iframe title="PDF da DANFE" src={url} className="h-[70vh] w-full rounded-md border border-border bg-card" />
}
