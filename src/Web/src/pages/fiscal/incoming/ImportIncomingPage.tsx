import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { CheckCircle2, FileUp, XCircle } from 'lucide-react'
import { type DragEvent, useRef, useState } from 'react'
import { getListIncomingInvoicesQueryKey, importIncomingInvoice } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { cn } from '@/lib/utils'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { DICAS, type FileResult, importedLabel, MAX_FILES, precheck } from './incoming'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'
const secondary = 'inline-flex min-h-11 items-center gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'

/**
 * Importar XML (FIS-04a CA 1 a 5, `/app/fiscal/entrada/importar`): arrastar e soltar até 20 arquivos `.xml`; cada um vai numa requisição
 * (o servidor conta 60 por hora por usuário) e mostra o próprio resultado, sem bloquear os outros.
 */
export function ImportIncomingPage() {
  const client = useQueryClient()
  const canImport = useCan('fiscal.import_incoming')
  const input = useRef<HTMLInputElement>(null)
  const [files, setFiles] = useState<File[]>([])
  const [results, setResults] = useState<FileResult[]>([])
  const [busy, setBusy] = useState(false)
  const [over, setOver] = useState(false)
  const [tooMany, setTooMany] = useState(false)

  const pick = (list: FileList | null) => {
    if (!list) return
    const chosen = [...files, ...Array.from(list)]
    setTooMany(chosen.length > MAX_FILES)
    setFiles(chosen.slice(0, MAX_FILES))
    setResults([])
  }
  const drop = (e: DragEvent) => {
    e.preventDefault()
    setOver(false)
    pick(e.dataTransfer.files)
  }

  const send = async () => {
    setBusy(true)
    const next: FileResult[] = files.map((f) => ({ name: f.name, state: 'pending' }))
    setResults([...next])
    let ok = 0
    for (const [i, file] of files.entries()) {
      const refused = precheck(file)
      if (refused) {
        next[i] = { name: file.name, state: 'error', message: refused }
        setResults([...next])
        continue
      }
      next[i] = { name: file.name, state: 'sending' }
      setResults([...next])
      try {
        const r = await importIncomingInvoice({ file })
        next[i] = { name: file.name, state: 'ok', id: r.id, label: `NF-e ${r.number} · série ${r.series} — ${r.issuerName}`, remembered: r.remembered }
        ok++
      } catch (e) {
        const link = e instanceof ApiError && typeof e.data.invoiceId === 'string' ? e.data.invoiceId : undefined
        next[i] = { name: file.name, state: 'error', message: e instanceof ApiError ? e.message : 'Não foi possível enviar o arquivo. Tente de novo.', link }
      }
      setResults([...next])
    }
    setBusy(false)
    setFiles([])
    if (ok > 0) {
      notifySuccess(importedLabel(ok))
      await client.invalidateQueries({ queryKey: getListIncomingInvoicesQueryKey() })
    }
  }

  if (!canImport) {
    return (
      <>
        <PageHeading title="Importar XML" />
        <EmptyState title="Importar nota de entrada pede a permissão do Fiscal." description="Fale com quem administra os grupos para receber a permissão Importar nota de entrada." />
      </>
    )
  }

  return (
    <>
      <PageHeading title="Importar XML">
        <p className="mt-2 flex flex-wrap items-center gap-1 text-muted-foreground">
          Solte o XML autorizado que o fornecedor mandou. O sistema confere o antivírus, o leiaute, o protocolo e a assinatura<Dica nome="Assinatura da nota" texto={DICAS.assinatura} /> de cada arquivo.
        </p>
      </PageHeading>
      <div
        onDragOver={(e) => { e.preventDefault(); setOver(true) }}
        onDragLeave={() => setOver(false)}
        onDrop={drop}
        className={cn('flex flex-col items-center gap-3 rounded-md border-2 border-dashed border-border bg-card px-4 py-10 text-center', over && 'border-primary bg-primary-soft')}
      >
        <FileUp aria-hidden="true" className="size-8 text-muted-foreground" />
        <p className="font-semibold">Arraste os arquivos .xml para cá</p>
        <p className="text-sm text-muted-foreground">Até {MAX_FILES} arquivos de até 2 MB cada.</p>
        <input ref={input} type="file" accept=".xml,application/xml,text/xml" multiple className="sr-only" id="xml-files" onChange={(e) => { pick(e.target.files); e.target.value = '' }} />
        <label htmlFor="xml-files" className={cn(secondary, 'cursor-pointer')}>Escolher arquivos</label>
      </div>
      {tooMany ? <p role="alert" className="mt-2 text-sm font-medium text-warn-ink">Ficaram os primeiros {MAX_FILES} arquivos. Importe os outros depois.</p> : null}

      {files.length > 0 ? (
        <div className="mt-4 space-y-2">
          <ul className="space-y-1 text-sm">
            {files.map((f, i) => <li key={`${f.name}-${i}`} className="rounded-md border border-border bg-card px-3 py-2">{f.name}</li>)}
          </ul>
          <div className="flex flex-wrap gap-2">
            <button type="button" disabled={busy} onClick={() => void send()} className={primary}><FileUp aria-hidden="true" className="size-4" />Importar XML</button>
            <button type="button" disabled={busy} onClick={() => setFiles([])} className={secondary}>Limpar</button>
          </div>
        </div>
      ) : null}

      {results.length > 0 ? (
        <section aria-label="Resultado da importação" aria-live="polite" className="mt-4 space-y-2">
          <h2 className="font-semibold">Resultado</h2>
          <ul className="space-y-2">
            {results.map((r, i) => (
              <li key={`${r.name}-${i}`} className={cn('rounded-md border border-border bg-card p-3 text-sm', r.state === 'error' && 'border-l-4 border-l-crit')}>
                <p className="flex flex-wrap items-center gap-2 font-semibold">
                  {r.state === 'ok' ? <CheckCircle2 aria-hidden="true" className="size-4 text-ok-ink" /> : r.state === 'error' ? <XCircle aria-hidden="true" className="size-4 text-crit-ink" /> : null}
                  {r.name}
                  {r.state === 'pending' ? <StatusChip tone="neutral">Na fila</StatusChip> : null}
                  {r.state === 'sending' ? <StatusChip tone="info">Enviando</StatusChip> : null}
                  {r.state === 'ok' ? <StatusChip tone="ok">Importada</StatusChip> : null}
                  {r.state === 'error' ? <StatusChip tone="crit">Recusada</StatusChip> : null}
                </p>
                {r.state === 'ok' ? (
                  <p className="mt-1">
                    <Link to="/app/fiscal/entrada/$id" params={{ id: r.id }} className="font-semibold text-primary">{r.label}</Link>
                    {r.remembered > 0 ? <span className="text-muted-foreground"> · {r.remembered === 1 ? '1 item com de-para lembrado' : `${r.remembered} itens com de-para lembrado`}</span> : null}
                  </p>
                ) : null}
                {r.state === 'error' ? (
                  <p className="mt-1 text-crit-ink">
                    {r.message}
                    {r.link ? <> <Link to="/app/fiscal/entrada/$id" params={{ id: r.link }} className="font-semibold text-primary">Abrir a nota</Link></> : null}
                  </p>
                ) : null}
              </li>
            ))}
          </ul>
          <Link to="/app/fiscal/entrada" className={secondary}>Ver notas de entrada</Link>
        </section>
      ) : null}
    </>
  )
}
