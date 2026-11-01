import { useNavigate } from '@tanstack/react-router'
import { FileUp, Keyboard } from 'lucide-react'
import { type DragEvent, useRef, useState } from 'react'
import { startManualIncomingInvoice, uploadIncomingInvoicePdf } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState } from '@/components/ui/states'
import { cn } from '@/lib/utils'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { precheckPdf } from './reading'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'
const secondary = 'inline-flex min-h-11 items-center gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-50'

/**
 * Importar PDF da DANFE (FIS-05 CA 1, `/app/fiscal/entrada/importar-pdf`): um PDF de até 5 MB, que passa pelo antivírus e pela recusa de
 * conteúdo ativo e vai para a leitura no worker isolado; a tela da leitura acompanha. "Digitar a nota" quando não há PDF que sirva.
 */
export function ImportPdfPage() {
  const navigate = useNavigate()
  const canImport = useCan('fiscal.import_incoming')
  const input = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [over, setOver] = useState(false)

  const pick = (list: FileList | null) => {
    const chosen = list?.[0]
    if (!chosen) return
    setFile(chosen)
    setError(precheckPdf(chosen))
  }
  const drop = (e: DragEvent) => {
    e.preventDefault()
    setOver(false)
    pick(e.dataTransfer.files)
  }

  const send = async () => {
    if (!file) return
    const refused = precheckPdf(file)
    if (refused) {
      setError(refused)
      return
    }
    setBusy(true)
    setError(null)
    try {
      const reading = await uploadIncomingInvoicePdf({ file })
      notifySuccess('PDF enviado para leitura')
      await navigate({ to: '/app/fiscal/entrada/leitura/$id', params: { id: reading.id } })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível enviar o PDF. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  const manual = async () => {
    setBusy(true)
    setError(null)
    try {
      const reading = await startManualIncomingInvoice()
      await navigate({ to: '/app/fiscal/entrada/leitura/$id', params: { id: reading.id } })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível abrir o formulário. Tente de novo.')
      setBusy(false)
    }
  }

  if (!canImport) {
    return (
      <>
        <PageHeading title="Importar PDF da DANFE" />
        <EmptyState title="Importar nota de entrada pede a permissão do Fiscal." description="Fale com quem administra os grupos para receber a permissão Importar nota de entrada." />
      </>
    )
  }

  return (
    <>
      <PageHeading title="Importar PDF da DANFE">
        <p className="mt-2 text-muted-foreground">
          Quando o fornecedor mandou só o PDF: o sistema lê a chave, o cabeçalho, os totais e os itens, e você confere campo a campo. O XML continua sendo o melhor caminho.
        </p>
      </PageHeading>
      <div
        onDragOver={(e) => { e.preventDefault(); setOver(true) }}
        onDragLeave={() => setOver(false)}
        onDrop={drop}
        className={cn('flex flex-col items-center gap-3 rounded-md border-2 border-dashed border-border bg-card px-4 py-10 text-center', over && 'border-primary bg-primary-soft')}
      >
        <FileUp aria-hidden="true" className="size-8 text-muted-foreground" />
        <p className="font-semibold">Arraste o PDF da DANFE para cá</p>
        <p className="text-sm text-muted-foreground">Um arquivo .pdf de até 5 MB, de 1 a 10 páginas, com texto (PDF escaneado não é lido).</p>
        <input ref={input} type="file" accept=".pdf,application/pdf" className="sr-only" id="pdf-file" onChange={(e) => { pick(e.target.files); e.target.value = '' }} />
        <label htmlFor="pdf-file" className={cn(secondary, 'cursor-pointer')}>Escolher arquivo</label>
      </div>
      {file ? <p className="mt-3 rounded-md border border-border bg-card px-3 py-2 text-sm">{file.name}</p> : null}
      {error ? <p role="alert" className="mt-2 text-sm font-medium text-crit-ink">{error}</p> : null}
      <div className="mt-3 flex flex-wrap gap-2">
        <button type="button" disabled={busy || !file || error !== null} onClick={() => void send()} className={primary}><FileUp aria-hidden="true" className="size-4" />Importar PDF</button>
        <button type="button" disabled={busy} onClick={() => void manual()} className={secondary}><Keyboard aria-hidden="true" className="size-4" />Digitar a nota</button>
      </div>
    </>
  )
}
