import * as Menu from '@radix-ui/react-dropdown-menu'
import { ChevronDown, FileDown } from 'lucide-react'
import { useId, useState } from 'react'
import { getDownloadRfqComparisonPdfUrl, getDownloadRfqRequestPdfUrl } from '@/api/generated/bemaqui'
import type { RfqDetail, RfqSupplierView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { DocumentActions } from '@/components/ui/document-actions'
import { responseState, rfqRequestFileName } from './rfqs'

const button = 'inline-flex min-h-10 items-center gap-1.5 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'

/** Baixa o pedido de cotação de um fornecedor; devolve a mensagem de erro do servidor (409 da cotação fechada, 404) ou null. */
export async function downloadRfqRequest(rfq: Pick<RfqDetail, 'id' | 'number'>, supplier: Pick<RfqSupplierView, 'id' | 'partyName'>): Promise<string | null> {
  try {
    await apiDownload(getDownloadRfqRequestPdfUrl(rfq.id, supplier.id), rfqRequestFileName(rfq.number, supplier.partyName))
    return null
  } catch (e) {
    return e instanceof ApiError ? e.message : 'Não foi possível gerar o PDF. Verifique a conexão e tente de novo.'
  }
}

/** O mapa comparativo sai da cotação aberta com ao menos uma resposta, da concluída e da cancelada (EXP-03 CA 2). */
export const canDownloadComparison = (rfq: Pick<RfqDetail, 'status' | 'suppliers'>) => rfq.status !== 'Open' || rfq.suppliers.some((s) => s.responded)

/**
 * "Baixar pedido de cotação" (EXP-03 CA 1): menu com um item por fornecedor e a situação da resposta; cada um baixa o PDF só daquele
 * fornecedor, sem preço de ninguém. Só na cotação aberta.
 */
export function RfqRequestMenu({ rfq }: { rfq: RfqDetail }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const errorId = useId()
  const run = async (supplier: RfqSupplierView) => {
    setError(null)
    setBusy(true)
    setError(await downloadRfqRequest(rfq, supplier))
    setBusy(false)
  }
  const item = 'flex cursor-pointer justify-between gap-3 rounded-sm px-2 py-1.5 text-sm outline-none data-[highlighted]:bg-muted'
  return (
    <div className="flex flex-col items-start gap-1">
      <Menu.Root>
        <Menu.Trigger disabled={busy || rfq.suppliers.length === 0} aria-busy={busy} aria-describedby={error ? errorId : undefined} className={button}>
          <FileDown aria-hidden="true" className="size-4" />
          {busy ? 'Gerando PDF…' : 'Baixar pedido de cotação'}
          <ChevronDown aria-hidden="true" className="size-3.5" />
        </Menu.Trigger>
        <Menu.Portal>
          <Menu.Content align="start" className="z-40 min-w-56 rounded-md border border-border bg-popover p-1 text-popover-foreground shadow-lg">
            {rfq.suppliers.map((s) => (
              <Menu.Item key={s.id} className={item} onSelect={() => void run(s)}>
                <span>{s.partyName}</span>
                <span className="text-xs text-muted-foreground">{responseState(s).label}</span>
              </Menu.Item>
            ))}
          </Menu.Content>
        </Menu.Portal>
      </Menu.Root>
      {error ? <p id={errorId} role="alert" className="max-w-sm text-sm text-crit-ink">{error}</p> : null}
    </div>
  )
}

/** "Baixar PDF" na linha do fornecedor (aba Respostas): o pedido de cotação só dele. */
export function RfqSupplierPdf({ rfq, supplier }: { rfq: RfqDetail; supplier: RfqSupplierView }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const errorId = useId()
  return (
    <span className="flex flex-col items-start gap-1">
      <button type="button" className={button} disabled={busy} aria-busy={busy} aria-describedby={error ? errorId : undefined}
        aria-label={`Baixar PDF do pedido de cotação para ${supplier.partyName}`}
        onClick={async () => { setError(null); setBusy(true); setError(await downloadRfqRequest(rfq, supplier)); setBusy(false) }}>
        <FileDown aria-hidden="true" className="size-4" />
        {busy ? 'Gerando PDF…' : 'Baixar PDF'}
      </button>
      {error ? <span id={errorId} role="alert" className="max-w-sm text-sm text-crit-ink">{error}</span> : null}
    </span>
  )
}

/** "Baixar mapa comparativo" (EXP-03 CA 2 e 3): "Prévia" enquanto aberta, sem marca na concluída, "Cancelado" na cancelada. */
export function RfqComparisonPdf({ rfq }: { rfq: RfqDetail }) {
  if (!canDownloadComparison(rfq)) return null
  return <DocumentActions url={getDownloadRfqComparisonPdfUrl(rfq.id)} fileName={`${rfq.number}-comparativo.pdf`} label="Baixar mapa comparativo" />
}
