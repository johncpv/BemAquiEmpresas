import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { previewQuoteCostRefresh, refreshQuoteCosts } from '@/api/generated/bemaqui'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'

/** "Atualizar custos" (CA 13): mostra cada linha com o valor antigo e o novo e só grava ao confirmar. */
export function RefreshCostsDialog({ quoteId, version, onDone }: { quoteId: string; version: number; onDone: () => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const preview = useQuery({ queryKey: ['quote-refresh', quoteId, version], queryFn: () => previewQuoteCostRefresh(quoteId), enabled: open })
  const lines = preview.data?.lines ?? []
  return (
    <ConfirmDialog size="lg" verb="Atualizar custos" done="Custos atualizados" title="Atualizar os custos de mão de obra?" open={open} onOpenChange={setOpen}
      description="Os custos-hora dos cargos mudaram desde que o orçamento foi montado. Preços digitados à mão não mudam."
      preconditions={[{ ok: lines.length > 0, text: 'Há custos diferentes do custo-hora atual.' }]}
      impact={preview.data ? <p className="text-sm">Custo total <Money value={preview.data.oldTotalCost} /> → <Money value={preview.data.newTotalCost} className="font-semibold" /></p> : null}
      onConfirm={async () => { await refreshQuoteCosts(quoteId, { version }); await onDone() }}
      trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Atualizar custos</button>}>
      {lines.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">{preview.isPending ? 'Conferindo os custos…' : 'Todos os custos já estão atualizados.'}</p> : (
        <ul className="mt-3 divide-y divide-border rounded-md border border-border text-sm">
          {lines.map((l) => <li key={l.lineId} className="flex flex-wrap justify-between gap-2 p-2"><span>{l.itemDescription} · {l.positionName} · {l.hours} h</span><span><Money value={l.oldTotal} /> → <Money value={l.newTotal} className="font-semibold" /></span></li>)}
        </ul>
      )}
    </ConfirmDialog>
  )
}
