import { FileDown, Printer } from 'lucide-react'
import { useId, useState } from 'react'
import { ApiError, apiBlobUrl, apiDownload } from '@/api/http'

interface DocumentActionsProps {
  /** Endereço do PDF (gerado pelo Orval, ex.: getDownloadPaymentOrderPdfUrl(id)). */
  url: string
  /** Nome do arquivo baixado (ex.: "OP-2026-0001.pdf"). */
  fileName: string
  /** Rótulo do botão de baixar; padrão "Baixar PDF". */
  label?: string
  /** Mostra "Imprimir" (abre o PDF numa aba nova para o navegador imprimir). Padrão: sim. */
  print?: boolean
}

type Busy = 'download' | 'print' | null

const button = 'inline-flex min-h-10 items-center gap-1.5 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'

/**
 * Ações do documento (EXP-01, ADR 0045 §4): "Baixar PDF" e "Imprimir" no cabeçalho, primeiro botão secundário à direita do título. Enquanto
 * gera, o botão fica ocupado ("Gerando PDF…", aria-busy); se falhar, a mensagem do servidor aparece abaixo com "Tentar de novo".
 */
export function DocumentActions({ url, fileName, label = 'Baixar PDF', print = true }: DocumentActionsProps) {
  const [busy, setBusy] = useState<Busy>(null)
  const [error, setError] = useState<{ message: string; retry: Exclude<Busy, null> } | null>(null)
  const errorId = useId()

  const run = async (kind: Exclude<Busy, null>) => {
    setError(null)
    setBusy(kind)
    // A aba abre no clique (antes de esperar o servidor) para o navegador não bloquear como pop-up.
    const tab = kind === 'print' ? window.open('about:blank', '_blank') : null
    try {
      if (kind === 'download') {
        await apiDownload(url, fileName)
      } else {
        const href = await apiBlobUrl(url)
        if (tab) {
          tab.opener = null
          tab.location.href = href
        } else {
          setError({ message: 'O navegador bloqueou a aba nova. Libere as janelas deste site ou use "Baixar PDF".', retry: kind })
        }
        setTimeout(() => URL.revokeObjectURL(href), 60_000)
      }
    } catch (e) {
      tab?.close()
      setError({ message: e instanceof ApiError ? e.message : 'Não foi possível gerar o PDF. Verifique a conexão e tente de novo.', retry: kind })
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="flex flex-col items-start gap-1">
      <div className="flex flex-wrap gap-2" role="group" aria-label="Documento em PDF">
        <button type="button" className={button} onClick={() => void run('download')} disabled={busy !== null} aria-busy={busy === 'download'} aria-describedby={error ? errorId : undefined}>
          <FileDown aria-hidden="true" className="size-4" />
          {busy === 'download' ? 'Gerando PDF…' : label}
        </button>
        {print ? (
          <button type="button" className={button} onClick={() => void run('print')} disabled={busy !== null} aria-busy={busy === 'print'} aria-describedby={error ? errorId : undefined}>
            <Printer aria-hidden="true" className="size-4" />
            {busy === 'print' ? 'Abrindo…' : 'Imprimir'}
          </button>
        ) : null}
      </div>
      {error ? (
        <p id={errorId} role="alert" className="flex max-w-sm flex-wrap items-center gap-2 text-sm text-crit-ink">
          {error.message}
          <button type="button" onClick={() => void run(error.retry)} className="font-semibold text-primary underline-offset-2 hover:underline">Tentar de novo</button>
        </p>
      ) : null}
    </div>
  )
}
