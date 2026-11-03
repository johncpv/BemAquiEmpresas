import * as Menu from '@radix-ui/react-dropdown-menu'
import { ChevronDown, Download } from 'lucide-react'
import { useId, useState } from 'react'
import { ApiError, apiDownload } from '@/api/http'
import { exportDay } from './export-day'

export type ExportFormat = 'xlsx' | 'pdf'

interface ExportMenuProps {
  /** Endereço da exportação com os filtros atuais da tela (gerado pelo Orval, ex.: getExportPayablesUrl({ ...filtros, format })). */
  url: (format: ExportFormat) => string
  /** Começo do nome do arquivo, sem nome nem documento de pessoa (ex.: "contas-a-pagar"): sai "contas-a-pagar-2026-11-02.xlsx". */
  fileBase: string
  formats?: ExportFormat[]
  /** Lista vazia com os filtros atuais (EXP-02): "Exportar" desabilitado, com a dica "Nada para exportar com estes filtros.". */
  empty?: boolean
  /** Rótulo do item de PDF quando a tela tem documento próprio (ex.: "PDF do extrato"). */
  pdfLabel?: string
}

const LABELS: Record<ExportFormat, string> = { xlsx: 'Excel (.xlsx)', pdf: 'PDF' }
const EMPTY = 'Nada para exportar com estes filtros.'

/**
 * "Exportar" das listas (EXP-01, ADR 0045 §4): menu com Excel e PDF que envia os filtros e a busca da tela (não só a página atual). O arquivo
 * vem do servidor com a mesma permissão, a máscara e a auditoria da lista. Acima do teto, a mensagem do 422 ("Refine os filtros…") aparece
 * abaixo do botão. Teclado e leitor de tela pelo DropdownMenu do Radix (aria-haspopup, setas, Esc).
 */
export function ExportMenu({ url, fileBase, formats = ['xlsx', 'pdf'], empty = false, pdfLabel }: ExportMenuProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const errorId = useId()
  const emptyId = useId()

  const run = async (format: ExportFormat) => {
    setError(null)
    setBusy(true)
    try {
      await apiDownload(url(format), `${fileBase}-${exportDay()}.${format}`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível exportar. Verifique a conexão e tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  const item = 'cursor-pointer rounded-sm px-2 py-1.5 text-sm outline-none data-[highlighted]:bg-muted'
  return (
    <div className="flex flex-col items-end gap-1">
      <Menu.Root>
        <Menu.Trigger disabled={busy || empty} aria-busy={busy} aria-describedby={error ? errorId : empty ? emptyId : undefined} title={empty ? EMPTY : undefined}
          className="inline-flex min-h-9 items-center gap-1.5 rounded-md border border-border bg-card px-3 py-1.5 text-sm font-semibold disabled:opacity-60">
          <Download aria-hidden="true" className="size-4" />
          {busy ? 'Exportando…' : 'Exportar'}
          <ChevronDown aria-hidden="true" className="size-3.5" />
        </Menu.Trigger>
        <Menu.Portal>
          <Menu.Content align="end" className="z-40 min-w-44 rounded-md border border-border bg-popover p-1 text-popover-foreground shadow-lg">
            {formats.map((f) => (
              <Menu.Item key={f} className={item} onSelect={() => void run(f)}>{f === 'pdf' && pdfLabel ? pdfLabel : LABELS[f]}</Menu.Item>
            ))}
          </Menu.Content>
        </Menu.Portal>
      </Menu.Root>
      {empty && !error ? <span id={emptyId} className="sr-only">{EMPTY}</span> : null}
      {error ? <p id={errorId} role="alert" className="max-w-sm text-right text-sm text-crit-ink">{error}</p> : null}
    </div>
  )
}
