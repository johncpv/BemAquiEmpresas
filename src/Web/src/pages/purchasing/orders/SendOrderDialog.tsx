import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { dispatchPurchaseOrder, getPurchaseOrderDispatchDraft } from '@/api/generated/bemaqui'
import type { PurchaseOrderDetail } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Dica } from '@/help/Dica'
import { downloadOrderPdf } from './order-pdf'
import { DISPATCH_DICAS, dispatchVerb, hasStranger, isResend, parseCc } from './purchase-orders'

const MAX_MESSAGE = 1000

interface Props {
  order: PurchaseOrderDetail
  open: boolean
  onOpenChange: (open: boolean) => void
  onSent: (saved: PurchaseOrderDetail, resend: boolean) => void
}

/**
 * "Enviar ao fornecedor" e "Reenviar" (CMP-03b CA 2, 4 a 6): destinatário padrão preenchido, até 3 cópias, assunto pronto, mensagem
 * opcional (texto puro, até 1.000 caracteres) e o PDF novo anexo; endereço fora dos contatos pede "Confirmo o destinatário". Tela cheia a
 * 400 px (diálogo grande). Passou do limite diário, o erro oferece "Baixar PDF".
 */
export function SendOrderDialog({ order, open, onOpenChange, onSent }: Props) {
  const resend = isResend(order)
  const words = dispatchVerb(resend)
  const draft = useQuery({ queryKey: ['purchase-order-dispatch-draft', order.id, order.version], queryFn: () => getPurchaseOrderDispatchDraft(order.id), enabled: open })
  const [to, setTo] = useState<string | null>(null)
  const [cc, setCc] = useState('')
  const [message, setMessage] = useState('')
  const [confirm, setConfirm] = useState(false)
  const idempotency = useIdempotencyKey()
  const toValue = to ?? draft.data?.to ?? ''
  const ccList = parseCc(cc)
  const stranger = !!draft.data && hasStranger([toValue, ...ccList], draft.data.supplierEmails)
  const reset = () => { setTo(null); setCc(''); setMessage(''); setConfirm(false) }

  return (
    <ConfirmDialog size="lg" verb={words.verb} done={words.done} title={words.title} open={open} onOpenChange={onOpenChange} onOpen={reset}
      description={draft.data ? `Assunto: ${draft.data.subject}. Vai um PDF novo do pedido, anexo. O e-mail não leva link para o sistema.` : 'Preparando o envio…'}
      preconditions={[
        { ok: toValue.trim().length > 0, text: 'Destinatário informado.' },
        { ok: ccList.length <= 3, text: 'Até 3 endereços em cópia.' },
        { ok: !stranger || confirm, text: 'Destinatário confirmado.' },
        { ok: message.length <= MAX_MESSAGE, text: 'Mensagem com até 1.000 caracteres.' },
      ]}
      recovery={{ verb: 'Baixar PDF', done: 'PDF baixado', when: (code) => !!code?.startsWith('email_dispatch'), onSelect: () => downloadOrderPdf(order) }}
      onConfirm={async () => {
        const saved = await dispatchPurchaseOrder(order.id, { to: toValue.trim(), cc: ccList, message: message.trim() || null, confirmRecipient: confirm, version: order.version }, idempotency.options)
        idempotency.renew()
        onSent(saved, resend)
      }}>
      {draft.isError ? <p role="alert" className="text-sm text-crit-ink">Não foi possível preparar o envio. Feche e tente de novo.</p> : null}
      {draft.data && !draft.data.companyComplete ? (
        <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">Os dados da empresa estão incompletos: o PDF sai sem eles. Complete em Configurações → Empresa.</p>
      ) : null}
      <label className="block space-y-1 text-sm font-medium">Para
        <input type="email" value={toValue} maxLength={254} autoComplete="off" onChange={(e) => { setTo(e.target.value); setConfirm(false) }} className={inputClass} />
      </label>
      <div className="space-y-1">
        <label htmlFor="copia-pedido" className="inline-flex items-center text-sm font-medium">Cópia (até 3, separados por vírgula)<Dica nome="Cópia" texto={DISPATCH_DICAS.copia} /></label>
        <input id="copia-pedido" value={cc} autoComplete="off" onChange={(e) => { setCc(e.target.value); setConfirm(false) }} className={inputClass} />
      </div>
      {stranger ? (
        <div className="flex items-start gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <input id="confirmar-destinatario" type="checkbox" checked={confirm} onChange={(e) => setConfirm(e.target.checked)} className="mt-0.5 size-4 shrink-0 accent-primary" />
          <label htmlFor="confirmar-destinatario">Este e-mail não está nos contatos de {draft.data?.supplierName ?? order.partyName}. Confirmo o destinatário.</label>
          <Dica nome="Confirmo o destinatário" texto={DISPATCH_DICAS.confirmar} />
        </div>
      ) : null}
      <label className="block space-y-1 text-sm font-medium">Mensagem (opcional)
        <textarea rows={5} maxLength={MAX_MESSAGE} value={message} onChange={(e) => setMessage(e.target.value)} placeholder="Ex.: Favor confirmar o prazo de entrega." className={inputClass} />
      </label>
      <p className="text-xs text-muted-foreground tabular-nums">{message.length}/{MAX_MESSAGE} · texto puro: links e formatação saem como texto. As respostas do fornecedor vão para o seu e-mail.</p>
    </ConfirmDialog>
  )
}
