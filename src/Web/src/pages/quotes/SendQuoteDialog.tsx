import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { emailQuote, getQuoteSendDraft } from '@/api/generated/bemaqui'
import type { QuoteDetail } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Dica } from '@/help/Dica'

/** "Enviar por e-mail" (COM-03 CA 7 a 9): destinatário e assunto preenchidos, até 3 cópias, mensagem editável e o PDF anexo. */
export function SendQuoteDialog({ quote, onSent }: { quote: QuoteDetail; onSent: () => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const draft = useQuery({ queryKey: ['quote-send-draft', quote.id, quote.version], queryFn: () => getQuoteSendDraft(quote.id), enabled: open })
  const [to, setTo] = useState<string | null>(null)
  const [cc, setCc] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [confirm, setConfirm] = useState(false)
  const idempotency = useIdempotencyKey()
  const toValue = to ?? draft.data?.to ?? ''
  const messageValue = message ?? draft.data?.message ?? ''
  const ccList = cc.split(/[,;\s]+/).map((c) => c.trim()).filter(Boolean)
  const known = (draft.data?.customerEmails ?? []).map((e) => e.toLowerCase())
  const stranger = [toValue, ...ccList].some((a) => a && !known.includes(a.toLowerCase()))
  return (
    <ConfirmDialog size="lg" verb="Enviar orçamento" done="Orçamento enviado" title="Enviar o orçamento por e-mail" open={open} onOpenChange={setOpen}
      description={draft.data ? `Assunto: ${draft.data.subject}. O PDF atual vai anexo (gerado agora, se ainda não existe).` : 'Preparando o envio…'}
      preconditions={[{ ok: toValue.length > 0, text: 'Destinatário informado.' }, { ok: ccList.length <= 3, text: 'Até 3 endereços em cópia.' }, { ok: !stranger || confirm, text: 'Destinatário confirmado.' }]}
      onConfirm={async () => {
        await emailQuote(quote.id, { to: toValue.trim(), cc: ccList, message: messageValue, confirmRecipient: confirm, version: quote.version }, idempotency.options)
        idempotency.renew()
        await onSent()
      }}
      trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Enviar por e-mail</button>}>
      <div className="mt-3 space-y-3">
        <label className="block space-y-1 text-sm font-medium">Para<input type="email" value={toValue} maxLength={254} onChange={(e) => { setTo(e.target.value); setConfirm(false) }} className={inputClass} /></label>
        <label className="block space-y-1 text-sm font-medium">Cópia (até 3, separados por vírgula)<input value={cc} onChange={(e) => { setCc(e.target.value); setConfirm(false) }} className={inputClass} /></label>
        {stranger ? (
          <label className="flex items-start gap-2 rounded border border-warn-soft bg-warn-soft px-2 py-1 text-sm text-warn-ink">
            <input type="checkbox" checked={confirm} onChange={(e) => setConfirm(e.target.checked)} className="mt-0.5 size-4 accent-primary" />
            <span>Este e-mail não está nos contatos de {quote.partyName}. Confirmo o destinatário.</span>
          </label>
        ) : null}
        <label className="block space-y-1 text-sm font-medium">Mensagem<textarea rows={6} maxLength={2000} value={messageValue} onChange={(e) => setMessage(e.target.value)} className={inputClass} /></label>
        <p className="flex flex-wrap items-center text-xs text-muted-foreground">
          Sem custo no PDF<Dica nome="Sem custo no PDF" texto="O PDF nunca mostra custo nem margem." />
          · Responder para<Dica nome="Responder para" texto="As respostas do cliente vão para o seu e-mail." />
        </p>
      </div>
    </ConfirmDialog>
  )
}
