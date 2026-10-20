import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { copyQuote, copyQuoteItems, listQuoteCopyOptions } from '@/api/generated/bemaqui'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'

/**
 * "Copiar de…" (COM-01b CA 13) e "Copiar itens de…" (CA 14): escolhe um orçamento anterior (e, nos itens, quais);
 * a cópia leva composição e marcos no servidor.
 */
export function CopyQuoteDialog({ mode, quoteId, version, onDone }: { mode: 'quote' | 'items'; quoteId?: string; version?: number; onDone: (id: string) => Promise<void> | void }) {
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')
  const [source, setSource] = useState('')
  const [picked, setPicked] = useState<string[]>([])
  const idempotency = useIdempotencyKey()
  const options = useQuery({ queryKey: ['quote-copy-options', q], queryFn: () => listQuoteCopyOptions({ q: q || undefined }), enabled: open })
  const chosen = options.data?.find((o) => o.id === source)
  const verb = mode === 'quote' ? 'Copiar orçamento' : 'Copiar itens'
  return (
    <ConfirmDialog size="lg" verb={verb} done={mode === 'quote' ? 'Orçamento copiado' : 'Itens copiados'} open={open} onOpenChange={setOpen}
      title={mode === 'quote' ? 'Copiar de outro orçamento' : 'Copiar itens de outro orçamento'}
      description={mode === 'quote' ? 'Nasce um rascunho novo com os itens, os custos, os preços e os marcos. O original não muda.' : 'Os itens escolhidos entram neste orçamento com a composição de custo.'}
      preconditions={[{ ok: !!source, text: 'Orçamento de origem escolhido.' }, ...(mode === 'items' ? [{ ok: picked.length > 0, text: 'Ao menos um item escolhido.' }] : [])]}
      onConfirm={async () => {
        if (mode === 'quote') {
          const saved = await copyQuote(source, { partyId: null, contactId: null, siteAddressId: null }, idempotency.options)
          idempotency.renew()
          await onDone(saved.id)
        } else {
          await copyQuoteItems(quoteId ?? '', { sourceQuoteId: source, itemIds: picked, version: version ?? 0 })
          await onDone(quoteId ?? '')
        }
      }}
      trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">{mode === 'quote' ? 'Copiar de…' : 'Copiar itens de…'}</button>}>
      <div className="mt-3 space-y-3">
        <input type="search" aria-label="Buscar orçamento" placeholder="Número ou cliente" value={q} onChange={(e) => setQ(e.target.value)} className={inputClass} />
        <ul className="max-h-60 divide-y divide-border overflow-y-auto rounded-md border border-border text-sm" aria-label="Orçamentos">
          {(options.data ?? []).filter((o) => o.id !== quoteId).map((o) => (
            <li key={o.id}><label className="flex items-center gap-2 p-2"><input type="radio" name="origem" checked={source === o.id} onChange={() => { setSource(o.id); setPicked(o.items.map((i) => i.id)) }} className="size-4 accent-primary" />
              <span className="flex-1">{o.number} — {o.partyName}</span><Money value={o.totalPrice} /></label></li>
          ))}
        </ul>
        {mode === 'items' && chosen ? (
          <fieldset className="space-y-1">
            <legend className="text-sm font-medium">Itens</legend>
            {chosen.items.map((i) => <label key={i.id} className="flex items-center gap-2 text-sm"><input type="checkbox" checked={picked.includes(i.id)} onChange={(e) => setPicked(e.target.checked ? [...picked, i.id] : picked.filter((p) => p !== i.id))} className="size-4 accent-primary" />{i.description}</label>)}
          </fieldset>
        ) : null}
      </div>
    </ConfirmDialog>
  )
}
